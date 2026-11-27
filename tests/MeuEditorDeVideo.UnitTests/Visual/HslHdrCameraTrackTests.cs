// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.UnitTests.Visual;

/// <summary>Ajustes HSL e HDR (aba Cor) e rastreamento de câmera (aba Vídeo).</summary>
public sealed class HslHdrCameraTrackTests
{
    private static readonly EffectContext Ctx = new(1, 0);

    private static BgraImage Solid(int w, int h, byte b, byte g, byte r)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
        return new BgraImage(w, h, px);
    }

    private static EffectInstance Effect(string type, params (string Key, double Value)[] values) =>
        values.Aggregate(EffectCatalog.Create(type), (e, v) => EffectCatalog.WithValue(e, v.Key, v.Value));

    [Fact]
    public void Hsl_sem_ajustes_e_neutro()
    {
        Assert.True(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.Hsl)));
        Assert.Equal(EffectCatalog.HslBands.Count * 3, EffectCatalog.Find(EffectCatalog.Hsl)!.Parameters.Count);
    }

    [Fact]
    public void Hsl_dessatura_so_a_faixa_escolhida()
    {
        var blue = Solid(4, 4, 230, 60, 30);
        var red = Solid(4, 4, 30, 40, 220);
        var e = Effect(EffectCatalog.Hsl, ("blue.s", -100));

        var b = EffectProcessor.Apply(blue, [e], Ctx).Image.Pixels;
        var r = EffectProcessor.Apply(red, [e], Ctx).Image.Pixels;

        Assert.True(Math.Abs(b[0] - b[2]) < 40, $"azul deveria ficar quase cinza: {b[0]},{b[1]},{b[2]}");
        Assert.Equal(red.Pixels[..4], r[..4]);
    }

    [Fact]
    public void Hsl_nao_altera_cinzas()
    {
        var gray = Solid(4, 4, 128, 128, 128);
        var e = Effect(EffectCatalog.Hsl, ("red.h", 30), ("blue.s", 100), ("green.l", 100));
        Assert.Equal(gray.Pixels, EffectProcessor.Apply(gray, [e], Ctx).Image.Pixels);
    }

    [Fact]
    public void Hdr_levanta_as_sombras_e_zero_e_neutro()
    {
        var dark = Solid(16, 16, 30, 30, 30);
        var result = EffectProcessor.Apply(dark, [EffectCatalog.Create(EffectCatalog.Hdr)], Ctx).Image.Pixels;
        Assert.True(result[0] > 30, $"sombra deveria clarear: {result[0]}");
        Assert.True(EffectCatalog.IsNeutral(Effect(EffectCatalog.Hdr, ("strength", 0))));
    }

    private static Matte PersonOnRight()
    {
        // Silhueta de 20×60 px no lado direito de um quadro 100×100 (linhas 30–89).
        var alpha = new byte[100 * 100];
        for (var y = 30; y < 90; y++)
            for (var x = 70; x < 90; x++) alpha[y * 100 + x] = 255;
        return new Matte(100, 100, alpha);
    }

    [Fact]
    public void Alvo_da_cabeca_fica_no_alto_da_silhueta_e_o_do_corpo_no_meio()
    {
        var head = CameraTracker.Target(PersonOnRight(), head: true)!.Value;
        var body = CameraTracker.Target(PersonOnRight(), head: false)!.Value;
        Assert.InRange(head.X, 0.78, 0.81);
        Assert.InRange(body.X, 0.78, 0.81);
        Assert.True(head.Y < 0.40, $"cabeça: {head.Y}");
        Assert.InRange(body.Y, 0.55, 0.65);
        Assert.Null(CameraTracker.Target(new Matte(10, 10, new byte[100]), head: true));
    }

    [Fact]
    public void Corte_automatico_aproxima_a_camera_do_alvo()
    {
        // Metade esquerda preta, direita branca: com zoom na pessoa (à direita), o quadro fica mais claro.
        var px = new byte[100 * 100 * 4];
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++)
            {
                var i = (y * 100 + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(x >= 50 ? 255 : 0);
                px[i + 3] = 255;
            }
        var image = new BgraImage(100, 100, px);
        var effect = Effect(EffectCatalog.CameraTrack, ("autocrop", 100), ("target", 1));

        var result = CameraTracker.Apply(image, PersonOnRight(), effect, 0, 1);

        double Mean(byte[] p) => Enumerable.Range(0, p.Length / 4).Average(k => p[k * 4]);
        Assert.True(Mean(result.Pixels) > Mean(px) + 40, $"{Mean(result.Pixels)} vs {Mean(px)}");
        Assert.True(EffectCatalog.IsNeutral(Effect(EffectCatalog.CameraTrack, ("autocrop", 0))));
    }
}
