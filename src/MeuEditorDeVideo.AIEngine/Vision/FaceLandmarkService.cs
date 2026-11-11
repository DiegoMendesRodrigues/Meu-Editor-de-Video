using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Pontos do rosto para o retoque (beleza e maquiagem). A cabeça é localizada pela máscara da pessoa (modelo de
/// segmentação, quando instalado) ou pelo rosto do quadro anterior; a malha é calculada nesse recorte e refinada com
/// um segundo recorte em torno dos pontos encontrados. Os resultados recentes ficam em cache por quadro.
/// </summary>
public sealed class FaceLandmarkService : IFaceLandmarkService, IDisposable
{
    private readonly ModelSessionHolder<FaceMeshDetector> _model;
    private readonly ISegmentationService _segmentation;
    private readonly Lock _gate = new();
    private readonly LinkedList<((Guid, long, int, int) Key, FaceLandmarks? Value)> _cache = new();
    private (double X, double Y, double Side)? _lastRoi;

    public FaceLandmarkService(IModelRegistry registry, OnnxSessionFactory sessions, ISegmentationService segmentation)
    {
        _segmentation = segmentation;
        _model = new ModelSessionHolder<FaceMeshDetector>(registry, sessions, ModelCapability.FaceLandmarks,
            (s, _) => new FaceMeshDetector(s.Session));
    }

    public bool IsAvailable => _model.IsAvailable;

    public FaceLandmarks? Detect(MediaAsset asset, string? path, MediaTime sourceTime, BgraImage frame)
    {
        if (_model.Get() is not { } detector) return null;
        var key = (asset.Id, SegmentationService.FrameKey(asset, sourceTime), frame.Width, frame.Height);
        lock (_gate)
        {
            foreach (var item in _cache)
                if (item.Key == key) return item.Value;
        }

        var result = DetectIn(detector, frame, InitialRoi(asset, path, sourceTime, frame));
        lock (_gate)
        {
            _cache.AddFirst((key, result));
            while (_cache.Count > 64) _cache.RemoveLast();
        }
        return result;
    }

    private (double X, double Y, double Side) InitialRoi(MediaAsset asset, string? path, MediaTime t, BgraImage frame)
    {
        if (_segmentation.IsAvailable && _segmentation.GetMatte(asset, path, t, frame) is { } matte
            && HeadRoi(matte) is { } head)
            return (head.X * frame.Width, head.Y * frame.Height, head.Side * frame.Width);
        lock (_gate)
        {
            if (_lastRoi is { } last) return last;
        }
        // Sem máscara: quadrado central do quadro, um pouco acima do meio.
        var side = Math.Min(frame.Width, frame.Height) * 0.9;
        return (frame.Width / 2.0, frame.Height * 0.45, side);
    }

    private FaceLandmarks? DetectIn(FaceMeshDetector detector, BgraImage frame, (double X, double Y, double Side) roi)
    {
        if (detector.Detect(frame, roi.X, roi.Y, roi.Side) is not { } first) return Forget();
        // Refino: recorte justo em torno dos pontos (≈ 1,5× o rosto), como no pipeline original do MediaPipe.
        var refinedRoi = Roi(first.Points);
        if (refinedRoi.Side < 16 || detector.Detect(frame, refinedRoi.X, refinedRoi.Y, refinedRoi.Side) is not { Score: >= 0.5 } refined)
            return Forget();
        lock (_gate) _lastRoi = Roi(refined.Points);
        return refined.Points;

        FaceLandmarks? Forget()
        {
            lock (_gate) _lastRoi = null;
            return null;
        }
    }

    /// <summary>Recorte quadrado em torno dos pontos (centro e lado em pixels).</summary>
    public static (double X, double Y, double Side) Roi(FaceLandmarks p)
    {
        float minX = p.X.Min(), maxX = p.X.Max(), minY = p.Y.Min(), maxY = p.Y.Max();
        return ((minX + maxX) / 2.0, (minY + maxY) / 2.0, Math.Max(maxX - minX, maxY - minY) * 1.5);
    }

    /// <summary>
    /// Cabeça a partir da máscara da pessoa: faixa de cima da silhueta (≈ 22% da altura). Devolve o centro (frações
    /// do quadro) e o lado do recorte (fração da largura), ou null sem pessoa.
    /// </summary>
    public static (double X, double Y, double Side)? HeadRoi(Matte matte)
    {
        int w = matte.Width, h = matte.Height, top = -1, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (matte.Alpha[y * w + x] < 128) continue;
                if (top < 0) top = y;
                bottom = y;
                break;
            }
        }
        if (top < 0) return null;
        var band = Math.Max(2, (int)Math.Round((bottom - top + 1) * 0.22));
        var last = Math.Min(bottom, top + band);
        // Largura da cabeça: mediana das larguras das linhas da faixa (ignora ombros que entram no fim da faixa).
        var widths = new List<int>();
        double sx = 0;
        long n = 0;
        for (var y = top; y <= last; y++)
        {
            int l = -1, r = -1;
            for (var x = 0; x < w; x++)
            {
                if (matte.Alpha[y * w + x] < 128) continue;
                if (l < 0) l = x;
                r = x;
                sx += x;
                n++;
            }
            if (l >= 0) widths.Add(r - l + 1);
        }
        if (n == 0 || widths.Count == 0) return null;
        widths.Sort();
        var headW = Math.Max(widths[widths.Count / 2], (last - top + 1) * 0.6);
        var cy = top + Math.Min(last - top, headW * 0.6);
        return (sx / n / w, cy / h, headW * 1.7 / w);
    }

    public void Dispose() => _model.Dispose();
}
