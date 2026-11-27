// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
namespace MeuEditorDeVideo.Domain.Effects;

/// <summary>
/// Efeitos de vídeo animados da biblioteca (tremido, falha digital, luzes, partículas, bordas, visores, aberturas…).
/// Cada tipo tem um parâmetro "style" (índice nas opções) e controles próprios; as predefinições da biblioteca
/// combinam tipo, estilo e valores. Os tipos gravados no projeto (vfx.*) nunca podem ser renomeados.
/// </summary>
public static class VideoEffects
{
    public const string Shake = "vfx.shake";
    public const string Glitch = "vfx.glitch";
    public const string Flicker = "vfx.flicker";
    public const string Border = "vfx.border";
    public const string Light = "vfx.light";
    public const string Particles = "vfx.particles";
    public const string Retro = "vfx.retro";
    public const string Grunge = "vfx.grunge";
    public const string Magnify = "vfx.magnify";
    public const string Distort = "vfx.distort";
    public const string Split = "vfx.split";
    public const string Viewfinder = "vfx.viewfinder";
    public const string Spotlight = "vfx.spotlight";
    public const string Timer = "vfx.timer";
    public const string Opener = "vfx.opener";
    public const string Ending = "vfx.ending";

    /// <summary>Estilos de abertura e encerramento (mesma ordem nos dois efeitos).</summary>
    public static IReadOnlyList<string> RevealStyles { get; } =
        ["Esmaecer", "Cortina", "Íris", "Faixas de cinema", "Desfoque", "Zoom", "Flash", "Deslizar", "Persiana"];

    public static bool IsVideoEffect(string type) => type.StartsWith("vfx.", StringComparison.Ordinal);

    private static EffectParameter Style(params string[] options) => new("style", "Estilo", ParameterKind.Choice, 0, options.Length - 1, 0, Options: options);

    private static EffectParameter Amount(double def = 60) => new("amount", "Intensidade", ParameterKind.Slider, 0, 100, def, "%");

    private static EffectParameter Speed() => new("speed", "Velocidade", ParameterKind.Slider, 10, 300, 100, "%");

    private static EffectParameter Center(string key, double def) =>
        new(key, key == "x" ? "Centro horizontal" : "Centro vertical", ParameterKind.Slider, 0, 100, def, "%");

    public static IReadOnlyList<EffectDefinition> Definitions { get; } =
    [
        new(Shake, "Tremido", "Sacode a imagem como uma câmera na mão, com força e ritmo ajustáveis.", EffectCategory.Stylize,
            [Style("Suave", "Extremo", "Caos", "Vertical", "Lateral", "Rotação", "Pulsar"), Amount(), Speed()]),
        new(Glitch, "Falha digital", "Interferências digitais: faixas deslocadas, cores separadas e blocos corrompidos.", EffectCategory.Stylize,
            [Style("Digital", "Divisão RGB", "Linhas deslocadas", "Blocos", "Sinal fraco", "Pixels quebrados"), Amount(), Speed()]),
        new(Flicker, "Cintilação", "A imagem pisca, como uma luz instável ou um estroboscópio.", EffectCategory.Stylize,
            [Style("Piscar", "Estroboscópio", "Luz instável", "Preto e branco", "Cores"), Amount(), Speed()]),
        new(Border, "Borda", "Moldura em volta do vídeo: simples, neon, arco-íris, filme, foto instantânea ou desfocada.", EffectCategory.Stylize,
            [Style("Simples", "Neon", "Arco-íris", "Filme", "Foto instantânea", "Arredondada", "Desfocada"),
             new("size", "Espessura", ParameterKind.Slider, 1, 30, 6, "%"), new("color", "Cor", ParameterKind.Color, DefaultColor: (1.0, 1.0, 1.0))]),
        new(Light, "Luz", "Vazamentos de luz, reflexo de lente, raios, bokeh e luzes coloridas que se movem sobre a imagem.", EffectCategory.Stylize,
            [Style("Vazamento de luz", "Reflexo de lente", "Raios de luz", "Bokeh", "Brilho do sol", "Luzes de festa", "Brilho rosado"), Amount(70), Speed()]),
        new(Particles, "Partículas", "Corações, confete, brilhos, neve, estrelas, bolhas, fogos de artifício ou chuva sobre o vídeo.", EffectCategory.Stylize,
            [Style("Corações", "Confete", "Brilhos", "Neve", "Estrelas", "Bolhas", "Fogos de artifício", "Chuva"), Amount(50),
             new("size", "Tamanho", ParameterKind.Slider, 20, 300, 100, "%"), Speed()]),
        new(Retro, "Retrô", "Aparência de fita VHS, filme antigo, TV de tubo ou câmera 8 mm.", EffectCategory.Stylize,
            [Style("VHS", "Filme antigo", "TV antiga", "Linhas de varredura", "Fita danificada", "Câmera 8 mm"), Amount(70)]),
        new(Grunge, "Grunge", "Textura gasta: arranhões, poeira, papel envelhecido, sujeira ou bordas queimadas.", EffectCategory.Stylize,
            [Style("Arranhões", "Poeira", "Papel envelhecido", "Sujeira", "Queimado", "Rasgado"), Amount(60)]),
        new(Magnify, "Lupa e zoom", "Amplia a imagem: zoom fixo, lupa, olho de peixe, zoom pulsante e movimentos lentos (Ken Burns).", EffectCategory.Stylize,
            [Style("Zoom", "Lupa", "Olho de peixe", "Zoom pulsante", "Aproximação lenta", "Afastamento lento",
                   "Panorâmica para a esquerda", "Panorâmica para a direita", "Panorâmica para cima", "Panorâmica para baixo"), Amount(50),
             Center("x", 50), Center("y", 50)]),
        new(Distort, "Distorção", "Deforma a imagem: ondas, redemoinho, ondulação, bolha, espelho, caleidoscópio ou ar quente.", EffectCategory.Stylize,
            [Style("Onda", "Redemoinho", "Ondulação", "Bolha", "Espelho", "Caleidoscópio", "Calor"), Amount(50), Speed()]),
        new(Split, "Tela dividida", "Repete o vídeo em vários quadros lado a lado ou espelhados.", EffectCategory.Stylize,
            [Style("2 colunas", "3 colunas", "2 linhas", "4 quadros", "9 quadros", "Espelho horizontal", "Espelho vertical"),
             new("gap", "Espaço entre quadros", ParameterKind.Slider, 0, 10, 1, "%"), new("color", "Cor do espaço", ParameterKind.Color, DefaultColor: (1.0, 1.0, 1.0))]),
        new(Viewfinder, "Visor", "Sobrepõe a tela de uma câmera, celular, mira, visão noturna, binóculo ou câmera de segurança.", EffectCategory.Stylize,
            [Style("Câmera gravando", "Celular", "Mira", "Visão noturna", "Binóculo", "Câmera de segurança", "Foco automático"), Amount(100)]),
        new(Spotlight, "Destaque", "Chama a atenção para uma área: holofote, círculo, desfoque ou preto e branco ao redor.", EffectCategory.Stylize,
            [Style("Holofote", "Círculo", "Desfocar ao redor", "Preto e branco ao redor", "Moldura de foco"), Amount(70),
             Center("x", 50), Center("y", 45),
             new("size", "Tamanho", ParameterKind.Slider, 5, 100, 35, "%"), new("color", "Cor", ParameterKind.Color, DefaultColor: (1.0, 0.85, 0.2))]),
        new(Timer, "Temporizador", "Contagem regressiva, cronômetro ou barra de progresso que acompanham a duração do clipe.", EffectCategory.Stylize,
            [Style("Contagem regressiva", "Cronômetro", "Barra de progresso", "Anel de progresso", "Contagem 3, 2, 1"),
             new("position", "Posição", ParameterKind.Choice, 0, 2, 0, Options: ["Centro", "Canto inferior direito", "Embaixo"]),
             new("size", "Tamanho", ParameterKind.Slider, 20, 200, 100, "%"), new("color", "Cor", ParameterKind.Color, DefaultColor: (1.0, 1.0, 1.0))]),
        new(Opener, "Abertura", "Revela o vídeo no início do clipe: esmaecer, cortina, íris, faixas de cinema, desfoque, zoom, flash…", EffectCategory.Stylize,
            [new("style", "Estilo", ParameterKind.Choice, 0, RevealStyles.Count - 1, 0, Options: RevealStyles),
             new("duration", "Duração", ParameterKind.Slider, 0.2, 5, 1.5, " s")]),
        new(Ending, "Encerramento", "Esconde o vídeo no fim do clipe: esmaecer, cortina, íris, faixas de cinema, desfoque, zoom, flash…", EffectCategory.Stylize,
            [new("style", "Estilo", ParameterKind.Choice, 0, RevealStyles.Count - 1, 0, Options: RevealStyles),
             new("duration", "Duração", ParameterKind.Slider, 0.2, 5, 1.5, " s")]),
    ];
}
