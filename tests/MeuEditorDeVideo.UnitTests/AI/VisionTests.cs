using System.IO;
using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.App.ViewModels.Inspector;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using OpenCvSharp;

namespace MeuEditorDeVideo.UnitTests.AI;

public sealed class VisionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-vision-" + Guid.NewGuid().ToString("N")[..8]);

    public VisionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static BgraImage Solid(int w, int h, byte b, byte g, byte r, byte a = 255)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = b;
            px[i + 1] = g;
            px[i + 2] = r;
            px[i + 3] = a;
        }
        return new BgraImage(w, h, px);
    }

    /// <summary>Máscara com a metade esquerda como pessoa (255) e a direita como fundo (0).</summary>
    private static Matte HalfMatte(int w, int h)
    {
        var a = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w / 2; x++) a[y * w + x] = 255;
        return new Matte(w, h, a);
    }

    private static EffectInstance Effect(int mode, int keep = 0, double feather = 0) =>
        EffectCatalog.WithValue(EffectCatalog.WithValue(EffectCatalog.WithValue(
            EffectCatalog.Create(EffectCatalog.AiBackground), "mode", mode), "keep", keep), "feather", feather);

    private static byte[] Px(BgraImage img, int x, int y) => img.Pixels.AsSpan((y * img.Width + x) * 4, 4).ToArray();

    // ---------------------------------------------------------------- composição do fundo

    [Fact]
    public void Fundo_transparente_zera_o_alfa_do_fundo_e_mantem_a_pessoa()
    {
        var img = Solid(40, 20, 10, 20, 30);
        var (result, alpha) = BackgroundCompositor.Apply(img, HalfMatte(40, 20), Effect(0), (_, _) => null, 1);
        Assert.True(alpha);
        Assert.Equal(255, Px(result, 5, 10)[3]);
        Assert.Equal(0, Px(result, 35, 10)[3]);
        Assert.Equal(new byte[] { 10, 20, 30 }, Px(result, 35, 10)[..3]);
    }

    [Fact]
    public void Manter_o_fundo_inverte_a_mascara()
    {
        var (result, _) = BackgroundCompositor.Apply(Solid(40, 20, 10, 20, 30), HalfMatte(40, 20), Effect(0, keep: 1), (_, _) => null, 1);
        Assert.Equal(0, Px(result, 5, 10)[3]);
        Assert.Equal(255, Px(result, 35, 10)[3]);
    }

    [Fact]
    public void Cor_solida_substitui_o_fundo_e_mantem_opaco()
    {
        var effect = EffectCatalog.WithColor(Effect(1), "color", 1, 0, 0);
        var (result, alpha) = BackgroundCompositor.Apply(Solid(40, 20, 10, 20, 30), HalfMatte(40, 20), effect, (_, _) => null, 1);
        Assert.False(alpha);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(result, 35, 10));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, Px(result, 5, 10));
    }

    [Fact]
    public void Imagem_de_fundo_usa_o_quadro_fornecido_e_sem_quadro_fica_transparente()
    {
        var bg = Solid(40, 20, 200, 100, 50);
        var (result, _) = BackgroundCompositor.Apply(Solid(40, 20, 10, 20, 30), HalfMatte(40, 20), Effect(3), (w, h) => w == 40 && h == 20 ? bg : null, 1);
        Assert.Equal(new byte[] { 200, 100, 50, 255 }, Px(result, 35, 10));
        var (none, alpha) = BackgroundCompositor.Apply(Solid(40, 20, 10, 20, 30), HalfMatte(40, 20), Effect(3), (_, _) => null, 1);
        Assert.True(alpha);
        Assert.Equal(0, Px(none, 35, 10)[3]);
    }

    [Fact]
    public void Desfoque_mistura_o_fundo_e_preserva_a_pessoa()
    {
        // Fundo listrado: o desfoque deixa as listras próximas da média.
        var img = Solid(64, 32, 0, 0, 0);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 64; x++)
                if (x % 2 == 0)
                    img.Pixels[(y * 64 + x) * 4] = img.Pixels[(y * 64 + x) * 4 + 1] = img.Pixels[(y * 64 + x) * 4 + 2] = 255;
        var effect = EffectCatalog.WithValue(Effect(2), "blur", 20);
        var (result, _) = BackgroundCompositor.Apply(img, HalfMatte(64, 32), effect, (_, _) => null, 1);
        var v = Px(result, 50, 16)[0];
        Assert.InRange(v, 80, 175);
        Assert.Equal(img.Pixels[(16 * 64 + 4) * 4], Px(result, 4, 16)[0]);
    }

    [Fact]
    public void Mascara_e_redimensionada_e_a_suavizacao_cria_transicao()
    {
        var m = BackgroundCompositor.RefineMatte(HalfMatte(10, 5), 100, 50, Effect(0, feather: 10), 1);
        Assert.Equal(100 * 50, m.Length);
        Assert.Equal(255, m[25 * 100 + 5]);
        Assert.Equal(0, m[25 * 100 + 95]);
        Assert.InRange(m[25 * 100 + 50], 40, 215);
    }

    [Fact]
    public void Efeito_de_fundo_esta_no_catalogo_como_IA_e_altera_alfa()
    {
        var def = EffectCatalog.Find(EffectCatalog.AiBackground)!;
        Assert.Equal(EffectCategory.Ai, def.Category);
        Assert.True(def.AffectsAlpha);
        Assert.False(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.AiBackground)));
    }

    [Fact]
    public void Processador_sem_modelo_devolve_a_imagem_original()
    {
        var img = Solid(8, 8, 1, 2, 3);
        var processor = new AiFrameProcessor(new NoSegmentation());
        var asset = new MediaAsset { Id = Guid.NewGuid(), Kind = MediaKind.Image, Name = "a.png", OriginalPath = "a.png" };
        var (result, alpha) = processor.Apply(Effect(0), img, asset, MediaTime.Zero, (_, _) => null, 1);
        Assert.Same(img, result);
        Assert.False(alpha);
    }

    private sealed class NoSegmentation : ISegmentationService
    {
        public bool IsAvailable => false;
        public Matte? GetMatte(MediaAsset asset, MediaTime sourceTime, BgraImage frame) => null;
        public Task PrecomputeAsync(Project p, Clip c, IReadOnlyDictionary<Guid, string> paths, IProgress<double>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TrackSample>> TrackPersonAsync(Project p, Clip c, IReadOnlyDictionary<Guid, string> paths, IProgress<double>? progress = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TrackSample>>([]);
        public void ClearCache(Guid assetId) { }
    }

    // ---------------------------------------------------------------- cache e tamanhos

    [Fact]
    public void Cache_de_mascaras_guarda_em_disco_e_limpa()
    {
        var asset = Guid.NewGuid();
        var cache = new MatteCache(_dir, memoryCapacity: 1);
        var matte = HalfMatte(32, 16);
        cache.Put(asset, 7, matte);
        cache.Put(asset, 8, matte); // tira o quadro 7 da memória
        var fresh = new MatteCache(_dir);
        Assert.True(fresh.Contains(asset, 7));
        Assert.Equal(matte.Alpha, fresh.Get(asset, 7)!.Alpha);
        fresh.Clear(asset);
        Assert.False(fresh.Contains(asset, 7));
        Assert.Null(fresh.Get(asset, 8));
    }

    [Theory]
    [InlineData(1920, 1080, 512, 896, 512)]
    [InlineData(1080, 1920, 512, 512, 896)]
    [InlineData(640, 480, 512, 672, 512)]
    public void Tamanho_de_inferencia_do_MODNet_e_multiplo_de_32(int w, int h, int reference, int ew, int eh) =>
        Assert.Equal((ew, eh), ModnetSegmenter.InferenceSize(w, h, reference));

    [Fact]
    public void Caixa_da_pessoa_vem_da_maior_regiao()
    {
        var a = new byte[100 * 50];
        for (var y = 10; y < 40; y++)
            for (var x = 20; x < 40; x++) a[y * 100 + x] = 255;
        a[0] = 255; // ruído isolado
        var b = ModnetSegmenter.Bounds(new Matte(100, 50, a))!.Value;
        Assert.Equal(0.3, b.X, 0.01);
        Assert.Equal(0.5, b.Y, 0.01);
        Assert.Equal(0.2, b.W, 0.01);
        Assert.Equal(0.6, b.H, 0.01);
        Assert.Null(ModnetSegmenter.Bounds(new Matte(10, 10, new byte[100])));
    }

    // ---------------------------------------------------------------- cenas

    private static BgraImage Scene(int scene, int frame)
    {
        // Cada cena tem uma cor; um quadrado se move dentro dela (movimento normal não deve gerar corte).
        var colors = new (byte, byte, byte)[] { (200, 40, 40), (40, 180, 60), (30, 60, 200) };
        var (r, g, b) = colors[scene % colors.Length];
        var img = Solid(64, 36, b, g, r);
        var x0 = frame % 40;
        for (var y = 10; y < 20; y++)
            for (var x = x0; x < x0 + 10; x++)
            {
                var i = (y * 64 + x) * 4;
                img.Pixels[i] = img.Pixels[i + 1] = img.Pixels[i + 2] = 230;
            }
        return img;
    }

    [Fact]
    public void Histogramas_detectam_as_trocas_de_cena()
    {
        var hists = Enumerable.Range(0, 90).Select(i => HistogramSceneDetector.Histogram(Scene(i / 30, i))).ToList();
        var cuts = HistogramSceneDetector.Detect(hists, 0.5, 12);
        Assert.Equal([30, 60], cuts);
    }

    [Fact]
    public void Picos_do_TransNet_respeitam_limiar_e_distancia_minima()
    {
        float[] p = [0, 0.1f, 0.7f, 0.9f, 0.6f, 0, 0, 0.8f, 0, 0, 0, 0, 0, 0.95f, 0];
        Assert.Equal([3, 13], TransNetV2.Cuts(p, 0.5, 5));
        Assert.Equal([3, 7, 13], TransNetV2.Cuts(p, 0.5, 2));
    }

    [ModelFact("transnetv2.onnx")]
    public void TransNetV2_detecta_corte_entre_cenas_sinteticas()
    {
        using var session = OnnxSessionFactory.CreateCpu(TestModels.Require("transnetv2.onnx"));
        var net = new TransNetV2(session.Session);
        var frames = Enumerable.Range(0, 120).Select(i => TransNetV2.Thumbnail(Scene(i < 60 ? 0 : 2, i))).ToList();
        var p = net.Predict(frames, null, CancellationToken.None);
        Assert.Equal(120, p.Length);
        var cuts = TransNetV2.Cuts(p, 0.5, 12);
        Assert.Contains(cuts, c => Math.Abs(c - 60) <= 1);
        Assert.DoesNotContain(cuts, c => Math.Abs(c - 60) > 1);
    }

    // ---------------------------------------------------------------- modelos de imagem

    [ModelFact("modnet.onnx")]
    public void MODNet_gera_mascara_no_tamanho_de_inferencia()
    {
        using var session = OnnxSessionFactory.CreateCpu(TestModels.Require("modnet.onnx"));
        var seg = new ModnetSegmenter(session.Session, 256);
        var matte = seg.Segment(Scene(0, 5));
        Assert.Equal(ModnetSegmenter.InferenceSize(64, 36, 256), (matte.Width, matte.Height));
        Assert.Equal(matte.Width * matte.Height, matte.Alpha.Length);
    }

    [ModelFact("swin2sr-x2.onnx")]
    public async Task Swin2SR_amplia_2x_mantendo_o_conteudo()
    {
        var input = Path.Combine(_dir, "pequena imagem ç.png");
        using (var mat = new Mat(30, 44, MatType.CV_8UC3, new Scalar(40, 120, 200)))
        {
            Cv2.Rectangle(mat, new Rect(10, 8, 20, 12), new Scalar(250, 250, 250), -1);
            Cv2.ImEncode(".png", mat, out var bytes);
            File.WriteAllBytes(input, bytes);
        }
        var output = Path.Combine(_dir, "saída.png");
        var registry = new FakeRegistry(TestModels.Require("swin2sr-x2.onnx"));
        await new ImageEnhancementService(registry, new OnnxSessionFactory()).EnhanceImageAsync(input, output);
        using var result = Cv2.ImDecode(File.ReadAllBytes(output), ImreadModes.Unchanged);
        Assert.Equal(88, result.Width);
        Assert.Equal(60, result.Height);
        var center = result.At<Vec3b>(4, 4);
        Assert.InRange(center.Item1, 100, 140);
        Assert.InRange(center.Item2, 180, 220);
        Assert.True(result.At<Vec3b>(28, 40).Item0 > 220);
    }

    private sealed class FakeRegistry(string path) : IModelRegistry
    {
        private readonly InstalledModel _model = new(new ModelManifest
        {
            Id = "teste", Name = "Teste", Version = "1", Source = "teste", Capability = ModelCapability.ImageEnhancement,
            Purpose = "teste", Format = "ONNX", File = Path.GetFileName(path), License = "MIT", Sha256 = new string('0', 64),
        }, Path.GetDirectoryName(path)!);

        public IReadOnlyList<InstalledModel> Installed => [_model];
        public InstalledModel? Find(ModelCapability capability) => capability == _model.Manifest.Capability ? _model : null;
        public bool IsAvailable(ModelCapability capability) => Find(capability) is not null;
        public Task<InstalledModel> InstallAsync(string packagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> VerifyAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void Refresh() { }
        public event EventHandler? Changed { add { } remove { } }
    }

    // ---------------------------------------------------------------- comandos e silêncios

    private static MediaTime S(double s) => MediaTime.FromSecondsApprox(s);

    [Fact]
    public void Trechos_sem_fala_sao_o_complemento_das_falas()
    {
        var clip = new Clip { Id = Guid.NewGuid(), Start = S(10), Duration = S(20) };
        TimeRange[] speech = [new(S(11), S(14)), new(S(14.3), S(20)), new(S(25), S(29.8))];
        var gaps = AiInspectorViewModel.SilenceGaps(clip, speech, S(0.5));
        Assert.Equal([new TimeRange(S(10), S(11)), new TimeRange(S(20), S(25))], gaps);
        Assert.Empty(AiInspectorViewModel.SilenceGaps(clip, [], S(0.5)));
    }

    [Fact]
    public void Audio_gerado_vai_para_trilhas_novas_e_silencia_o_original()
    {
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = MediaKind.Audio, Name = "fala.wav", OriginalPath = @"C:\m\fala.wav", Duration = S(30),
            Info = new MediaTechnicalInfo { AudioCodec = "pcm" },
        };
        var voice = media with { Id = Guid.NewGuid(), Name = "fala - voz.wav", Duration = S(8) };
        var ambient = media with { Id = Guid.NewGuid(), Name = "fala - ambiente.wav", Duration = S(8) };
        var p = Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [media, voice, ambient] };
        var seq = p.MainSequence;
        var audioIndex = seq.Tracks.FindIndex(t => t.Kind == TrackKind.Audio);
        var clip = new Clip { Id = Guid.NewGuid(), MediaAssetId = media.Id, Start = S(2), Duration = S(8), SourceIn = S(5) };
        p = p.WithSequence(seq with { Tracks = seq.Tracks.SetItem(audioIndex, seq.Tracks[audioIndex] with { Clips = [clip] }) });

        var result = new PlaceGeneratedAudioCommand(seq.Id, [new(clip.Id, [voice.Id, ambient.Id])], ["Voz (IA)", "Ambiente (IA)"], true).Apply(p);
        var tracks = result.MainSequence.Tracks;
        Assert.Equal(seq.Tracks.Count + 2, tracks.Count);
        Assert.Equal("Voz (IA)", tracks[audioIndex + 1].Name);
        Assert.Equal("Ambiente (IA)", tracks[audioIndex + 2].Name);
        var placed = tracks[audioIndex + 1].Clips.Single();
        Assert.Equal(voice.Id, placed.MediaAssetId);
        Assert.Equal(clip.Start, placed.Start);
        Assert.Equal(clip.Duration, placed.Duration);
        Assert.Equal(MediaTime.Zero, placed.SourceIn);
        Assert.Equal(0, tracks[audioIndex].Clips.Single().Volume);
    }
}
