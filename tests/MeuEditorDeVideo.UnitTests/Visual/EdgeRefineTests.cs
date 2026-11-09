using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class EdgeRefineTests
{
    private static readonly EffectContext Ctx = new(1, 0);

    /// <summary>Imagem 100×100 totalmente transparente.</summary>
    private static BgraImage Transparent(int width = 100, int height = 100) => new(width, height, new byte[width * height * 4]);

    /// <summary>Preenche o retângulo [x0..x1] × [y0..y1] (inclusive) com a cor e alfa informados.</summary>
    private static void Fill(BgraImage img, int x0, int y0, int x1, int y1, byte b, byte g, byte r, byte a)
    {
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var i = (y * img.Width + x) * 4;
                img.Pixels[i] = b;
                img.Pixels[i + 1] = g;
                img.Pixels[i + 2] = r;
                img.Pixels[i + 3] = a;
            }
    }

    private static byte AlphaAt(BgraImage img, int x, int y) => img.Pixels[(y * img.Width + x) * 4 + 3];

    private static int CountOpaque(BgraImage img)
    {
        var count = 0;
        for (var i = 3; i < img.Pixels.Length; i += 4)
            if (img.Pixels[i] > 128) count++;
        return count;
    }

    private static BgraImage Square40()
    {
        var img = Transparent();
        Fill(img, 30, 30, 69, 69, 0, 0, 255, 255); // quadrado vermelho opaco 40×40
        return img;
    }

    private static BgraImage Refine(BgraImage img, double choke, double feather, double decontaminate)
    {
        var effect = EffectCatalog.Create(EffectCatalog.EdgeRefine);
        effect = EffectCatalog.WithValue(effect, "choke", choke);
        effect = EffectCatalog.WithValue(effect, "feather", feather);
        effect = EffectCatalog.WithValue(effect, "decontaminate", decontaminate);
        return EffectProcessor.Apply(img, [effect], Ctx).Image;
    }

    // ---------------------------------------------------------------- catálogo

    [Fact]
    public void Catalogo_contem_o_efeito_refinar_bordas_com_os_tres_parametros()
    {
        var def = EffectCatalog.Find(EffectCatalog.EdgeRefine);

        Assert.NotNull(def);
        Assert.Equal("Refinar bordas", def!.Name);
        Assert.Equal(EffectCategory.Key, def.Category);
        Assert.Equal(["choke", "feather", "decontaminate"], def.Parameters.Select(p => p.Key));

        var choke = def.Parameters[0];
        Assert.Equal(ParameterKind.Slider, choke.Kind);
        Assert.Equal((-20.0, 20.0, 0.0, " px"), (choke.Min, choke.Max, choke.Default, choke.Unit));

        var feather = def.Parameters[1];
        Assert.Equal(ParameterKind.Slider, feather.Kind);
        Assert.Equal((0.0, 30.0, 2.0, " px"), (feather.Min, feather.Max, feather.Default, feather.Unit));

        var decon = def.Parameters[2];
        Assert.Equal(ParameterKind.Slider, decon.Kind);
        Assert.Equal((0.0, 100.0, 50.0, "%"), (decon.Min, decon.Max, decon.Default, decon.Unit));
    }

    [Fact]
    public void Valores_padrao_do_efeito_vem_do_catalogo()
    {
        var effect = EffectCatalog.Create(EffectCatalog.EdgeRefine);

        Assert.Equal(0, EffectCatalog.Value(effect, "choke"));
        Assert.Equal(2, EffectCatalog.Value(effect, "feather"));
        Assert.Equal(50, EffectCatalog.Value(effect, "decontaminate"));
    }

    // ---------------------------------------------------------------- contrair / expandir

    [Fact]
    public void Contrair_com_4_px_reduz_a_area_opaca()
    {
        var src = Square40();
        var before = CountOpaque(src);

        var result = Refine(src, 4, 0, 0);

        Assert.True(CountOpaque(result) < before);
    }

    [Fact]
    public void Expandir_com_menos_4_px_aumenta_a_area_opaca()
    {
        var src = Square40();
        var before = CountOpaque(src);

        var result = Refine(src, -4, 0, 0);

        Assert.True(CountOpaque(result) > before);
    }

    // ---------------------------------------------------------------- suavizar

    [Fact]
    public void Suavizar_com_6_px_cria_valores_intermediarios_na_borda()
    {
        var src = Square40();
        Assert.DoesNotContain(src.Pixels.Where((_, i) => i % 4 == 3), a => a > 20 && a < 235);

        var result = Refine(src, 0, 6, 0);

        var alphas = result.Pixels.Where((_, i) => i % 4 == 3).ToArray();
        Assert.Contains(alphas, a => a > 20 && a < 235);
    }

    // ---------------------------------------------------------------- neutro

    [Fact]
    public void Parametros_zerados_mantem_as_mesmas_transparencias()
    {
        var src = Square40();

        var result = Refine(src, 0, 0, 0);

        for (var y = 0; y < src.Height; y++)
            for (var x = 0; x < src.Width; x++)
                Assert.Equal(AlphaAt(src, x, y), AlphaAt(result, x, y));
    }

    // ---------------------------------------------------------------- limpar cor das bordas

    [Fact]
    public void Limpar_cor_das_bordas_puxa_pixels_verdes_semitransparentes_para_vermelho()
    {
        var src = Transparent();
        Fill(src, 28, 28, 71, 71, 0, 255, 0, 128);   // anel verde semitransparente (2 px)
        Fill(src, 30, 30, 69, 69, 0, 0, 255, 255);   // interior vermelho opaco

        var result = Refine(src, 0, 0, 100);

        var i = (50 * src.Width + 29) * 4;           // pixel do anel, encostado no interior
        var g = result.Pixels[i + 1];
        var r = result.Pixels[i + 2];
        Assert.Equal(128, result.Pixels[i + 3]);     // alfa preservado
        Assert.True(r > 0, $"R deveria aumentar, veio {r}");
        Assert.True(g < 255, $"G deveria diminuir, veio {g}");
    }

    // ---------------------------------------------------------------- entrada imutável

    [Fact]
    public void Nao_altera_a_imagem_de_entrada()
    {
        var src = Square40();
        Fill(src, 28, 28, 71, 71, 0, 255, 0, 128);
        var copy = (byte[])src.Pixels.Clone();

        _ = Refine(src, 3, 4, 100);

        Assert.Equal(copy, src.Pixels);
    }
}
