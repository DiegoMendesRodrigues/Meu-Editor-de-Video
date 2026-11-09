using System.Globalization;
using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Segmentação de pessoas (RF156, RF162) com cache de máscaras, pré-cálculo em segundo plano e rastreamento
/// inteligente a partir da caixa da pessoa em cada quadro (RF163).
/// </summary>
public sealed class SegmentationService : ISegmentationService, IDisposable
{
    /// <summary>Tamanho máximo do quadro decodificado para o pré-cálculo e o rastreamento.</summary>
    private const int AnalysisWidth = 960, AnalysisHeight = 540;

    private readonly ModelSessionHolder<ModnetSegmenter> _model;
    private readonly IVideoFrameSourceFactory _frames;
    private readonly object _decodeGate = new();
    private IVideoFrameSource? _decoder;
    private readonly List<Guid> _recent = [];

    public SegmentationService(IModelRegistry registry, OnnxSessionFactory sessions, IVideoFrameSourceFactory frames, MatteCache cache)
    {
        _frames = frames;
        Cache = cache;
        _model = new ModelSessionHolder<ModnetSegmenter>(registry, sessions, ModelCapability.PersonSegmentation, (s, m) =>
            new ModnetSegmenter(s.Session, m.Manifest.Parameters.TryGetValue("referenceSize", out var r)
                && int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 512));
    }

    public MatteCache Cache { get; }

    public bool IsAvailable => _model.IsAvailable;

    /// <summary>Quadro de origem usado como chave do cache (imagens: sempre 0).</summary>
    public static long FrameKey(MediaAsset asset, MediaTime sourceTime) =>
        asset.Kind == MediaKind.Image ? 0 : (asset.FrameRate ?? FrameRate.Fps30).TimeToFrame(sourceTime);

    public Matte? GetMatte(MediaAsset asset, string? path, MediaTime sourceTime, BgraImage frame, double temporalSmoothing = 0)
    {
        var key = FrameKey(asset, sourceTime);
        if (Raw(asset, path, key, frame) is not { } center) return null;
        var radius = asset.Kind == MediaKind.Image || path is null ? 0 : MatteSmoothing.Radius(temporalSmoothing);
        if (radius == 0) return center;

        var last = LastFrame(asset);
        var neighbors = new List<(int, Matte)>(2 * radius);
        for (var k = -radius; k <= radius; k++)
        {
            var f = key + k;
            if (k == 0 || f < 0 || f > last) continue;
            if (Raw(asset, path, f, null) is { } m) neighbors.Add((k, m));
        }
        return MatteSmoothing.Blend(center, neighbors, temporalSmoothing);
    }

    /// <summary>Máscara sem suavização do quadro de origem <paramref name="key"/> (cache ou modelo).</summary>
    private Matte? Raw(MediaAsset asset, string? path, long key, BgraImage? fallback)
    {
        if (Cache.Get(asset.Id, key) is { } cached) return cached;
        if (_model.Get() is not { } segmenter) return null;
        var input = (path is null ? null : Decode(asset, path, key)) ?? fallback;
        if (input is null) return null;
        var matte = segmenter.Segment(input);
        Cache.Put(asset.Id, key, matte);
        return matte;
    }

    /// <summary>Lê o quadro de origem no tamanho de análise com um decodificador próprio (acesso sequencial eficiente).</summary>
    private BgraImage? Decode(MediaAsset asset, string path, long key)
    {
        var time = asset.Kind == MediaKind.Image ? MediaTime.Zero : (asset.FrameRate ?? FrameRate.Fps30).FrameToTime(key);
        lock (_decodeGate)
        {
            _decoder ??= _frames.Create(false);
            // Mantém abertos só os decodificadores das últimas mídias usadas.
            _recent.Remove(asset.Id);
            _recent.Insert(0, asset.Id);
            if (_recent.Count > 3)
            {
                _recent.RemoveRange(3, _recent.Count - 3);
                _decoder.Trim(_recent.ToHashSet());
            }
            return _decoder.GetFrame(asset, path, time, AnalysisWidth, AnalysisHeight);
        }
    }

    private static long LastFrame(MediaAsset asset) =>
        asset.Duration > MediaTime.Zero ? Math.Max(0, (asset.FrameRate ?? FrameRate.Fps30).TimeToFrame(asset.Duration) - 1) : long.MaxValue;

    public Task PrecomputeAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var (asset, path, times) = Prepare(project, clip, paths);
        using var source = _frames.Create(false);
        for (var i = 0; i < times.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = FrameKey(asset, times[i]);
            if (!Cache.Contains(asset.Id, key) && source.GetFrame(asset, path, times[i], AnalysisWidth, AnalysisHeight) is { } frame)
                Raw(asset, null, key, frame);
            progress?.Report((i + 1.0) / times.Count);
        }
    }, cancellationToken);

    public Task<IReadOnlyList<TrackSample>> TrackPersonAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var (asset, path, times) = Prepare(project, clip, paths);
        using var source = _frames.Create(false);
        var samples = new List<TrackSample>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = FrameKey(asset, times[i]);
            var matte = Cache.Get(asset.Id, key);
            if (matte is null && source.GetFrame(asset, path, times[i], AnalysisWidth, AnalysisHeight) is { } frame)
                matte = Raw(asset, null, key, frame);
            if (matte is not null && ModnetSegmenter.Bounds(matte) is { } b)
                samples.Add(new TrackSample(times[i], b.X, b.Y, b.W, b.H));
            progress?.Report((i + 1.0) / times.Count);
        }
        return (IReadOnlyList<TrackSample>)samples;
    }, cancellationToken);

    public void ClearCache(Guid assetId) => Cache.Clear(assetId);

    private (MediaAsset Asset, string Path, IReadOnlyList<MediaTime> Times) Prepare(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths)
    {
        if (!IsAvailable) throw new InvalidOperationException("O modelo de segmentação de pessoas não está instalado.");
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { } asset || asset.Kind is not (MediaKind.Video or MediaKind.Image))
            throw new InvalidOperationException("Selecione um clipe de vídeo ou imagem.");
        if (!paths.TryGetValue(id, out var path)) throw new InvalidOperationException("A mídia do clipe não foi encontrada.");
        IReadOnlyList<MediaTime> times = asset.Kind == MediaKind.Image
            ? [MediaTime.Zero]
            : CompositionOps.SourceFrames(clip, asset.FrameRate ?? project.Settings.FrameRate, clip.SourceIn, clip.SourceOut);
        return (asset, path, times);
    }

    public void Dispose()
    {
        _model.Dispose();
        lock (_decodeGate)
        {
            _decoder?.Dispose();
            _decoder = null;
        }
    }
}
