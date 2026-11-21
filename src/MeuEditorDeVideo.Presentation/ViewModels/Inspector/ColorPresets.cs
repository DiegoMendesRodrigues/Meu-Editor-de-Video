using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Presentation.ViewModels.Inspector;

/// <summary>Predefinição de cor (aba Cor): um ou mais efeitos de cor do catálogo com valores prontos.</summary>
public sealed record ColorPreset(string Id, string Name, string Description, Func<IReadOnlyList<EffectInstance>> Build)
{
    /// <summary>Texto lido por leitores de tela (itens de lista usam o texto do objeto).</summary>
    public override string ToString() => Name;

    /// <summary>Chave da miniatura em <see cref="Services.EffectPreviews"/>.</summary>
    public string PreviewKey => "preset:" + Id;
}

/// <summary>
/// Predefinições de cor. São compostas só de efeitos do catálogo (ajustes, curvas, rodas), então ficam editáveis
/// na pilha depois de aplicadas e não exigem nada novo no formato .med.
/// </summary>
public static class ColorPresets
{
    private static EffectInstance Adjust(params (string Key, double Value)[] values) =>
        values.Aggregate(EffectCatalog.Create(EffectCatalog.ColorAdjust), (e, v) => EffectCatalog.WithValue(e, v.Key, v.Value));

    private static EffectInstance SCurve(double strength) =>
        EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master",
            [(0, 0), (0.25, 0.25 - strength), (0.75, 0.75 + strength), (1, 1)]);

    private static EffectInstance Wheels((double X, double Y, double L) lift, (double X, double Y, double L) gain)
    {
        var e = EffectCatalog.Create(EffectCatalog.Wheels);
        e = EffectCatalog.WithWheel(e, "lift", lift.X, lift.Y, lift.L);
        return EffectCatalog.WithWheel(e, "gain", gain.X, gain.Y, gain.L);
    }

    public static IReadOnlyList<ColorPreset> All { get; } =
    [
        new("pb", "Preto e branco", "Remove a cor, com um pouco mais de contraste.",
            () => [Adjust(("saturation", -100), ("contrast", 15))]),
        new("noir", "Noir", "Preto e branco duro, com sombras profundas.",
            () => [Adjust(("saturation", -100), ("contrast", 45), ("shadows", -30)), SCurve(0.08)]),
        new("cinema", "Cinema", "Sombras azuladas e realces alaranjados (teal & orange).",
            () => [Wheels((-0.35, -0.25, -0.05), (0.3, 0.2, 0.05)), Adjust(("contrast", 15), ("saturation", 10))]),
        new("vivido", "Vívido", "Cores saturadas e mais contraste.",
            () => [Adjust(("saturation", 45), ("contrast", 20)), SCurve(0.04)]),
        new("quente", "Quente", "Tons de pôr do sol.",
            () => [Adjust(("temperature", 45), ("saturation", 10), ("highlights", -10))]),
        new("frio", "Frio", "Tons azulados e limpos.",
            () => [Adjust(("temperature", -45), ("tint", -5), ("saturation", -5))]),
        new("desbotado", "Desbotado", "Pretos levantados e cores suaves, aspecto de filme antigo.",
            () => [EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master", [(0, 0.12), (1, 0.92)]), Adjust(("saturation", -35))]),
        new("retro", "Retrô", "Amarelado, com pouco contraste.",
            () => [Adjust(("temperature", 35), ("tint", 10), ("saturation", -25), ("contrast", -15))]),
        new("claro", "Claro e arejado", "Mais luz, sombras suaves e cores leves.",
            () => [Adjust(("exposure", 0.4), ("shadows", 30), ("saturation", -10), ("temperature", 8))]),
    ];
}
