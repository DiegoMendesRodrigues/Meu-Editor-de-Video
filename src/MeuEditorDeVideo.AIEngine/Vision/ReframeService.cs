using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Reenquadrar automaticamente: amostra o clipe (4 vezes por segundo), localiza a pessoa pela máscara do modelo de
/// segmentação (o mesmo ponto do rastreamento de câmera) e suaviza o caminho.
/// </summary>
public sealed class ReframeService(ISegmentationService segmentation, IVideoFrameSourceFactory frames) : IReframeService
{
    private static readonly MediaTime Step = MediaTime.FromSecondsApprox(0.25);

    public bool IsAvailable => segmentation.IsAvailable;

    public Task<IReadOnlyList<FocusSample>> AnalyzeAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, bool head,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<FocusSample>>(() =>
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { HasVideo: true } asset) return [];
        if (!paths.TryGetValue(id, out var path)) throw new InvalidOperationException("A mídia do clipe não foi encontrada.");

        var offsets = new List<MediaTime>();
        for (var o = MediaTime.Zero; o < clip.Duration; o += Step) offsets.Add(o);
        offsets.Add(clip.Duration);

        using var source = frames.Create(false);
        var samples = new List<FocusSample>(offsets.Count);
        (double X, double Y) last = (0.5, 0.5);
        for (var i = 0; i < offsets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = MediaTime.Min(clip.Start + offsets[i], clip.End - new MediaTime(1));
            var sourceTime = clip.FreezeAt ?? clip.SourceTimeAt(t);
            if (segmentation.IsAvailable && source.GetFrame(asset, path, sourceTime, 512, 512) is { } frame
                && segmentation.GetMatte(asset, path, sourceTime, frame, 0.5) is { } matte
                && CameraTracker.Target(matte, head) is { } target)
                last = target;
            samples.Add(new FocusSample(offsets[i], last.X, last.Y));
            progress?.Report((i + 1.0) / offsets.Count);
        }
        // Até a primeira detecção, usa o primeiro ponto encontrado (e não o centro).
        var firstFound = samples.FindIndex(s => s.X != 0.5 || s.Y != 0.5);
        for (var i = 0; i < firstFound; i++) samples[i] = samples[i] with { X = samples[firstFound].X, Y = samples[firstFound].Y };
        return Reframe.Smooth(samples, 3);
    }, cancellationToken);
}
