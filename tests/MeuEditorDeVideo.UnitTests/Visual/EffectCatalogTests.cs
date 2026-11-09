using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class EffectCatalogTests
{
    private static EffectInstance Blur(double radius = 8) =>
        EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Blur), "radius", radius);

    // ---------------------------------------------------------------- Value / WithValue

    [Fact]
    public void Value_retorna_o_padrao_do_catalogo_quando_o_parametro_esta_ausente()
    {
        var effect = EffectCatalog.Create(EffectCatalog.Blur);

        Assert.Equal(8, EffectCatalog.Value(effect, "radius"));
    }

    [Fact]
    public void Value_retorna_zero_para_chave_desconhecida()
    {
        var effect = EffectCatalog.Create(EffectCatalog.Blur);

        Assert.Equal(0, EffectCatalog.Value(effect, "nao-existe"));
    }

    [Fact]
    public void Value_retorna_o_valor_gravado()
    {
        Assert.Equal(30, EffectCatalog.Value(Blur(30), "radius"));
    }

    [Theory]
    [InlineData(EffectCatalog.Blur, "radius", 500, 100)]
    [InlineData(EffectCatalog.Blur, "radius", -5, 0)]
    [InlineData(EffectCatalog.ColorAdjust, "brightness", -500, -100)]
    [InlineData(EffectCatalog.Sharpen, "amount", 999, 200)]
    public void WithValue_limita_o_valor_ao_minimo_e_maximo_do_parametro(string type, string key, double input, double expected)
    {
        var effect = EffectCatalog.WithValue(EffectCatalog.Create(type), key, input);

        Assert.Equal(expected, EffectCatalog.Value(effect, key));
    }

    // ---------------------------------------------------------------- curvas

    [Fact]
    public void Curva_gravada_volta_ordenada_por_X()
    {
        var curve = EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master",
            new (double X, double Y)[] { (1, 0.9), (0, 0.1), (0.5, 0.5) });

        var points = EffectCatalog.CurvePoints(curve, "master");

        Assert.Equal(new (double X, double Y)[] { (0, 0.1), (0.5, 0.5), (1, 0.9) }, points);
    }

    [Fact]
    public void Curva_limita_os_pontos_entre_0_e_1()
    {
        var curve = EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "r",
            new (double X, double Y)[] { (2, -1), (0.5, 0.5) });

        var points = EffectCatalog.CurvePoints(curve, "r");

        Assert.Equal(new (double X, double Y)[] { (0.5, 0.5), (1, 0) }, points);
    }

    [Fact]
    public void Curva_sem_pontos_gravados_e_a_identidade()
    {
        var curve = EffectCatalog.Create(EffectCatalog.Curves);

        Assert.Equal(new (double X, double Y)[] { (0, 0), (1, 1) }, EffectCatalog.CurvePoints(curve, "master"));
    }

    [Fact]
    public void Regravar_curva_substitui_os_pontos_anteriores()
    {
        var first = EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master",
            new (double X, double Y)[] { (0, 0), (0.3, 0.2), (0.6, 0.7), (1, 1) });

        var second = EffectCatalog.WithCurve(first, "master", new (double X, double Y)[] { (0, 0), (1, 0.8) });

        Assert.Equal(2, EffectCatalog.CurvePoints(second, "master").Count);
        Assert.Equal(new (double X, double Y)[] { (0, 0), (1, 0.8) }, EffectCatalog.CurvePoints(second, "master"));
    }

    // ---------------------------------------------------------------- roda de cor e cor

    [Fact]
    public void WithWheel_normaliza_o_ponto_que_sai_do_circulo_unitario()
    {
        var wheel = EffectCatalog.WithWheel(EffectCatalog.Create(EffectCatalog.Wheels), "lift", 3, 4, 0.2);

        var (x, y, l) = EffectCatalog.WheelValue(wheel, "lift");
        Assert.Equal(0.6, x, 9);
        Assert.Equal(0.8, y, 9);
        Assert.Equal(0.2, l, 9);
    }

    [Fact]
    public void WithWheel_mantem_ponto_dentro_do_circulo_e_limita_a_luminancia()
    {
        var wheel = EffectCatalog.WithWheel(EffectCatalog.Create(EffectCatalog.Wheels), "gain", 0.3, 0.4, -2);

        Assert.Equal((0.3, 0.4, -1.0), EffectCatalog.WheelValue(wheel, "gain"));
    }

    [Fact]
    public void WithColor_grava_e_ColorValue_le_a_cor_limitada_a_faixa_0_1()
    {
        var effect = EffectCatalog.WithColor(EffectCatalog.Create(EffectCatalog.ChromaKey), "keyColor", 0.2, 0.5, 1.5);

        Assert.Equal((0.2, 0.5, 1.0), EffectCatalog.ColorValue(effect, "keyColor"));
    }

    [Fact]
    public void ColorValue_sem_cor_gravada_usa_a_cor_padrao_do_catalogo()
    {
        var effect = EffectCatalog.Create(EffectCatalog.ChromaKey);

        Assert.Equal((0.0, 0.8, 0.2), EffectCatalog.ColorValue(effect, "keyColor"));
    }

    // ---------------------------------------------------------------- IsNeutral / ActiveEffects

    [Fact]
    public void IsNeutral_ajuste_de_cor_recem_criado_e_neutro()
    {
        Assert.True(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.ColorAdjust)));
    }

    [Fact]
    public void IsNeutral_efeito_desativado_e_neutro_mesmo_com_valores_altos()
    {
        var disabled = Blur(50) with { Enabled = false };

        Assert.True(EffectCatalog.IsNeutral(disabled));
    }

    [Fact]
    public void IsNeutral_desfoque_com_raio_zero_e_neutro_e_com_raio_positivo_nao()
    {
        Assert.True(EffectCatalog.IsNeutral(Blur(0)));
        Assert.False(EffectCatalog.IsNeutral(Blur(5)));
    }

    [Fact]
    public void IsNeutral_LUT_sem_arquivo_ou_com_intensidade_zero_e_neutra()
    {
        var withoutFile = EffectCatalog.Create(EffectCatalog.Lut);
        var withFile = EffectCatalog.Create(EffectCatalog.Lut, @"C:\luts\filme.cube");
        var zeroIntensity = EffectCatalog.WithValue(withFile, "intensity", 0);

        Assert.True(EffectCatalog.IsNeutral(withoutFile));
        Assert.False(EffectCatalog.IsNeutral(withFile));
        Assert.True(EffectCatalog.IsNeutral(zeroIntensity));
    }

    [Fact]
    public void IsNeutral_curva_identidade_e_roda_zerada_sao_neutras()
    {
        var identityCurve = EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master",
            new (double X, double Y)[] { (0, 0), (1, 1) });

        Assert.True(EffectCatalog.IsNeutral(identityCurve));
        Assert.True(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.Wheels)));
    }

    [Fact]
    public void ActiveEffects_remove_os_efeitos_neutros_e_preserva_a_ordem()
    {
        var invert = EffectCatalog.Create(EffectCatalog.Invert);
        var clip = new Clip
        {
            Id = Guid.NewGuid(),
            Start = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(1),
            Effects = [Blur(0), invert, EffectCatalog.Create(EffectCatalog.Grayscale) with { Enabled = false }],
        };

        var active = EffectCatalog.ActiveEffects(clip);

        var only = Assert.Single(active);
        Assert.Equal(invert.Id, only.Id);
    }

    // ---------------------------------------------------------------- Create / Find / catálogo

    [Fact]
    public void Create_com_tipo_desconhecido_lanca_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => EffectCatalog.Create("fx.inexistente"));
    }

    [Fact]
    public void Create_grava_o_recurso_externo_e_gera_Id()
    {
        var first = EffectCatalog.Create(EffectCatalog.Lut, @"C:\luts\filme.cube");
        var second = EffectCatalog.Create(EffectCatalog.Lut, @"C:\luts\filme.cube");

        Assert.Equal(@"C:\luts\filme.cube", first.Resource);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Find_tipo_inexistente_retorna_nulo()
    {
        Assert.Null(EffectCatalog.Find("fx.inexistente"));
        Assert.NotNull(EffectCatalog.Find(EffectCatalog.ChromaKey));
    }

    [Fact]
    public void Chaves_de_parametros_sao_unicas_em_cada_definicao()
    {
        foreach (var definition in EffectCatalog.All)
        {
            var keys = definition.Parameters.Select(p => p.Key).ToList();
            Assert.True(keys.Distinct(StringComparer.Ordinal).Count() == keys.Count, $"Chaves repetidas em {definition.Type}.");
        }
    }

    [Fact]
    public void Tipos_de_efeito_sao_unicos_no_catalogo()
    {
        var types = EffectCatalog.All.Select(d => d.Type).ToList();

        Assert.Equal(types.Count, types.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Padroes_dos_parametros_deslizantes_estao_dentro_dos_limites()
    {
        foreach (var definition in EffectCatalog.All)
        foreach (var parameter in definition.Parameters.Where(p => p.Kind == ParameterKind.Slider))
        {
            Assert.True(parameter.Default >= parameter.Min && parameter.Default <= parameter.Max,
                $"Padrão fora da faixa em {definition.Type}.{parameter.Key}.");
        }
    }
}
