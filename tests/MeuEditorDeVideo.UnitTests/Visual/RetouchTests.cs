using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Visual;

/// <summary>Retocar (aba Vídeo): beleza e maquiagem sobre a malha de pontos do rosto.</summary>
public sealed class RetouchTests
{
    private static EffectInstance Effect(string type, params (string Key, double Value)[] values) =>
        values.Aggregate(EffectCatalog.Create(type), (e, v) => EffectCatalog.WithValue(e, v.Key, v.Value));

    private static BgraImage Gradient(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                px[i] = (byte)(60 + x % 120); px[i + 1] = (byte)(90 + y % 100); px[i + 2] = (byte)(150 + (x + y) % 90); px[i + 3] = 255;
            }
        return new BgraImage(w, h, px);
    }

    /// <summary>Malha falsa: 478 pontos espalhados (semente fixa) num rosto de 80×100 px centrado em (100, 100).</summary>
    private static FaceLandmarks FakeFace()
    {
        var rnd = new Random(7);
        var xs = new float[478];
        var ys = new float[478];
        for (var k = 0; k < 478; k++)
        {
            xs[k] = 60 + (float)rnd.NextDouble() * 80;
            ys[k] = 50 + (float)rnd.NextDouble() * 100;
        }
        // Contorno e pontos de referência coerentes (largura e altura do rosto).
        (xs[234], ys[234], xs[454], ys[454]) = (60, 100, 140, 100);
        (xs[10], ys[10], xs[152], ys[152]) = (100, 50, 100, 150);
        return new FaceLandmarks(xs, ys);
    }

    [Fact]
    public void Padroes_de_beleza_e_maquiagem_nao_sao_neutros_e_zerados_sao()
    {
        Assert.False(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.Beauty)));
        Assert.False(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.Makeup)));
        var beauty = EffectCatalog.Find(EffectCatalog.Beauty)!.Parameters.Aggregate(EffectCatalog.Create(EffectCatalog.Beauty),
            (e, p) => EffectCatalog.WithValue(e, p.Key, 0));
        Assert.True(EffectCatalog.IsNeutral(beauty));
        Assert.True(EffectCatalog.IsNeutral(Effect(EffectCatalog.Makeup, ("look", 0))));
        Assert.False(EffectCatalog.IsNeutral(Effect(EffectCatalog.Makeup, ("look", 0), ("lipstick", 40))));
    }

    [Theory]
    [InlineData(EffectCatalog.Beauty)]
    [InlineData(EffectCatalog.Makeup)]
    public void Retoque_so_altera_a_regiao_do_rosto(string type)
    {
        var image = Gradient(300, 220);
        var effect = type == EffectCatalog.Beauty
            ? Effect(type, ("eyes", 50), ("face", 50))
            : Effect(type, ("look", 2), ("freckles", 80), ("eyecolor", 80));
        var result = type == EffectCatalog.Beauty ? FaceRetouch.Beauty(image, FakeFace(), effect) : FaceRetouch.Makeup(image, FakeFace(), effect);

        var (cx, cy, side) = FaceLandmarkService.Roi(FakeFace());
        var changed = false;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                var same = image.Pixels.AsSpan(i, 4).SequenceEqual(result.Pixels.AsSpan(i, 4));
                var inside = Math.Abs(x - cx) <= side / 2 + 1 && Math.Abs(y - cy) <= side / 2 + 1;
                if (!inside) Assert.True(same, $"pixel fora do rosto alterado: {x},{y}");
                else changed |= !same;
            }
        Assert.True(changed);
    }

    [Fact]
    public void Sem_rosto_detectado_o_quadro_fica_igual()
    {
        var image = Gradient(64, 64);
        var processor = new AiFrameProcessor(new NoSegmentation(), new NoFaces());
        var asset = new MediaAsset { Id = Guid.NewGuid(), Kind = MediaKind.Image, Name = "x", OriginalPath = "x.png" };
        var (result, alpha) = processor.Apply(EffectCatalog.Create(EffectCatalog.Beauty), image, asset, null, default, (_, _) => null, 1);
        Assert.Same(image, result);
        Assert.False(alpha);
    }

    [Fact]
    public void Cabeca_pela_mascara_fica_no_alto_da_silhueta()
    {
        // Pessoa de 20×60 px no lado direito (linhas 30–89) de um quadro 100×100.
        var alpha = new byte[100 * 100];
        for (var y = 30; y < 90; y++)
            for (var x = 70; x < 90; x++) alpha[y * 100 + x] = 255;
        var head = FaceLandmarkService.HeadRoi(new Matte(100, 100, alpha))!.Value;
        Assert.InRange(head.X, 0.78, 0.81);
        Assert.InRange(head.Y, 0.30, 0.45);
        Assert.True(head.Side > 0.2);
        Assert.Null(FaceLandmarkService.HeadRoi(new Matte(10, 10, new byte[100])));
    }

    private sealed class NoFaces : IFaceLandmarkService
    {
        public bool IsAvailable => false;
        public FaceLandmarks? Detect(MediaAsset asset, string? path, MeuEditorDeVideo.Domain.Time.MediaTime sourceTime, BgraImage frame) => null;
    }

    private sealed class NoSegmentation : ISegmentationService
    {
        public bool IsAvailable => false;
        public Matte? GetMatte(MediaAsset asset, string? path, MeuEditorDeVideo.Domain.Time.MediaTime sourceTime, BgraImage frame, double temporalSmoothing = 0) => null;
        public Task PrecomputeAsync(Project p, Clip c, IReadOnlyDictionary<Guid, string> paths, IProgress<double>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TrackSample>> TrackPersonAsync(Project p, Clip c, IReadOnlyDictionary<Guid, string> paths, IProgress<double>? progress = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TrackSample>>([]);
        public void ClearCache(Guid assetId) { }
    }
}
