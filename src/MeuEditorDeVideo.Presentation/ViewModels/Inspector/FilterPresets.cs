using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Presentation.ViewModels.Inspector;

/// <summary>
/// Filtro (seção Filtros da biblioteca): um "look" pronto que muda cor, clima e estilo de uma vez. É composto só de
/// efeitos do catálogo (ajustes de cor, curvas, rodas, vinheta, granulado, brilho suave, P&amp;B), então fica editável na
/// pilha de efeitos depois de aplicado e não exige nada novo no formato .med.
/// </summary>
/// <param name="Id">Identificador estável (inclui a categoria: o mesmo filtro pode aparecer em mais de uma).</param>
/// <param name="Category">Categoria em pt-BR (chave de tradução).</param>
/// <param name="Name">Nome em pt-BR (chave de tradução).</param>
public sealed record FilterPreset(string Id, string Category, string Name, Func<IReadOnlyList<EffectInstance>> Build)
{
    public const string Prefix = "flt:";

    /// <summary>Chave na biblioteca e na miniatura (<see cref="Services.EffectPreviews"/>).</summary>
    public string Key => Prefix + Id;

    public override string ToString() => Name;
}

/// <summary>Filtros por categoria, inspirados nos estilos mais usados de editores de vídeo comerciais.</summary>
public static class FilterPresets
{
    public const string Basic = "Básico";
    public const string Featured = "Destaques";
    public const string Mono = "Monocromático";
    public const string Lifestyle = "Estilo de vida";
    public const string Cinematic = "Cinematográfico";
    public const string Retro = "Retrô";
    public const string Spring = "Primavera";
    public const string Indoor = "Ambientes internos";
    public const string Night = "Noturno";
    public const string Creative = "Criativos";

    public static IReadOnlyList<string> Categories { get; } = [Basic, Featured, Mono, Lifestyle, Cinematic, Retro, Spring, Indoor, Night, Creative];

    // Matizes das rodas de cor (graus; 0 = vermelho, 120 = verde, 240 = azul).
    private const double Red = 0, Orange = 30, Gold = 45, Yellow = 58, Green = 120, Mint = 150, Teal = 185, Cyan = 195, Blue = 225,
        Violet = 265, Purple = 280, Magenta = 305, Pink = 335;

    /// <summary>Monta a pilha de efeitos de um filtro.</summary>
    private sealed class Look
    {
        private readonly List<(string Key, double Value)> _adjust = [];
        private (double H, double S, double L)? _lift, _gamma, _gain;
        private double _curve, _fadeLow, _fadeHigh = 1, _vignette, _grain, _glow, _gray, _sepia;

        public Look A(string key, double value) { _adjust.Add((key, value)); return this; }

        public Look Exposure(double v) => A("exposure", v);

        public Look Bright(double v) => A("brightness", v);

        public Look Contrast(double v) => A("contrast", v);

        public Look Sat(double v) => A("saturation", v);

        public Look Temp(double v) => A("temperature", v);

        public Look Tint(double v) => A("tint", v);

        public Look Hue(double v) => A("hue", v);

        public Look Shadows(double v) => A("shadows", v);

        public Look Highlights(double v) => A("highlights", v);

        /// <summary>Cor nas sombras (lift): matiz, força 0–1 e luminância.</summary>
        public Look Lift(double hue, double strength, double l = 0) { _lift = (hue, strength, l); return this; }

        public Look Mid(double hue, double strength, double l = 0) { _gamma = (hue, strength, l); return this; }

        /// <summary>Cor nos realces (gain).</summary>
        public Look Gain(double hue, double strength, double l = 0) { _gain = (hue, strength, l); return this; }

        /// <summary>Curva em S (contraste de filme).</summary>
        public Look S(double strength) { _curve = strength; return this; }

        /// <summary>Pretos levantados e brancos baixados (aspecto desbotado).</summary>
        public Look Fade(double low, double high = 1) { _fadeLow = low; _fadeHigh = high; return this; }

        public Look Vignette(double amount) { _vignette = amount; return this; }

        public Look Grain(double amount) { _grain = amount; return this; }

        public Look Glow(double amount) { _glow = amount; return this; }

        public Look Gray(double intensity = 100) { _gray = intensity; return this; }

        public Look Sepia(double intensity) { _sepia = intensity; return this; }

        public IReadOnlyList<EffectInstance> Build()
        {
            var list = new List<EffectInstance>();
            if (_gray > 0) list.Add(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Grayscale), "intensity", _gray));
            if (_adjust.Count > 0)
                list.Add(_adjust.Aggregate(EffectCatalog.Create(EffectCatalog.ColorAdjust), (e, v) => EffectCatalog.WithValue(e, v.Key, v.Value)));
            if (_curve != 0 || _fadeLow > 0 || _fadeHigh < 1)
            {
                var lo = _fadeLow;
                var hi = _fadeHigh;
                double At(double x) => lo + (hi - lo) * x;
                list.Add(EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master",
                    [(0, lo), (0.25, At(0.25 - _curve)), (0.75, At(0.75 + _curve)), (1, hi)]));
            }
            if (_lift is not null || _gamma is not null || _gain is not null)
            {
                var e = EffectCatalog.Create(EffectCatalog.Wheels);
                e = Wheel(e, "lift", _lift);
                e = Wheel(e, "gamma", _gamma);
                list.Add(Wheel(e, "gain", _gain));
            }
            if (_sepia > 0) list.Add(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Sepia), "intensity", _sepia));
            if (_glow > 0) list.Add(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Glow), "amount", _glow));
            if (_grain > 0) list.Add(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Noise), "amount", _grain));
            if (_vignette > 0) list.Add(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Vignette), "amount", _vignette));
            return list;
        }

        private static EffectInstance Wheel(EffectInstance e, string key, (double H, double S, double L)? w)
        {
            if (w is not { } v) return e;
            var a = v.H * Math.PI / 180;
            return EffectCatalog.WithWheel(e, key, v.S * Math.Cos(a), v.S * Math.Sin(a), v.L);
        }
    }

    private static Look L() => new();

    private static FilterPreset F(string category, string id, string name, Look look) =>
        new($"{Slug(category)}.{id}", category, name, look.Build);

    private static string Slug(string category) => category switch
    {
        Basic => "basico",
        Featured => "destaques",
        Mono => "mono",
        Lifestyle => "estilo",
        Cinematic => "cinema",
        Retro => "retro",
        Spring => "primavera",
        Indoor => "interno",
        Night => "noturno",
        _ => "criativo",
    };

    // Looks compartilhados por mais de uma categoria.
    private static Look SweetGlow() => L().Exposure(0.15).Sat(10).Temp(12).Tint(12).Glow(45).Fade(0.04).Gain(Pink, 0.25);
    private static Look ScarletSymphony() => L().Contrast(18).Sat(20).Gain(Red, 0.45).Lift(Magenta, 0.2, -0.02).S(0.04);
    private static Look VelvetNightfall() => L().Exposure(-0.35).Contrast(15).Sat(-10).Lift(Violet, 0.45, -0.03).Gain(Magenta, 0.2).Vignette(55);
    private static Look PartyPulse() => L().Sat(45).Contrast(20).Lift(Purple, 0.35).Gain(Pink, 0.3).Glow(35);
    private static Look MagentaCarnival() => L().Sat(30).Tint(40).Contrast(12).Gain(Magenta, 0.35).Glow(25);
    private static Look GoldenToast() => L().Temp(45).Sat(15).Contrast(10).Gain(Gold, 0.4).Lift(Orange, 0.15).Glow(20);
    private static Look StarlitLagoon() => L().Temp(-30).Sat(15).Lift(Teal, 0.4, -0.02).Gain(Cyan, 0.2).Glow(30).Vignette(35);
    private static Look Pinkamour() => L().Exposure(0.2).Tint(30).Sat(5).Gain(Pink, 0.4).Fade(0.06).Glow(30);
    private static Look Blushbloom() => L().Exposure(0.25).Sat(-10).Tint(18).Gain(Pink, 0.28).Lift(Orange, 0.1).Fade(0.08, 0.97);
    private static Look VintageShimmer() => L().Sat(-25).Temp(25).Fade(0.12, 0.94).Gain(Gold, 0.25).Glow(30).Grain(18);
    private static Look BwStyle() => L().Gray().Contrast(20).S(0.04);
    private static Look EtherealGray() => L().Gray(85).Exposure(0.25).Contrast(-20).Fade(0.1, 0.96).Glow(25);
    private static Look AmberDawn() => L().Temp(35).Sat(10).Exposure(0.1).Gain(Orange, 0.35).Lift(Blue, 0.1).Glow(15);
    private static Look PeachBloom() => L().Exposure(0.2).Temp(18).Tint(15).Sat(-5).Gain(Orange, 0.25).Fade(0.06);
    private static Look GreenBreeze() => L().Tint(-25).Sat(15).Exposure(0.1).Gain(Mint, 0.25).Lift(Teal, 0.1);
    private static Look FreshMist() => L().Exposure(0.3).Contrast(-15).Sat(-15).Temp(-10).Fade(0.1, 0.97).Glow(15);
    private static Look PastelSkies() => L().Exposure(0.3).Sat(-25).Contrast(-20).Fade(0.12).Gain(Blue, 0.15).Lift(Pink, 0.12);
    private static Look SeaFoam() => L().Temp(-20).Tint(-12).Sat(5).Gain(Mint, 0.3).Fade(0.05);
    private static Look VerdantBreeze() => L().Tint(-30).Sat(25).Contrast(10).Gain(Green, 0.2).Lift(Teal, 0.12);
    private static Look SilentSnowyNight() => L().Temp(-50).Exposure(-0.2).Sat(-25).Lift(Blue, 0.35, 0.02).Fade(0.06).Glow(20).Vignette(40);
    private static Look EpicCinematic() => L().Contrast(25).Sat(-5).Lift(Teal, 0.4, -0.03).Gain(Orange, 0.35).S(0.05).Vignette(45);

    public static IReadOnlyList<FilterPreset> All { get; } =
    [
        // Básico
        F(Basic, "dias-ensolarados", "Dias ensolarados", L().Exposure(0.2).Temp(25).Sat(20).Highlights(-10).Gain(Gold, 0.15)),
        F(Basic, "sierra", "Sierra", L().Fade(0.08, 0.95).Sat(-15).Temp(12).Contrast(-8).Vignette(30)),
        F(Basic, "contraste-01", "Contraste 01", L().Contrast(30).S(0.04)),
        F(Basic, "contraste-02", "Contraste 02", L().Contrast(45).Sat(10).S(0.07).Shadows(-15)),
        F(Basic, "matiz-mais", "Matiz mais", L().Sat(35).Hue(12).Contrast(8)),
        F(Basic, "estilo-pb", "Estilo P&B", BwStyle()),
        F(Basic, "clareza-estudio", "Clareza de estúdio", L().Exposure(0.15).Contrast(15).Shadows(20).Highlights(-15).Sat(8)),
        F(Basic, "brilho-definido", "Brilho definido", L().Exposure(0.35).Contrast(12).Highlights(-20)),
        F(Basic, "aumento-clareza", "Aumento de clareza", L().Contrast(20).Shadows(25).Highlights(-25).Sat(12)),

        // Destaques
        F(Featured, "brilho-doce", "Brilho doce", SweetGlow()),
        F(Featured, "sinfonia-escarlate", "Sinfonia escarlate", ScarletSymphony()),
        F(Featured, "anoitecer-veludo", "Anoitecer de veludo", VelvetNightfall()),
        F(Featured, "pulso-festa", "Pulso de festa", PartyPulse()),
        F(Featured, "carnaval-magenta", "Carnaval magenta", MagentaCarnival()),
        F(Featured, "torrada-dourada", "Torrada dourada", GoldenToast()),
        F(Featured, "lagoa-estrelada", "Lagoa estrelada", StarlitLagoon()),
        F(Featured, "rosamor", "Rosamor", Pinkamour()),
        F(Featured, "rubor-florido", "Rubor florido", Blushbloom()),
        F(Featured, "cintilar-vintage", "Cintilar vintage", VintageShimmer()),

        // Monocromático
        F(Mono, "estilo-pb", "Estilo P&B", BwStyle()),
        F(Mono, "preto-branco", "Preto e branco", L().Gray()),
        F(Mono, "alto-contraste", "Alto contraste", L().Gray().Contrast(55).S(0.09).Shadows(-25)),
        F(Mono, "dessaturado", "Dessaturado", L().Sat(-65).Contrast(8)),
        F(Mono, "cinza-aco", "Cinza aço", L().Gray(90).Contrast(20).Lift(Blue, 0.25).Gain(Cyan, 0.08)),
        F(Mono, "cinza-etereo", "Cinza etéreo", EtherealGray()),

        // Estilo de vida
        F(Lifestyle, "aurora-ambar", "Aurora âmbar", AmberDawn()),
        F(Lifestyle, "flor-pessego", "Flor de pêssego", PeachBloom()),
        F(Lifestyle, "brisa-verde", "Brisa verde", GreenBreeze()),
        F(Lifestyle, "nevoa-fresca", "Névoa fresca", FreshMist()),
        F(Lifestyle, "ceu-pastel", "Céu pastel", PastelSkies()),
        F(Lifestyle, "espuma-mar", "Espuma do mar", SeaFoam()),
        F(Lifestyle, "brisa-verdejante", "Brisa verdejante", VerdantBreeze()),
        F(Lifestyle, "brilho-pessego", "Brilho de pêssego", L().Temp(22).Tint(10).Exposure(0.15).Gain(Orange, 0.3).Glow(25)),
        F(Lifestyle, "menta-fresca", "Menta fresca", L().Tint(-18).Temp(-12).Exposure(0.15).Gain(Mint, 0.35).Fade(0.05)),
        F(Lifestyle, "vista-verao", "Vista de verão", L().Temp(20).Sat(30).Contrast(12).Gain(Yellow, 0.2).Lift(Cyan, 0.15)),

        // Cinematográfico
        F(Cinematic, "celebrando-feriados", "Celebrando feriados", L().Temp(25).Sat(15).Contrast(15).Gain(Gold, 0.3).Lift(Red, 0.15).Glow(30)),
        F(Cinematic, "noite-neve", "Noite silenciosa de neve", SilentSnowyNight()),
        F(Cinematic, "palhaco-cordas", "Palhaço de cordas", L().Contrast(30).Sat(-20).Lift(Green, 0.25, -0.02).Gain(Yellow, 0.2).Vignette(55).Grain(15)),
        F(Cinematic, "jogo-sobrevivencia", "Jogo de sobrevivência", L().Contrast(20).Sat(-30).Temp(12).Lift(Teal, 0.25, -0.03).Gain(Yellow, 0.25).Vignette(45).Grain(12)),
        F(Cinematic, "suspense-contraste", "Suspense de alto contraste", L().Contrast(45).Sat(-35).Lift(Blue, 0.3, -0.05).S(0.08).Vignette(60)),
        F(Cinematic, "sombrio-cinema", "Sombrio cinematográfico", L().Exposure(-0.4).Contrast(25).Sat(-25).Lift(Teal, 0.3, -0.03).Vignette(65)),
        F(Cinematic, "coringa", "Coringa", L().Contrast(25).Sat(5).Lift(Green, 0.35, -0.02).Gain(Orange, 0.25).Vignette(50).Grain(10)),
        F(Cinematic, "homem-aco", "Homem de aço", L().Contrast(30).Sat(-30).Lift(Blue, 0.3, -0.02).Gain(Cyan, 0.12).S(0.05)),
        F(Cinematic, "homem-aco-cinema", "Homem de aço cinematográfico", L().Contrast(35).Sat(-20).Lift(Teal, 0.4, -0.04).Gain(Gold, 0.25).S(0.06).Vignette(50)),
        F(Cinematic, "exercito-mortos", "Exército dos mortos", L().Sat(-55).Contrast(25).Lift(Green, 0.3, -0.03).Gain(Teal, 0.12).Vignette(55).Grain(20)),
        F(Cinematic, "emocao", "Montanha-russa de emoção", L().Contrast(35).Sat(25).Lift(Blue, 0.3).Gain(Orange, 0.3).S(0.05)),
        F(Cinematic, "epico", "Épico cinematográfico", EpicCinematic()),

        // Retrô
        F(Retro, "prado-sicilia", "Prado da Sicília", L().Temp(30).Sat(-15).Fade(0.1, 0.95).Gain(Yellow, 0.25).Grain(15)),
        F(Retro, "quadro-sf", "Quadro de São Francisco", L().Temp(15).Tint(8).Sat(-20).Fade(0.12, 0.93).Lift(Teal, 0.2).Vignette(35)),
        F(Retro, "ecos-pintados", "Ecos pintados", L().Sat(-30).Contrast(-10).Fade(0.14, 0.92).Gain(Orange, 0.2).Lift(Blue, 0.15).Grain(20)),
        F(Retro, "veu-londres", "Véu de Londres", L().Temp(-15).Sat(-40).Fade(0.12, 0.94).Lift(Teal, 0.18).Vignette(40)),
        F(Retro, "rastro-cipreste", "Rastro de cipreste", L().Tint(-15).Sat(-25).Fade(0.1, 0.95).Gain(Green, 0.15).Grain(15)),
        F(Retro, "memoria-citrica", "Memória cítrica", L().Temp(30).Sat(10).Fade(0.08, 0.96).Gain(Yellow, 0.3).Lift(Orange, 0.12)),
        F(Retro, "rastro-canela", "Rastro de canela", L().Temp(35).Sat(-10).Contrast(10).Fade(0.08, 0.95).Gain(Orange, 0.3).Sepia(25)),
        F(Retro, "serenata-mato", "Serenata no mato", L().Tint(-10).Temp(18).Sat(-20).Fade(0.1, 0.95).Gain(Gold, 0.2).Lift(Green, 0.15)),
        F(Retro, "velhos-tempos", "Velhos tempos", L().Sepia(70).Fade(0.1, 0.92).Vignette(55).Grain(30)),
        F(Retro, "video-vintage", "Vídeo vintage", L().Sat(-30).Temp(20).Contrast(-10).Fade(0.14, 0.9).Grain(35).Vignette(45)),
        F(Retro, "cintilar-vintage", "Cintilar vintage", VintageShimmer()),

        // Primavera
        F(Spring, "aurora-ambar", "Aurora âmbar primavera", AmberDawn().Tint(-8).Sat(12)),
        F(Spring, "flor-pessego", "Flor de pêssego primavera", PeachBloom().Sat(12)),
        F(Spring, "brisa-verde", "Brisa verde", GreenBreeze()),
        F(Spring, "nevoa-fresca", "Névoa fresca primavera", FreshMist().Tint(-10)),
        F(Spring, "ceu-pastel", "Céu pastel primavera", PastelSkies().Tint(-8)),
        F(Spring, "espuma-mar", "Espuma do mar primavera", SeaFoam().Sat(12)),
        F(Spring, "brisa-verdejante", "Brisa verdejante", VerdantBreeze()),

        // Ambientes internos
        F(Indoor, "verde-espuma", "Verde espuma do mar", L().Tint(-20).Temp(-10).Sat(-10).Gain(Mint, 0.25).Fade(0.06)),
        F(Indoor, "nevoa-roxa", "Névoa roxa", L().Sat(-15).Lift(Purple, 0.3).Gain(Violet, 0.15).Fade(0.08).Glow(15)),
        F(Indoor, "cinza-oceano", "Cinza oceano", L().Sat(-45).Temp(-15).Lift(Blue, 0.2).Fade(0.06)),
        F(Indoor, "violeta-suave", "Violeta suave", L().Sat(-25).Tint(15).Gain(Violet, 0.2).Fade(0.08)),
        F(Indoor, "turquesa-suave", "Turquesa suave", L().Sat(-20).Temp(-12).Gain(Teal, 0.25).Fade(0.06)),
        F(Indoor, "azul-desbotado", "Azul desbotado", L().Sat(-30).Temp(-20).Fade(0.12, 0.95).Lift(Blue, 0.2)),
        F(Indoor, "cinza-etereo", "Cinza etéreo", EtherealGray()),
        F(Indoor, "marrom-tranquilo", "Marrom tranquilo", L().Temp(25).Sat(-25).Contrast(-5).Gain(Orange, 0.2).Sepia(20).Fade(0.06)),

        // Noturno
        F(Night, "cidade-sombria", "Noite sombria na cidade", L().Exposure(-0.35).Contrast(25).Sat(-20).Lift(Blue, 0.4, -0.03).Gain(Orange, 0.15).Vignette(55)),
        F(Night, "ouro-negro", "Ouro negro", L().Exposure(-0.25).Contrast(30).Sat(-40).Gain(Gold, 0.45).Lift(Gold, 0.05, -0.04).Vignette(50)),
        F(Night, "epico", "Épico cinematográfico", EpicCinematic()),
        F(Night, "carnaval-caotico", "Noite de carnaval caótica", L().Sat(50).Contrast(25).Lift(Purple, 0.35, -0.02).Gain(Orange, 0.3).Glow(45).Vignette(40)),
        F(Night, "pulso-quantico", "Noite de pulso quântico", L().Exposure(-0.2).Sat(35).Lift(Cyan, 0.4, -0.03).Gain(Magenta, 0.3).Glow(40)),
        F(Night, "ovelha-eletrica", "Ovelha elétrica", L().Contrast(30).Sat(30).Lift(Teal, 0.45, -0.03).Gain(Pink, 0.35).Glow(35).Vignette(35)),
        F(Night, "noite-neve", "Noite silenciosa de neve", SilentSnowyNight()),
        F(Night, "pulso-urbano", "Pulso urbano", L().Exposure(-0.2).Contrast(30).Sat(15).Lift(Blue, 0.35, -0.03).Gain(Yellow, 0.25).Glow(30).Vignette(40)),
        F(Night, "noite-amaro", "Noite Amaro", L().Exposure(-0.15).Fade(0.08).Sat(-10).Lift(Blue, 0.25).Gain(Gold, 0.2).Vignette(50)),
        F(Night, "noite-hefe", "Noite Hefe", L().Exposure(-0.2).Contrast(30).Sat(15).Temp(15).Gain(Orange, 0.25).Vignette(60)),

        // Criativos / Instagram
        F(Creative, "brilho-doce", "Brilho doce", SweetGlow()),
        F(Creative, "sinfonia-escarlate", "Sinfonia escarlate", ScarletSymphony()),
        F(Creative, "anoitecer-veludo", "Anoitecer de veludo", VelvetNightfall()),
        F(Creative, "pulso-festa", "Pulso de festa", PartyPulse()),
        F(Creative, "carnaval-magenta", "Carnaval magenta", MagentaCarnival()),
        F(Creative, "torrada-dourada", "Torrada dourada", GoldenToast()),
        F(Creative, "lagoa-estrelada", "Lagoa estrelada", StarlitLagoon()),
        F(Creative, "rosamor", "Rosamor", Pinkamour()),
        F(Creative, "rubor-florido", "Rubor florido", Blushbloom()),
    ];

    private static readonly Dictionary<string, FilterPreset> ByKey = All.ToDictionary(p => p.Key, StringComparer.Ordinal);

    public static FilterPreset? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var p) ? p : null;
}
