// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using V = MeuEditorDeVideo.Domain.Effects.VideoEffects;

namespace MeuEditorDeVideo.Presentation.ViewModels.Inspector;

/// <summary>
/// Efeito pronto da biblioteca (seção Efeitos): um ou mais efeitos do catálogo com estilo e valores escolhidos.
/// <see cref="PreviewTime"/> é o instante do clipe (de 4 s) mostrado na miniatura, onde o efeito aparece bem.
/// </summary>
public sealed record VideoEffectPreset(string Id, string Category, string Name, Func<IReadOnlyList<EffectInstance>> Build, double PreviewTime = 1.2)
{
    public const string Prefix = "vfx:";

    public string Key => Prefix + Id;

    /// <summary>Efeito de movimento: a miniatura sobrepõe instantes vizinhos (rastro) para mostrá-lo.</summary>
    public bool Motion => Category == "Tremido" || Id.EndsWith("pulsar", StringComparison.Ordinal) || Id is "tendencia.caos" or "falha.tremida";
}

/// <summary>Biblioteca de efeitos de vídeo por categoria (aberturas, amor, bordas, falha digital, tremido…).</summary>
public static class VideoEffectPresets
{
    /// <summary>Duração do clipe usada nas miniaturas.</summary>
    public const double PreviewDuration = 4;

    /// <summary>Categoria com os efeitos básicos do catálogo (desfoque, nitidez, chroma key…).</summary>
    public const string Utilities = "Utilitários";

    public static IReadOnlyList<string> Categories { get; } =
    [
        "Tendências", "Aberturas", "Encerramentos", "Amor", "Festa", "Borda", "Destaque", "Distorção", "Falha digital", "Cintilação",
        "Grunge", "Lupa", "Luz", "Retrô", "Tremido", "Slides", "Tela dividida", "Temporizador", "Visor", Utilities,
    ];

    private static EffectInstance Fx(string type, int style, params (string Key, double Value)[] values)
    {
        var e = EffectCatalog.WithValue(EffectCatalog.Create(type), "style", style);
        foreach (var (key, value) in values) e = EffectCatalog.WithValue(e, key, value);
        return e;
    }

    private static EffectInstance Colored(EffectInstance e, double r, double g, double b) => EffectCatalog.WithColor(e, "color", r, g, b);

    private static VideoEffectPreset P(string id, string category, string name, Func<EffectInstance> one, double time = 1.2) =>
        new(id, category, name, () => [one()], time);

    private static VideoEffectPreset P(string id, string category, string name, Func<IReadOnlyList<EffectInstance>> many, double time = 1.2) =>
        new(id, category, name, many, time);

    private static IEnumerable<VideoEffectPreset> Reveals()
    {
        string[] openers = ["Esmaecer do preto", "Cortina", "Íris", "Faixas de cinema", "Desfoque", "Zoom", "Flash", "Deslizar", "Persiana"];
        string[] endings = ["Esmaecer para o preto", "Cortina", "Íris", "Faixas de cinema", "Desfoque", "Zoom", "Flash", "Deslizar", "Persiana"];
        for (var i = 0; i < openers.Length; i++)
        {
            var style = i;
            yield return P($"abertura.{i}", "Aberturas", openers[i], () => Fx(V.Opener, style), 0.7);
        }
        for (var i = 0; i < endings.Length; i++)
        {
            var style = i;
            yield return P($"encerramento.{i}", "Encerramentos", endings[i], () => Fx(V.Ending, style), PreviewDuration - 0.7);
        }
    }

    private static IEnumerable<VideoEffectPreset> Styles(string idPrefix, string category, string type, string[] names, double time = 1.2, params (string Key, double Value)[] values)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var style = i;
            yield return P($"{idPrefix}.{i}", category, names[i], () => Fx(type, style, values), time);
        }
    }

    public static IReadOnlyList<VideoEffectPreset> All { get; } =
    [
        // Tendências
        P("tendencia.caos", "Tendências", "Caos", () => Fx(V.Shake, 2, ("amount", 70))),
        P("tendencia.vhs", "Tendências", "VHS", () => Fx(V.Retro, 0)),
        P("tendencia.rgb", "Tendências", "Divisão RGB", () => Fx(V.Glitch, 1, ("amount", 80))),
        P("tendencia.neon", "Tendências", "Borda neon", () => Colored(Fx(V.Border, 1, ("size", 4)), 0.2, 0.75, 1)),
        P("tendencia.pulsar", "Tendências", "Zoom pulsante", () => Fx(V.Magnify, 3, ("amount", 70)), 0.25),
        P("tendencia.neve", "Tendências", "Neve", () => Fx(V.Particles, 3, ("amount", 70))),
        P("tendencia.chuva", "Tendências", "Chuva", () => Fx(V.Particles, 7, ("amount", 60))),
        P("tendencia.noturna", "Tendências", "Visão noturna", () => Fx(V.Viewfinder, 3)),

        ..Reveals(),

        // Amor
        P("amor.coracoes", "Amor", "Corações", () => Fx(V.Particles, 0, ("amount", 45))),
        P("amor.chuva", "Amor", "Chuva de corações", () => Fx(V.Particles, 0, ("amount", 100), ("size", 60), ("speed", 160))),
        P("amor.rosado", "Amor", "Brilho rosado", () => Fx(V.Light, 6)),
        P("amor.romance", "Amor", "Romance", () => [Fx(V.Light, 6, ("amount", 55)), Fx(V.Particles, 0, ("amount", 25), ("size", 80))]),
        P("amor.moldura", "Amor", "Moldura de amor", () => [Colored(Fx(V.Border, 1, ("size", 4)), 1, 0.35, 0.65), Fx(V.Particles, 0, ("amount", 15), ("size", 70))]),
        P("amor.bokeh", "Amor", "Bokeh romântico", () => [Fx(V.Light, 3, ("amount", 80)), Fx(V.Light, 6, ("amount", 35))]),

        // Festa
        P("festa.confete", "Festa", "Confete", () => Fx(V.Particles, 1, ("amount", 60))),
        P("festa.fogos", "Festa", "Fogos de artifício", () => Fx(V.Particles, 6, ("amount", 60)), 0.6),
        P("festa.luzes", "Festa", "Luzes de festa", () => Fx(V.Light, 5)),
        P("festa.discoteca", "Festa", "Discoteca", () => [Fx(V.Light, 5, ("amount", 80)), Fx(V.Flicker, 4, ("amount", 40))]),
        P("festa.brilhos", "Festa", "Brilhos", () => Fx(V.Particles, 2, ("amount", 70), ("size", 130))),
        P("festa.estrelas", "Festa", "Estrelas", () => Fx(V.Particles, 4, ("amount", 40))),
        P("festa.bolhas", "Festa", "Bolhas", () => Fx(V.Particles, 5, ("amount", 40), ("size", 130))),

        // Borda
        P("borda.branca", "Borda", "Borda branca", () => Fx(V.Border, 0)),
        P("borda.preta", "Borda", "Borda preta", () => Colored(Fx(V.Border, 0), 0, 0, 0)),
        P("borda.neonazul", "Borda", "Neon azul", () => Colored(Fx(V.Border, 1, ("size", 4)), 0.2, 0.75, 1)),
        P("borda.neonrosa", "Borda", "Neon rosa", () => Colored(Fx(V.Border, 1, ("size", 4)), 1, 0.3, 0.75)),
        P("borda.arcoiris", "Borda", "Arco-íris", () => Fx(V.Border, 2, ("size", 4))),
        P("borda.filme", "Borda", "Filme", () => Fx(V.Border, 3)),
        P("borda.instantanea", "Borda", "Foto instantânea", () => Fx(V.Border, 4, ("size", 5))),
        P("borda.arredondada", "Borda", "Cantos arredondados", () => Fx(V.Border, 5, ("size", 4))),
        P("borda.desfocada", "Borda", "Fundo desfocado", () => Fx(V.Border, 6, ("size", 8))),

        ..Styles("destaque", "Destaque", V.Spotlight, ["Holofote", "Círculo", "Desfocar ao redor", "Preto e branco ao redor", "Moldura de foco"]),
        ..Styles("distorcao", "Distorção", V.Distort, ["Onda", "Redemoinho", "Ondulação", "Bolha", "Espelho", "Caleidoscópio", "Ar quente"], 1.2, ("amount", 70)),

        // Falha digital
        ..Styles("falha", "Falha digital", V.Glitch, ["Digital", "Divisão RGB", "Linhas deslocadas", "Blocos", "Sinal fraco", "Pixels quebrados"], 1.2, ("amount", 80)),
        P("falha.tremida", "Falha digital", "Falha tremida", () => [Fx(V.Glitch, 0, ("amount", 70)), Fx(V.Shake, 2, ("amount", 35))]),

        ..Styles("cintilacao", "Cintilação", V.Flicker, ["Piscar", "Estroboscópio", "Luz instável", "Preto e branco", "Cores"], 0.02, ("amount", 80)),

        // Grunge
        ..Styles("grunge", "Grunge", V.Grunge, ["Arranhões", "Poeira", "Papel envelhecido", "Sujeira", "Queimado", "Rasgado"], 1.2, ("amount", 75)),
        P("grunge.filmesujo", "Grunge", "Filme sujo", () => [Fx(V.Retro, 1, ("amount", 60)), Fx(V.Grunge, 0, ("amount", 50)), Fx(V.Grunge, 1, ("amount", 40))]),

        // Lupa e Slides
        P("lupa.zoom", "Lupa", "Zoom", () => Fx(V.Magnify, 0, ("amount", 40))),
        P("lupa.lupa", "Lupa", "Lupa", () => Fx(V.Magnify, 1, ("amount", 50))),
        P("lupa.olho", "Lupa", "Olho de peixe", () => Fx(V.Magnify, 2, ("amount", 60))),
        P("lupa.pulsar", "Lupa", "Zoom pulsante", () => Fx(V.Magnify, 3, ("amount", 70)), 0.25),
        P("slides.aproximar", "Slides", "Aproximação lenta", () => Fx(V.Magnify, 4, ("amount", 80)), 3.8),
        P("slides.afastar", "Slides", "Afastamento lento", () => Fx(V.Magnify, 5, ("amount", 80)), 0.2),
        P("slides.esquerda", "Slides", "Panorâmica para a esquerda", () => Fx(V.Magnify, 6, ("amount", 80)), 0.4),
        P("slides.direita", "Slides", "Panorâmica para a direita", () => Fx(V.Magnify, 7, ("amount", 80)), 0.4),
        P("slides.cima", "Slides", "Panorâmica para cima", () => Fx(V.Magnify, 8, ("amount", 80)), 0.4),
        P("slides.baixo", "Slides", "Panorâmica para baixo", () => Fx(V.Magnify, 9, ("amount", 80)), 0.4),

        // Luz
        ..Styles("luz", "Luz", V.Light, ["Vazamento de luz", "Reflexo de lente", "Raios de luz", "Bokeh", "Brilho do sol", "Luzes de festa"], 1.2, ("amount", 80)),

        ..Styles("retro", "Retrô", V.Retro, ["VHS", "Filme antigo", "TV antiga", "Linhas de varredura", "Fita danificada", "Câmera 8 mm"], 1.2, ("amount", 85)),

        // Tremido
        P("tremido.suave", "Tremido", "Suave", () => Fx(V.Shake, 0, ("amount", 60))),
        P("tremido.extremo", "Tremido", "Extremo", () => Fx(V.Shake, 1, ("amount", 80))),
        P("tremido.caos", "Tremido", "Caos", () => Fx(V.Shake, 2, ("amount", 70))),
        P("tremido.caos2", "Tremido", "Caos 2", () => [Fx(V.Shake, 2, ("amount", 90), ("speed", 150)), Fx(V.Glitch, 1, ("amount", 60))]),
        P("tremido.vertical1", "Tremido", "Vertical 1", () => Fx(V.Shake, 3, ("amount", 40))),
        P("tremido.vertical2", "Tremido", "Vertical 2", () => Fx(V.Shake, 3, ("amount", 90), ("speed", 160))),
        P("tremido.lateral1", "Tremido", "Lateral 1", () => Fx(V.Shake, 4, ("amount", 40))),
        P("tremido.lateral2", "Tremido", "Lateral 2", () => Fx(V.Shake, 4, ("amount", 90), ("speed", 160))),
        P("tremido.rotacao", "Tremido", "Rotação", () => Fx(V.Shake, 5, ("amount", 70))),
        P("tremido.pulsar", "Tremido", "Pulsar", () => Fx(V.Shake, 6, ("amount", 80)), 0.5),

        // Tela dividida
        ..Styles("dividida", "Tela dividida", V.Split, ["2 colunas", "3 colunas", "2 linhas", "4 quadros", "9 quadros", "Espelho horizontal", "Espelho vertical"]),

        // Temporizador
        P("timer.regressiva", "Temporizador", "Contagem regressiva", () => Fx(V.Timer, 0), 1.3),
        P("timer.cronometro", "Temporizador", "Cronômetro", () => Fx(V.Timer, 1), 1.3),
        P("timer.barra", "Temporizador", "Barra de progresso", () => Fx(V.Timer, 2), 2.4),
        P("timer.anel", "Temporizador", "Anel de progresso", () => Fx(V.Timer, 3), 2.4),
        P("timer.321", "Temporizador", "Contagem 3, 2, 1", () => Fx(V.Timer, 4), 0.25),
        P("timer.vermelho", "Temporizador", "Contagem regressiva vermelha", () => Colored(Fx(V.Timer, 0), 1, 0.25, 0.25), 2.3),

        // Visor
        ..Styles("visor", "Visor", V.Viewfinder, ["Câmera gravando", "Celular", "Mira", "Visão noturna", "Binóculo", "Câmera de segurança", "Foco automático"], 0.3),
    ];

    public static VideoEffectPreset? Find(string key) => All.FirstOrDefault(p => p.Key == key);
}
