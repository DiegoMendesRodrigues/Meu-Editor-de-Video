using System.IO;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>F7 de ponta a ponta com mídia real: loudness, silêncios, sincronização, tom preservado e exportação com efeitos.</summary>
[Collection(SampleMediaCollection.Name)]
public sealed class AudioRenderTests(SampleMedia media) : IDisposable
{
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);
    private readonly AudioSampleSourceFactory _sources = new(NullLoggerFactory.Instance);
    private readonly string _out = Path.Combine(media.Root, "áudio " + Guid.NewGuid().ToString("N")[..6]);

    private async Task<MediaAsset> AssetAsync(string path, string name)
    {
        var r = await _probe.ProbeAsync(path);
        return new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = name, OriginalPath = path,
            FileSize = new FileInfo(path).Length, LastWriteUtc = File.GetLastWriteTimeUtc(path),
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info,
        };
    }

    private static Project NewProject() =>
        Project.CreateNew("Áudio", new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }, StorageMode.Linked, DateTimeOffset.UtcNow);

    private string Generate(string name, string lavfi)
    {
        Directory.CreateDirectory(_out);
        var path = Path.Combine(_out, name);
        SampleMedia.Ffmpeg($"-f lavfi -i \"{lavfi}\" -ac 2 -ar 48000 \"{path}\"");
        return path;
    }

    /// <summary>Projeto com a mídia na primeira trilha de áudio (e no vídeo, se tiver) a partir de <paramref name="at"/>.</summary>
    private async Task<(Project P, Dictionary<Guid, string> Paths, Clip Clip)> SingleAsync(string path, double at = 0)
    {
        var asset = await AssetAsync(path, Path.GetFileNameWithoutExtension(path));
        var p = new AddMediaAssetsCommand([asset]).Apply(NewProject());
        var seq = p.MainSequence;
        var video = seq.Tracks.First(t => t.Kind == TrackKind.Video).Id;
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        p = new AddMediaToTimelineCommand(seq.Id, asset.Id, video, audio, MediaTime.FromSecondsApprox(at), MediaTime.FromSeconds(3)).Apply(p);
        var clip = p.MainSequence.Tracks.First(t => t.Kind == TrackKind.Audio).Clips[0];
        return (p, new Dictionary<Guid, string> { [asset.Id] = path }, clip);
    }

    private static Clip AudioClip(Project p, int track = 0, int index = 0) =>
        p.MainSequence.Tracks.Where(t => t.Kind == TrackKind.Audio).ElementAt(track).Clips[index];

    [Fact]
    public async Task Normalizacao_leva_o_loudness_ao_alvo()
    {
        var (p, paths, clip) = await SingleAsync(media.Video);
        var service = new AudioAnalysisService(_sources);

        var before = await service.MeasureLoudnessAsync(p, clip, paths);
        Assert.True(before.HasSignal);
        // Seno igual nos dois canais: LUFS ≈ pico em dBFS (a 1 kHz exato; a 440 Hz a ponderação K tira ~0,6 dB).
        Assert.InRange(before.IntegratedLufs, before.PeakDbfs - 1.5, before.PeakDbfs + 0.5);

        var gain = LoudnessMeter.NormalizationGainDb(before.IntegratedLufs, before.PeakDbfs, -14);
        p = new UpdateClipCommand(p.MainSequence.Id, clip.Id, c => ClipAnimation.ScaleVolume(c, Math.Pow(10, gain / 20)), "normalizar").Apply(p);
        var after = await service.MeasureLoudnessAsync(p, AudioClip(p), paths);
        Assert.InRange(after.IntegratedLufs, -14.5, -13.5);
        Assert.True(after.PeakDbfs <= -0.9);
    }

    [Fact]
    public async Task Silencio_detectado_e_removido_com_o_video_vinculado()
    {
        var path = Generate("fala com pausa.wav", "sine=frequency=330:sample_rate=48000:duration=3,volume='if(between(t,1,2),0,1)':eval=frame");
        var asset = await AssetAsync(path, "fala");
        var p = new AddMediaAssetsCommand([asset]).Apply(NewProject());
        var seq = p.MainSequence;
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        p = new AddMediaToTimelineCommand(seq.Id, asset.Id, null, audio, MediaTime.Zero, MediaTime.FromSeconds(3)).Apply(p);
        var paths = new Dictionary<Guid, string> { [asset.Id] = path };
        var clip = AudioClip(p);

        var ranges = await new AudioAnalysisService(_sources).DetectSilenceAsync(p, clip, paths, -40,
            MediaTime.FromSecondsApprox(0.5), MediaTime.FromSecondsApprox(0.15));
        var r = Assert.Single(ranges);
        Assert.InRange(r.Start.TotalSeconds, 1.1, 1.2);
        Assert.InRange(r.End.TotalSeconds, 1.8, 1.9);

        p = new RemoveTimeRangesCommand(seq.Id, clip.Id, ranges).Apply(p);
        var clips = p.MainSequence.Tracks.First(t => t.Kind == TrackKind.Audio).Clips;
        Assert.Equal(2, clips.Count);
        Assert.Equal(clips[0].End, clips[1].Start);
        Assert.InRange(p.MainSequence.Duration.TotalSeconds, 2.25, 2.4);
    }

    [Fact]
    public async Task Sincronizacao_encontra_o_deslocamento_entre_duas_gravacoes()
    {
        // "Câmera" e "gravador": o mesmo som; o gravador começou 1,5 s depois.
        var camera = Generate("camera.wav", "anoisesrc=d=8:c=pink:r=48000:a=0.3:seed=7");
        var recorder = Path.Combine(_out, "gravador.wav");
        SampleMedia.Ffmpeg($"-ss 1.5 -i \"{camera}\" -t 5 -c:a pcm_s16le \"{recorder}\"");

        var a = await AssetAsync(camera, "câmera");
        var b = await AssetAsync(recorder, "gravador");
        var p = new AddMediaAssetsCommand([a, b]).Apply(NewProject());
        var seq = p.MainSequence;
        p = new AddTrackCommand(seq.Id, TrackKind.Audio, "Áudio 2").Apply(p);
        var tracks = p.MainSequence.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
        p = new AddMediaToTimelineCommand(seq.Id, a.Id, null, tracks[0].Id, MediaTime.FromSeconds(1), MediaTime.FromSeconds(3)).Apply(p);
        p = new AddMediaToTimelineCommand(seq.Id, b.Id, null, tracks[1].Id, MediaTime.FromSeconds(6), MediaTime.FromSeconds(3)).Apply(p);
        var paths = new Dictionary<Guid, string> { [a.Id] = camera, [b.Id] = recorder };

        var result = await new AudioAnalysisService(_sources).FindSyncAsync(p, AudioClip(p, 0), AudioClip(p, 1), paths);
        Assert.NotNull(result);
        // 1 s (início da câmera) + 1,5 s = 2,5 s, alinhado à grade de quadros de 25 FPS (±1 quadro).
        Assert.InRange(result.NewStart.TotalSeconds, 2.46, 2.54);
        Assert.True(result.Confidence > 0.8, $"{result.Confidence}");
    }

    [Fact]
    public async Task Velocidade_dobrada_mantem_o_tom_quando_pedido()
    {
        var (p, paths, clip) = await SingleAsync(media.Video);
        p = new SetClipSpeedCommand(p.MainSequence.Id, clip.Id, new Rational(2, 1)).Apply(p);

        double Frequency(bool preserve)
        {
            var q = new UpdateClipCommand(p.MainSequence.Id, AudioClip(p).Id, c => c with { PreservePitch = preserve }, "tom").Apply(p);
            using var source = _sources.Create();
            var mixer = new AudioMixer(source);
            var buffer = new float[48000 * 2];
            mixer.Mix(q, q.MainSequence, paths, 0, buffer);
            var crossings = 0;
            for (var i = 4800 + 1; i < 43200; i++)
                if (buffer[(i - 1) * 2] < 0 && buffer[i * 2] >= 0) crossings++;
            return crossings / 0.8;
        }

        Assert.InRange(Frequency(preserve: true), 425, 455);
        Assert.InRange(Frequency(preserve: false), 860, 900);
    }

    [Fact]
    public async Task Exportacao_com_efeitos_de_audio_e_mixer()
    {
        var (p, paths, clip) = await SingleAsync(media.Video);
        var seq = p.MainSequence.Id;
        p = new UpdateClipCommand(seq, clip.Id, c => c with
        {
            Effects =
            [
                EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Equalizer), "lowcut", 120),
                EffectCatalog.Create(EffectCatalog.Compressor),
                EffectCatalog.Create(EffectCatalog.Denoise),
                EffectCatalog.Create(EffectCatalog.Reverb),
                EffectCatalog.Create(EffectCatalog.Echo),
            ],
            FadeOut = MediaTime.FromSeconds(1),
        }, "efeitos").Apply(p);
        var track = p.MainSequence.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        p = UpdateTrackCommand.SetPan(seq, track, -0.5).Apply(p);

        var engine = new RenderEngine(new VideoFrameSourceFactory(NullLoggerFactory.Instance), _sources, new CpuCompositor(), _probe,
            NullLogger<RenderEngine>.Instance);
        var settings = new ExportSettings { Container = ExportContainer.Mkv, VideoCodec = VideoCodec.H264, AudioCodec = AudioCodec.Flac, Encoder = EncoderPreference.Software };
        var output = Path.Combine(_out, "efeitos.mkv");
        await engine.RenderAsync(new RenderRequest
        {
            Project = p, SequenceId = seq, MediaPaths = paths, Settings = settings, OutputPath = output,
            WorkDirectory = Path.Combine(_out, "trabalho"), HardwareDecoding = false,
        });

        var probe = await _probe.ProbeAsync(output);
        Assert.Equal(3.0, probe.Duration.TotalSeconds, 1);

        // O áudio exportado tem som, está mais à esquerda (pan) e termina em silêncio (saída gradual).
        var asset = await AssetAsync(output, "exportado");
        using var source = _sources.Create();
        var all = new float[48000 * 3 * 2];
        var read = source.Read(asset, output, MediaTime.Zero, all);
        Assert.True(read > 48000 * 2.8);
        double l = 0, r = 0;
        for (var i = 0; i < 48000 * 2; i++)
        {
            l += all[i * 2] * all[i * 2];
            r += all[i * 2 + 1] * all[i * 2 + 1];
        }
        Assert.True(l > r * 2, $"L={l} R={r}");
        var tail = all.AsSpan((read - 480) * 2, 480 * 2);
        var tailPeak = 0f;
        foreach (var v in tail) tailPeak = Math.Max(tailPeak, Math.Abs(v));
        Assert.True(tailPeak < 0.01f, $"{tailPeak}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
