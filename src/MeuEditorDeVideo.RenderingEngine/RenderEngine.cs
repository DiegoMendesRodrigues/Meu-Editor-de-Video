using System.Diagnostics;
using System.Text.Json;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Subtitles;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Export;
using MeuEditorDeVideo.MediaEngine.Native;
using MeuEditorDeVideo.TimelineEngine;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>
/// Motor de renderização (§7.2): validar → montar o grafo → por quadro decodificar, compor e codificar →
/// multiplexar com o áudio mixado → validar o arquivo (probe e duração ± 1 quadro).
/// O vídeo é codificado em trechos de ~10 s gravados na pasta de trabalho; se a exportação for interrompida,
/// a próxima tentativa aproveita os trechos completos (renderização segmentada e retomável).
/// </summary>
public sealed class RenderEngine(
    IVideoFrameSourceFactory videoFactory,
    IAudioSampleSourceFactory audioFactory,
    ICompositor compositor,
    IMediaProbe probe,
    ILogger<RenderEngine> logger) : IRenderEngine
{
    /// <summary>Duração de cada trecho codificado (granularidade da retomada).</summary>
    public static readonly TimeSpan SegmentLength = TimeSpan.FromSeconds(10);

    private readonly FrameRenderer _frames = new(compositor);

    private const double MuxWeight = 0.04;
    private const string StateFile = "state.json";

    public IReadOnlyList<EncoderInfo> AvailableEncoders(VideoCodec codec) => EncoderCatalog.Available(codec);

    public Task<RenderResult> RenderAsync(RenderRequest request, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<RenderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(Render(request, progress, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        { IsBackground = true, Name = "Renderização", Priority = ThreadPriority.BelowNormal };
        thread.Start();
        return tcs.Task;
    }

    private sealed record WorkState(string Fingerprint, string Encoder);

    private sealed record Part(int Index, long StartFrame, long EndFrame, string Output)
    {
        public long Frames => EndFrame - StartFrame;
    }

    private RenderResult Render(RenderRequest req, IProgress<RenderProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var project = req.Project;
        var settings = req.Settings;
        var problems = ExportFormats.Validate(settings, project.Settings).ToList();
        if (problems.Count > 0) throw new MedException(MedErrors.InvalidExportSettings(problems));

        var sequence = project.Sequences.FirstOrDefault(s => s.Id == req.SequenceId) ?? project.MainSequence;
        var rangeEnd = req.RangeEnd is { } re && re < sequence.Duration ? re : sequence.Duration;
        var rangeStart = req.RangeStart < MediaTime.Zero ? MediaTime.Zero : req.RangeStart;
        if (rangeEnd <= rangeStart) throw new MedException(MedErrors.NothingToRender());

        // Mídias usadas precisam existir.
        var inUse = TimelineEvaluator.AssetsInUse(sequence);
        var missing = project.Media.Where(a => inUse.Contains(a.Id) &&
                (!req.MediaPaths.TryGetValue(a.Id, out var p) || !MediaExists(p, a)))
            .Select(a => a.Name).ToList();
        if (missing.Count > 0) throw new MedException(MedErrors.RenderMissingMedia(missing));

        var width = settings.ResolveWidth(project.Settings);
        var height = settings.ResolveHeight(project.Settings);
        var fps = settings.ResolveFrameRate(project.Settings);
        // Limites ajustados ao quadro mais próximo (evita um quadro extra por arredondamento de tempo).
        var firstFrame = fps.TimeToFrame(fps.SnapToFrame(rangeStart));
        var endFrame = Math.Max(firstFrame + 1, fps.TimeToFrame(fps.SnapToFrame(rangeEnd)));
        var totalFrames = endFrame - firstFrame;

        var parts = SplitParts(req.OutputPath, settings, fps, firstFrame, endFrame);
        var audioKbps = settings.AudioCodec == AudioCodec.None ? 0 : settings.AudioBitrateKbps;
        var bitrate = settings.TargetSizeMegabytes is { } mb
            ? Math.Min(settings.VideoBitrateKbps, ExportFormats.BitrateForTargetSize(mb, fps.FrameToTime(parts.Max(p => p.Frames)).TotalSeconds, audioKbps))
            : settings.VideoBitrateKbps;
        var spec = new VideoEncodeSpec(settings, width, height, fps, bitrate);

        Directory.CreateDirectory(req.WorkDirectory);
        foreach (var part in parts) EnsureWritable(part.Output);
        CheckDiskSpace(req, spec, fps.FrameToTime(totalFrames).TotalSeconds);

        var candidates = EncoderCatalog.Candidates(settings.VideoCodec, settings.Encoder, spec.Alpha).ToList();
        if (candidates.Count == 0) throw new MedException(MedErrors.EncoderUnavailable(ExportFormats.VideoCodecName(settings.VideoCodec)));

        // Retomada: mesmo pedido (configurações, intervalo e saída) e mesmo codificador.
        var fingerprint = Fingerprint(req, settings, firstFrame, endFrame);
        var state = ReadState(req.WorkDirectory);
        if (state is null || state.Fingerprint != fingerprint)
        {
            ClearSegments(req.WorkDirectory);
        }
        else if (candidates.Remove(state.Encoder))
        {
            candidates.Insert(0, state.Encoder);
        }

        var tracker = new ProgressTracker(progress, totalFrames, parts.Count, watch);
        tracker.Report(RenderStage.Preparing, 0, 1);

        string? encoder = null;
        for (var c = 0; c < candidates.Count; c++)
        {
            encoder = candidates[c];
            WriteState(req.WorkDirectory, new WorkState(fingerprint, encoder));
            try
            {
                EncodeAll(req, project, sequence, spec, encoder, parts, tracker, ct);
                break;
            }
            catch (FFmpegException ex) when (EncoderCatalog.IsHardware(encoder) && c + 1 < candidates.Count)
            {
                // Recuo automático: GPU indisponível/sem memória → próximo codificador (por fim, software).
                logger.LogWarning(ex, "Codificador {Encoder} falhou; tentando {Next}.", encoder, candidates[c + 1]);
                warnings.Add($"O codificador {EncoderCatalog.DisplayName(encoder)} falhou; foi usado {EncoderCatalog.DisplayName(candidates[c + 1])}.");
                ClearSegments(req.WorkDirectory);
                tracker.Reset();
            }
            catch (FFmpegException ex)
            {
                throw new MedException(EncoderCatalog.IsHardware(encoder)
                    ? MedErrors.GpuEncoderFailed(EncoderCatalog.DisplayName(encoder), ex)
                    : MedErrors.EncodeFailed(EncoderCatalog.DisplayName(encoder), ex));
            }
        }

        // Multiplexação e validação de cada arquivo.
        var files = new List<string>();
        using (var audioSource = audioFactory.Create())
        {
            var mixer = new AudioMixer(audioSource);
            foreach (var part in parts)
            {
                ct.ThrowIfCancellationRequested();
                var temp = part.Output + ".parcial";
                var partStart = fps.FrameToTime(part.StartFrame);
                var partDuration = fps.FrameToTime(part.EndFrame) - partStart;
                var audioStart = AudioFormat.ToFrames(partStart);
                var audioFrames = AudioFormat.ToFrames(partDuration);
                AudioFill? fill = settings.AudioCodec == AudioCodec.None ? null : (from, dest) =>
                {
                    mixer.Mix(project, sequence, req.MediaPaths, audioStart + from, dest);
                    return dest.Length / AudioFormat.Channels;
                };

                Guard(req.WorkDirectory, () => ExportMuxer.Mux(temp, settings, SegmentPaths(req.WorkDirectory, part, fps), audioFrames, fill,
                    f => tracker.Report(RenderStage.Muxing, part.Index, f), ct), encoder!);

                tracker.Report(RenderStage.Validating, part.Index, 1);
                Validate(temp, part.Output, spec, partDuration, fps);
                Guard(req.WorkDirectory, () => File.Move(temp, part.Output, overwrite: true), encoder!, part.Output);
                files.Add(part.Output);
            }
        }

        WriteSubtitleFiles(settings, sequence, parts, fps, warnings);
        TryDeleteDirectory(req.WorkDirectory);
        logger.LogInformation("Exportação concluída: {Files} arquivo(s), {Frames} quadros com {Encoder} em {Elapsed}.",
            files.Count, totalFrames, encoder, watch.Elapsed);
        return new RenderResult(files, EncoderCatalog.DisplayName(encoder!), watch.Elapsed, warnings);
    }

    /// <summary>
    /// Legendas em arquivo (.srt/.vtt) ao lado de cada parte exportada, com os tempos relativos ao início da parte
    /// e cortados ao intervalo dela (RF140/RF141 no sentido inverso: legendas para players e plataformas).
    /// </summary>
    public static IReadOnlyList<string> WriteSubtitleFiles(ExportSettings settings, Sequence sequence, IEnumerable<(string Output, MediaTime Start, MediaTime End)> parts,
        ICollection<string>? warnings = null)
    {
        var written = new List<string>();
        if (settings.SubtitleFile == SubtitleFileExport.None) return written;
        var entries = SubtitleOps.FromSequence(sequence);
        if (entries.Count == 0)
        {
            warnings?.Add("Não há legendas na sequência; o arquivo de legendas não foi criado.");
            return written;
        }
        var format = settings.SubtitleFile == SubtitleFileExport.Vtt ? SubtitleFormat.Vtt : SubtitleFormat.Srt;
        foreach (var (output, start, end) in parts)
        {
            var cues = entries.Where(e => e.End > start && e.Start < end)
                .Select(e => new SubtitleCue(MediaTime.Max(e.Start, start) - start, MediaTime.Min(e.End, end) - start, e.Text))
                .ToList();
            var path = Path.ChangeExtension(output, format == SubtitleFormat.Vtt ? ".vtt" : ".srt");
            try
            {
                SubtitleFile.Save(path, cues, format);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings?.Add($"Não foi possível gravar as legendas em \"{path}\": {ex.Message}");
            }
        }
        return written;
    }

    private static void WriteSubtitleFiles(ExportSettings settings, Sequence sequence, IReadOnlyList<Part> parts, FrameRate fps, List<string> warnings) =>
        WriteSubtitleFiles(settings, sequence, parts.Select(p => (p.Output, fps.FrameToTime(p.StartFrame), fps.FrameToTime(p.EndFrame))), warnings);

    // ------------------------------------------------------------------ codificação

    private void EncodeAll(RenderRequest req, Project project, Sequence sequence, VideoEncodeSpec spec, string encoder,
        IReadOnlyList<Part> parts, ProgressTracker tracker, CancellationToken ct)
    {
        var fps = spec.FrameRate;
        IVideoFrameSource? video = null;
        try
        {
            foreach (var part in parts)
            {
                foreach (var (path, start, end) in Segments(req.WorkDirectory, part, fps))
                {
                    ct.ThrowIfCancellationRequested();
                    if (VideoSegmentEncoder.CompletedFrames(path, encoder) == end - start)
                    {
                        tracker.Skip(end - start, part.Index);
                        continue;
                    }

                    video ??= videoFactory.Create(req.HardwareDecoding);
                    Guard(req.WorkDirectory, () =>
                    {
                        using var seg = new VideoSegmentEncoder(path, spec, encoder, start - part.StartFrame);
                        for (var f = start; f < end; f++)
                        {
                            ct.ThrowIfCancellationRequested();
                            seg.Write(RenderFrame(video, project, sequence, req.MediaPaths, spec, fps.FrameToTime(f)));
                            tracker.Frame(part.Index);
                        }
                        seg.Complete();
                    }, encoder);
                }
            }
        }
        finally
        {
            video?.Dispose();
        }
    }

    private BgraImage RenderFrame(IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        VideoEncodeSpec spec, MediaTime t) =>
        _frames.Render(video, project, sequence, paths, t, spec.Width, spec.Height, spec.Alpha,
            includeSubtitles: spec.Settings.BurnSubtitles);

    private static List<Part> SplitParts(string output, ExportSettings settings, FrameRate fps, long first, long end)
    {
        var perPart = settings.MaxSecondsPerFile is { } max ? Math.Max(1, (long)Math.Floor(max * fps.Value)) : long.MaxValue;
        var count = (int)Math.Max(1, (end - first + perPart - 1) / perPart);
        var parts = new List<Part>(count);
        for (var i = 0; i < count; i++)
        {
            var s = first + i * perPart;
            var e = Math.Min(end, s + perPart);
            parts.Add(new Part(i + 1, s, e, ExportFormats.PartPath(output, i + 1, count)));
        }
        return parts;
    }

    private static IEnumerable<(string Path, long Start, long End)> Segments(string dir, Part part, FrameRate fps)
    {
        var length = Math.Max(1, (long)Math.Round(SegmentLength.TotalSeconds * fps.Value));
        var k = 0;
        for (var s = part.StartFrame; s < part.EndFrame; s += length, k++)
            yield return (Path.Combine(dir, $"p{part.Index:000}_s{k:00000}.medseg"), s, Math.Min(part.EndFrame, s + length));
    }

    private static List<string> SegmentPaths(string dir, Part part, FrameRate fps) => Segments(dir, part, fps).Select(s => s.Path).ToList();

    // ------------------------------------------------------------------ validação e erros

    private void Validate(string temp, string final, VideoEncodeSpec spec, MediaTime expected, FrameRate fps)
    {
        var result = probe.ProbeAsync(temp).GetAwaiter().GetResult();
        if (!result.IsOk)
            throw new MedException(MedErrors.OutputValidationFailed(final, result.Error ?? "arquivo ilegível"));
        if (result.Width != spec.Width || result.Height != spec.Height)
            throw new MedException(MedErrors.OutputValidationFailed(final, $"resolução {result.Width}×{result.Height} diferente da esperada"));
        // ± 1 quadro (mais um quadro de áudio: codificadores completam o último bloco com silêncio).
        var tolerance = fps.FrameDuration.TotalSeconds + 2048.0 / AudioFormat.SampleRate;
        var diff = Math.Abs(result.Duration.TotalSeconds - expected.TotalSeconds);
        if (diff > tolerance)
            throw new MedException(MedErrors.OutputValidationFailed(final,
                $"duração {result.Duration.TotalSeconds:0.000} s, esperada {expected.TotalSeconds:0.000} s"));
    }

    /// <summary>Traduz falhas de E/S e memória para mensagens pt-BR (§7.4).</summary>
    private void Guard(string workDir, Action action, string encoder, string? output = null)
    {
        try
        {
            action();
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new MedException(MedErrors.RenderDiskFull(workDir, ex));
        }
        catch (IOException ex) when (output is not null)
        {
            throw new MedException(MedErrors.OutputNotWritable(output, ex));
        }
        catch (UnauthorizedAccessException ex) when (output is not null)
        {
            throw new MedException(MedErrors.OutputNotWritable(output, ex));
        }
        catch (OutOfMemoryException ex)
        {
            throw new MedException(MedErrors.RenderOutOfMemory(ex));
        }
        catch (FFmpegException ex) when (ex.Code == -28 /* ENOSPC */)
        {
            throw new MedException(MedErrors.RenderDiskFull(workDir, ex));
        }
        catch (EncoderSetupException ex)
        {
            throw new MedException(MedErrors.EncodeFailed(encoder, ex));
        }
    }

    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x70 or 0x27; // ERROR_DISK_FULL / HANDLE_DISK_FULL

    private static void EnsureWritable(string output)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var probePath = output + ".teste";
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose)) { }
            if (File.Exists(output))
            {
                using var _ = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MedException(MedErrors.OutputNotWritable(output, ex));
        }
    }

    /// <summary>Estimativa conservadora (trechos + arquivo final) para avisar antes de começar.</summary>
    private static void CheckDiskSpace(RenderRequest req, VideoEncodeSpec spec, double seconds)
    {
        var s = spec.Settings;
        double mbps = s.VideoCodec switch
        {
            VideoCodec.ProRes => spec.Width * spec.Height * spec.FrameRate.Value * (spec.Alpha ? 6.0 : 3.6) / 1e6,
            VideoCodec.Ffv1 or VideoCodec.QtAnimation => spec.Width * spec.Height * spec.FrameRate.Value * 12.0 / 1e6,
            VideoCodec.Gif => spec.Width * spec.Height * spec.FrameRate.Value * 2.0 / 1e6,
            _ when s.RateControl == RateControl.ConstantQuality => spec.Width * spec.Height * spec.FrameRate.Value * 0.3 / 1e6,
            _ => spec.VideoBitrateKbps * 1.5 / 1000,
        };
        var needed = (long)(mbps / 8 * 1e6 * seconds * 2) + 64L * 1024 * 1024;
        foreach (var dir in new[] { req.WorkDirectory, Path.GetDirectoryName(Path.GetFullPath(req.OutputPath))! })
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(dir));
                if (root is null) continue;
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < needed)
                    throw new MedException(MedErrors.RenderDiskFull(dir));
            }
            catch (ArgumentException)
            {
                // Caminho de rede: sem verificação prévia.
            }
        }
    }

    private static bool MediaExists(string path, MediaAsset asset) =>
        asset.Kind == MediaKind.ImageSequence ? Directory.Exists(Path.GetDirectoryName(path)) || File.Exists(path) : File.Exists(path);

    // ------------------------------------------------------------------ estado da retomada

    private static string Fingerprint(RenderRequest req, ExportSettings settings, long first, long end) =>
        JsonSerializer.Serialize(new
        {
            Settings = JsonSerializer.Serialize(settings),
            req.OutputPath,
            first,
            end,
            Project = req.Project.Id,
            Modified = req.Project.ModifiedAt,
        });

    private static WorkState? ReadState(string dir)
    {
        try
        {
            var path = Path.Combine(dir, StateFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<WorkState>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteState(string dir, WorkState state) =>
        File.WriteAllText(Path.Combine(dir, StateFile), JsonSerializer.Serialize(state));

    private static void ClearSegments(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*.medseg")) File.Delete(f);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A limpeza da pasta de trabalho não deve falhar a exportação.
        }
    }

    // ------------------------------------------------------------------ progresso

    /// <summary>Calcula fração, velocidade (quadros/s) e tempo restante; limita os relatórios a ~10 por segundo.</summary>
    private sealed class ProgressTracker(IProgress<RenderProgress>? progress, long totalFrames, int parts, Stopwatch watch)
    {
        private long _done;
        private long _encodedThisRun;
        private long _lastReport = -1000;
        private readonly double _sessionStart = watch.Elapsed.TotalSeconds;

        public void Reset()
        {
            _done = 0;
            _encodedThisRun = 0;
        }

        public void Skip(long frames, int part)
        {
            _done += frames;
            Report(RenderStage.Encoding, part, 0);
        }

        public void Frame(int part)
        {
            _done++;
            _encodedThisRun++;
            if (watch.ElapsedMilliseconds - _lastReport >= 100) Report(RenderStage.Encoding, part, 0);
        }

        public void Report(RenderStage stage, int part, double muxFraction)
        {
            if (progress is null) return;
            _lastReport = watch.ElapsedMilliseconds;
            var encodeShare = (double)_done / Math.Max(1, totalFrames);
            var fraction = stage switch
            {
                RenderStage.Preparing => 0,
                RenderStage.Encoding => encodeShare * (1 - MuxWeight),
                _ => (1 - MuxWeight) + MuxWeight * ((part - 1 + Math.Clamp(muxFraction, 0, 1)) / parts),
            };
            var seconds = watch.Elapsed.TotalSeconds - _sessionStart;
            var fpsRate = seconds > 0.2 ? _encodedThisRun / seconds : 0;
            TimeSpan? remaining = null;
            if (fraction > 0.01 && seconds > 1)
            {
                remaining = stage == RenderStage.Encoding && fpsRate > 0
                    ? TimeSpan.FromSeconds((totalFrames - _done) / fpsRate * (1 + MuxWeight))
                    : TimeSpan.FromSeconds(seconds / fraction * (1 - fraction));
            }
            progress.Report(new RenderProgress(stage, Math.Clamp(fraction, 0, 1), _done, totalFrames, fpsRate, watch.Elapsed, remaining, part, parts));
        }
    }
}
