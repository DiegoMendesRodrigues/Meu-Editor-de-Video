using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>Análises de áudio de clipes: renderiza o clipe pela mesma cadeia da mixagem e mede o resultado.</summary>
public sealed class AudioAnalysisService(IAudioSampleSourceFactory factory) : IAudioAnalysisService
{
    private const int Chunk = 8192;

    /// <summary>Limite de áudio analisado na sincronização (por clipe).</summary>
    public static readonly MediaTime MaxSyncDuration = MediaTime.FromSeconds(60);

    public Task<LoudnessMeasurement> MeasureLoudnessAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var meter = new LoudnessMeter();
        RenderClip(project, clip, paths, applyGain: true, clip.Duration, s => meter.Add(s), cancellationToken);
        return new LoudnessMeasurement(meter.IntegratedLufs, meter.PeakDbfs);
    }, cancellationToken);

    public Task<IReadOnlyList<TimeRange>> DetectSilenceAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        double thresholdDb, MediaTime minDuration, MediaTime padding, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var detector = new SilenceDetector();
        RenderClip(project, clip, paths, applyGain: false, clip.Duration, s => detector.Add(s), cancellationToken);
        var ranges = detector.Detect(thresholdDb, AudioFormat.ToFrames(minDuration), AudioFormat.ToFrames(padding));
        IReadOnlyList<TimeRange> result = ranges
            .Select(r => new TimeRange(clip.Start + AudioFormat.FromFrames(r.Start), clip.Start + AudioFormat.FromFrames(r.End)))
            .ToList();
        return result;
    }, cancellationToken);

    public Task<AudioSyncResult?> FindSyncAsync(Project project, Clip reference, Clip other, IReadOnlyDictionary<Guid, string> paths,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var a = Analysis(project, reference, paths, cancellationToken);
        var b = Analysis(project, other, paths, cancellationToken);
        if (a.Length < AudioCorrelator.AnalysisRate / 2 || b.Length < AudioCorrelator.AnalysisRate / 2) return null;
        var (lag, confidence) = AudioCorrelator.FindLag(a, b, Math.Max(a.Length, b.Length));
        if (confidence < 0.1) return null;
        var start = reference.Start + MediaTime.FromSecondsApprox((double)lag / AudioCorrelator.AnalysisRate);
        return (AudioSyncResult?)new AudioSyncResult(project.Settings.FrameRate.SnapToFrame(start), confidence);
    }, cancellationToken);

    private double[] Analysis(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, CancellationToken ct)
    {
        var duration = MediaTime.Min(clip.Duration, MaxSyncDuration);
        var all = new List<float>((int)AudioFormat.ToFrames(duration) * 2);
        RenderClip(project, clip, paths, applyGain: false, duration, s =>
        {
            foreach (var v in s) all.Add(v);
        }, ct);
        return AudioCorrelator.ToAnalysis(all.ToArray());
    }

    private delegate void ChunkSink(ReadOnlySpan<float> samples);

    private void RenderClip(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, bool applyGain, MediaTime duration,
        ChunkSink sink, CancellationToken ct)
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { HasAudio: true } asset)
            throw new InvalidOperationException("O clipe não tem áudio.");
        if (!paths.TryGetValue(id, out var path))
            throw new InvalidOperationException("A mídia do clipe não foi encontrada.");

        using var source = factory.Create();
        var stream = new ClipStream(source) { ApplyGainEnvelope = applyGain };
        var start = AudioFormat.ToFrames(clip.Start);
        var total = AudioFormat.ToFrames(duration);
        var buffer = new float[Chunk * AudioFormat.Channels];
        for (long done = 0; done < total; done += Chunk)
        {
            ct.ThrowIfCancellationRequested();
            var n = (int)Math.Min(Chunk, total - done);
            var span = buffer.AsSpan(0, n * AudioFormat.Channels);
            stream.Render(clip, asset, path, start + done, span);
            sink(span);
        }
    }
}

public static class AudioEngineServiceCollectionExtensions
{
    public static IServiceCollection AddAudioEngine(this IServiceCollection services)
    {
        services.AddSingleton<IAudioAnalysisService, AudioAnalysisService>();
        return services;
    }
}
