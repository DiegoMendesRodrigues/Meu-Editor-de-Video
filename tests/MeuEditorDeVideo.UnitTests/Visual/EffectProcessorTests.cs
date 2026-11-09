using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class EffectProcessorTests
{
    private static readonly EffectContext Ctx = new(1, 0);

    private static BgraImage Solid(int width, int height, byte b, byte g, byte r, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
        return new BgraImage(width, height, pixels);
    }

    private static (int B, int G, int R, int A) PixelAt(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    /// <summary>Efeito do catálogo com valores de parâmetros informados.</summary>
    private static EffectInstance Effect(string type, params (string Key, double Value)[] values)
    {
        var effect = EffectCatalog.Create(type);
        foreach (var (key, value) in values) effect = EffectCatalog.WithValue(effect, key, value);
        return effect;
    }

    private const string IdentityLut = """
        TITLE "identidade"
        LUT_3D_SIZE 2
        0 0 0
        1 0 0
        0 1 0
        1 1 0
        0 0 1
        1 0 1
        0 1 1
        1 1 1
        """;

    // ---------------------------------------------------------------- pilha de efeitos

    [Fact]
    public void A_imagem_de_entrada_nunca_e_alterada()
    {
        var source = Solid(4, 4, 100, 100, 100);
        var before = (byte[])source.Pixels.Clone();

        EffectProcessor.Apply(source, [Effect(EffectCatalog.ColorAdjust, ("brightness", 50))], Ctx);

        Assert.Equal(before, source.Pixels);
    }

    [Fact]
    public void Sem_efeitos_devolve_a_mesma_imagem_sem_transparencia_nova()
    {
        var source = Solid(4, 4, 1, 2, 3);

        var (image, alphaChanged) = EffectProcessor.Apply(source, [], Ctx);

        Assert.Same(source, image);
        Assert.False(alphaChanged);
    }

    [Fact]
    public void Brilho_positivo_clareia_a_imagem()
    {
        var source = Solid(4, 4, 100, 100, 100);

        var (image, _) = EffectProcessor.Apply(source, [Effect(EffectCatalog.ColorAdjust, ("brightness", 50))], Ctx);

        var (b, g, r, _) = PixelAt(image, 1, 1);
        Assert.True(b > 100 && g > 100 && r > 100, $"Pixel clareado esperado, veio ({b},{g},{r}).");
    }

    [Fact]
    public void Saturacao_minima_deixa_os_tres_canais_iguais()
    {
        var source = Solid(4, 4, 50, 100, 200);

        var (image, _) = EffectProcessor.Apply(source, [Effect(EffectCatalog.ColorAdjust, ("saturation", -100))], Ctx);

        var (b, g, r, _) = PixelAt(image, 2, 2);
        Assert.Equal(b, g);
        Assert.Equal(g, r);
    }

    [Fact]
    public void Preto_e_branco_deixa_os_tres_canais_iguais()
    {
        var source = Solid(4, 4, 30, 60, 200);

        var (image, _) = EffectProcessor.Apply(source, [EffectCatalog.Create(EffectCatalog.Grayscale)], Ctx);

        var (b, g, r, _) = PixelAt(image, 0, 0);
        Assert.Equal(b, g);
        Assert.Equal(g, r);
    }

    [Fact]
    public void Negativo_inverte_0_e_255()
    {
        var source = Solid(2, 2, 0, 255, 128);

        var (image, _) = EffectProcessor.Apply(source, [EffectCatalog.Create(EffectCatalog.Invert)], Ctx);

        Assert.Equal((255, 0, 127, 255), PixelAt(image, 0, 0));
    }

    // ---------------------------------------------------------------- chroma key

    [Fact]
    public void Chroma_remove_o_verde_puro_e_marca_transparencia()
    {
        // Bordas desligadas (edge = 0) para que a média do desfoque não altere o pixel testado.
        var source = Solid(8, 8, 0, 255, 0);
        var keyed = Effect(EffectCatalog.ChromaKey, ("edge", 0));

        var (image, alphaChanged) = EffectProcessor.Apply(source, [keyed], Ctx);

        Assert.Equal(0, PixelAt(image, 4, 4).A);
        Assert.True(alphaChanged);
    }

    [Fact]
    public void Chroma_mantem_tom_de_pele_opaco()
    {
        var source = Solid(8, 8, b: 140, g: 170, r: 220);
        var keyed = Effect(EffectCatalog.ChromaKey, ("edge", 0));

        var (image, alphaChanged) = EffectProcessor.Apply(source, [keyed], Ctx);

        Assert.Equal(255, PixelAt(image, 4, 4).A);
        Assert.False(alphaChanged);
    }

    [Fact]
    public void Chroma_com_fundo_solido_nao_cria_transparencia()
    {
        var source = Solid(8, 8, 0, 255, 0);
        var keyed = Effect(EffectCatalog.ChromaKey, ("edge", 0), ("background", 1));

        var (image, alphaChanged) = EffectProcessor.Apply(source, [keyed], Ctx);

        var (b, g, r, a) = PixelAt(image, 4, 4);
        Assert.Equal(255, a);
        Assert.False(alphaChanged);
        Assert.Equal((0, 0, 0), (b, g, r));
    }

    // ---------------------------------------------------------------- desfoque e mistura

    [Fact]
    public void Desfoque_em_imagem_uniforme_mantem_a_imagem_igual()
    {
        var source = Solid(16, 16, 50, 100, 150);

        var (image, _) = EffectProcessor.Apply(source, [Effect(EffectCatalog.Blur, ("radius", 5))], Ctx);

        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            Assert.Equal((50, 100, 150, 255), PixelAt(image, x, y));
    }

    [Fact]
    public void Mix_com_peso_meio_faz_a_media_dos_dois_quadros()
    {
        var a = Solid(2, 2, 0, 0, 0);
        var b = Solid(2, 2, 200, 200, 200);

        var mixed = EffectProcessor.Mix(a, b, 0.5);

        // Canais de cor fazem a média; o alfa (255 nas duas entradas) continua 255.
        for (var i = 0; i < mixed.Pixels.Length; i += 4)
        {
            Assert.Equal(100, mixed.Pixels[i]);
            Assert.Equal(100, mixed.Pixels[i + 1]);
            Assert.Equal(100, mixed.Pixels[i + 2]);
            Assert.Equal(255, mixed.Pixels[i + 3]);
        }
    }

    [Fact]
    public void Mix_com_peso_zero_devolve_o_primeiro_quadro()
    {
        var a = Solid(2, 2, 0, 0, 0);
        var b = Solid(2, 2, 200, 200, 200);

        Assert.Same(a, EffectProcessor.Mix(a, b, 0));
    }

    // ---------------------------------------------------------------- LUT 3D (.cube)

    [Fact]
    public void LUT_identidade_2x2x2_devolve_a_mesma_cor()
    {
        var lut = CubeLut.Parse(new StringReader(IdentityLut));

        var (r, g, b) = lut.Apply(0.25f, 0.5f, 0.75f);

        Assert.Equal(0.25, r, 4);
        Assert.Equal(0.5, g, 4);
        Assert.Equal(0.75, b, 4);
    }

    [Fact]
    public void LUT_com_quantidade_de_cores_errada_lanca_InvalidDataException()
    {
        // Tamanho 2 exige 8 cores; aqui há só 7.
        var truncated = IdentityLut.Replace("1 1 1", "", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => CubeLut.Parse(new StringReader(truncated)));
    }

    [Fact]
    public void LUT_1D_nao_e_suportada_e_lanca_InvalidDataException()
    {
        const string oneD = "LUT_1D_SIZE 4\n0 0 0\n0.3 0.3 0.3\n0.6 0.6 0.6\n1 1 1\n";

        Assert.Throws<InvalidDataException>(() => CubeLut.Parse(new StringReader(oneD)));
    }

    [Fact]
    public void LUT_de_arquivo_ausente_e_ignorada_sem_excecao()
    {
        var source = Solid(4, 4, 10, 20, 30);
        var missing = EffectCatalog.Create(EffectCatalog.Lut, @"C:\nao\existe\filme.cube");

        var (image, alphaChanged) = EffectProcessor.Apply(source, [missing], Ctx);

        Assert.Equal(source.Pixels, image.Pixels);
        Assert.False(alphaChanged);
    }

    // ---------------------------------------------------------------- curva de tons

    [Fact]
    public void Curva_identidade_gera_tabela_linear()
    {
        var table = ToneCurve.Table([(0.0, 0.0), (1.0, 1.0)]);

        Assert.Equal(256, table.Length);
        for (var i = 0; i < 256; i++)
            Assert.InRange(table[i], i / 255.0 - 1e-4, i / 255.0 + 1e-4);
    }

    [Fact]
    public void Curva_monotona_gera_tabela_nao_decrescente()
    {
        var table = ToneCurve.Table([(0.0, 0.0), (0.3, 0.6), (0.7, 0.65), (1.0, 1.0)]);

        for (var i = 1; i < 256; i++)
            Assert.True(table[i] >= table[i - 1] - 1e-6f, $"Queda em {i}: {table[i - 1]} → {table[i]}.");
    }
}
