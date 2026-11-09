using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Detecção de cenas de um clipe (RF161): TransNetV2 quando o modelo está instalado; caso contrário, histogramas.
/// Os cortes voltam em tempo da timeline, alinhados aos quadros do projeto.
/// </summary>
public sealed class SceneDetectionService : ISceneDetectionService, IDisposable
{
    private readonly ModelSessionHolder<TransNetV2> _model;
    private readonly IVideoFrameSourceFactory _frames;

    public SceneDetectionService(IModelRegistry registry, OnnxSessionFactory sessions, IVideoFrameSourceFactory frames)
    {
        _frames = frames;
        _model = new ModelSessionHolder<TransNetV2>(registry, sessions, ModelCapability.SceneDetection, (s, _) => new TransNetV2(s.Session));
    }

    public bool ModelAvailable => _model.IsAvailable;

    public Task<SceneDetectionResult> DetectAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, double sensitivity = 0.5,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { Kind: MediaKind.Video } asset)
            throw new InvalidOperationException("Selecione um clipe de vídeo.");
        if (!paths.TryGetValue(id, out var path)) throw new InvalidOperationException("A mídia do clipe não foi encontrada.");

        // Um quadro por quadro do projeto dentro do clipe (respeita velocidade e corte).
        var rate = project.Settings.FrameRate;
        var first = rate.TimeToFrame(clip.Start);
        var last = rate.TimeToFrame(clip.End);
        var times = new List<MediaTime>();
        for (var f = first; f < last; f++) times.Add(rate.FrameToTime(f));

        var segmenter = _model.Get();
        var decodeShare = segmenter is null ? 1.0 : 0.6;
        using var source = _frames.Create(false);
        var thumbs = new List<byte[]>(times.Count);
        var hists = new List<float[]>(times.Count);
        byte[]? lastThumb = null;
        float[]? lastHist = null;
        for (var i = 0; i < times.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceTime = clip.SourceIn + clip.Speed.SourceDurationOf(times[i] - clip.Start);
            var frame = source.GetFrame(asset, path, sourceTime, 160, 90);
            if (frame is not null)
            {
                if (segmenter is not null) lastThumb = TransNetV2.Thumbnail(frame);
                else lastHist = HistogramSceneDetector.Histogram(frame);
            }
            if (segmenter is not null) thumbs.Add(lastThumb ?? new byte[TransNetV2.FrameWidth * TransNetV2.FrameHeight * 3]);
            else hists.Add(lastHist ?? new float[256]);
            progress?.Report(decodeShare * (i + 1) / times.Count);
        }

        var minGap = Math.Max(2, (int)Math.Round(rate.Value * 0.5));
        IReadOnlyList<int> cuts;
        if (segmenter is not null)
        {
            var inner = new Progress<double>(p => progress?.Report(decodeShare + (1 - decodeShare) * p));
            var predictions = segmenter.Predict(thumbs, inner, cancellationToken);
            cuts = TransNetV2.Cuts(predictions, 0.8 - 0.6 * Math.Clamp(sensitivity, 0, 1), minGap);
        }
        else
        {
            cuts = HistogramSceneDetector.Detect(hists, sensitivity, minGap);
        }
        progress?.Report(1);
        IReadOnlyList<MediaTime> result = cuts.Select(c => times[c]).Where(t => t > clip.Start && t < clip.End).ToList();
        return new SceneDetectionResult(result, segmenter is not null);
    }, cancellationToken);

    public void Dispose() => _model.Dispose();
}
