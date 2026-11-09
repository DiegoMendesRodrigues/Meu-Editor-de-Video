using System.Collections.Immutable;
using System.Globalization;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Domain.Effects;

public enum EffectCategory
{
    /// <summary>Correção e gradação de cor (RF090–RF100).</summary>
    Color,

    /// <summary>Efeitos visuais (RF086).</summary>
    Stylize,

    /// <summary>Remoção de fundo por cor (RF112–RF118).</summary>
    Key,

    /// <summary>Processamento de áudio: equalizador, compressor, redução de ruído e efeitos sonoros (RF123–RF125, RF131).</summary>
    Audio,
}

public enum ParameterKind
{
    Slider,

    /// <summary>Lista de opções; o valor é o índice.</summary>
    Choice,

    /// <summary>Cor RGB 0–1 gravada em três parâmetros: chave.r, chave.g, chave.b.</summary>
    Color,

    /// <summary>Curva de tons (pontos 0–1) gravada como chave.n, chave.i.x, chave.i.y.</summary>
    Curve,

    /// <summary>Roda de cor: deslocamento cromático (chave.x, chave.y em −1…1) e luminância (chave.l).</summary>
    Wheel,
}

public sealed record EffectParameter(string Key, string Label, ParameterKind Kind, double Min = 0, double Max = 100, double Default = 0,
    string Unit = "", IReadOnlyList<string>? Options = null, (double R, double G, double B)? DefaultColor = null);

public sealed record EffectDefinition(string Type, string Name, string Description, EffectCategory Category,
    IReadOnlyList<EffectParameter> Parameters, bool UsesResource = false, bool AffectsAlpha = false);

/// <summary>
/// Catálogo de efeitos (§10): definição dos parâmetros usada pela interface e pelo processamento de imagem.
/// Os valores ficam em <see cref="EffectInstance.Parameters"/>; ausentes = padrão.
/// </summary>
public static class EffectCatalog
{
    public const string ColorAdjust = "color.adjust";
    public const string WhiteBalance = "color.whitebalance";
    public const string Curves = "color.curves";
    public const string Wheels = "color.wheels";
    public const string Lut = "color.lut";
    public const string Blur = "fx.blur";
    public const string Sharpen = "fx.sharpen";
    public const string Vignette = "fx.vignette";
    public const string Grayscale = "fx.grayscale";
    public const string Sepia = "fx.sepia";
    public const string Invert = "fx.invert";
    public const string Pixelate = "fx.pixelate";
    public const string Noise = "fx.noise";
    public const string Posterize = "fx.posterize";
    public const string Glow = "fx.glow";
    public const string ChromaKey = "key.chroma";
    public const string Equalizer = "audio.eq";
    public const string Compressor = "audio.compressor";
    public const string Denoise = "audio.denoise";
    public const string Echo = "audio.echo";
    public const string Reverb = "audio.reverb";
    public const string Telephone = "audio.telephone";
    public const string Distortion = "audio.distortion";

    public static IReadOnlyList<string> CurveChannels { get; } = ["master", "r", "g", "b"];

    public static IReadOnlyList<EffectDefinition> All { get; } =
    [
        new(ColorAdjust, "Ajustes de cor", "Exposição, brilho, contraste, saturação, temperatura, matiz, sombras e realces.", EffectCategory.Color,
        [
            new("exposure", "Exposição", ParameterKind.Slider, -3, 3, 0, " EV"),
            new("brightness", "Brilho", ParameterKind.Slider, -100, 100),
            new("contrast", "Contraste", ParameterKind.Slider, -100, 100),
            new("saturation", "Saturação", ParameterKind.Slider, -100, 100),
            new("temperature", "Temperatura", ParameterKind.Slider, -100, 100),
            new("tint", "Tonalidade (verde/magenta)", ParameterKind.Slider, -100, 100),
            new("hue", "Matiz", ParameterKind.Slider, -180, 180, 0, "°"),
            new("shadows", "Sombras", ParameterKind.Slider, -100, 100),
            new("highlights", "Realces", ParameterKind.Slider, -100, 100),
        ]),
        new(WhiteBalance, "Balanço de branco", "Indique a cor que deveria ser cinza neutro; o efeito compensa a dominante.", EffectCategory.Color,
        [
            new("neutral", "Cor de referência (deveria ser neutra)", ParameterKind.Color, DefaultColor: (0.5, 0.5, 0.5)),
            new("amount", "Intensidade", ParameterKind.Slider, 0, 100, 100, "%"),
        ]),
        new(Curves, "Curvas", "Curvas de tons geral e por canal (vermelho, verde, azul).", EffectCategory.Color,
        [
            new("master", "Geral", ParameterKind.Curve),
            new("r", "Vermelho", ParameterKind.Curve),
            new("g", "Verde", ParameterKind.Curve),
            new("b", "Azul", ParameterKind.Curve),
        ]),
        new(Wheels, "Rodas de cor", "Gradação por faixas: sombras (lift), tons médios (gamma) e realces (gain).", EffectCategory.Color,
        [
            new("lift", "Sombras", ParameterKind.Wheel),
            new("gamma", "Tons médios", ParameterKind.Wheel),
            new("gain", "Realces", ParameterKind.Wheel),
        ]),
        new(Lut, "LUT 3D (.cube)", "Aplica uma tabela de cores no formato .cube.", EffectCategory.Color,
        [
            new("intensity", "Intensidade", ParameterKind.Slider, 0, 100, 100, "%"),
        ], UsesResource: true),

        new(Blur, "Desfoque", "Desfoque gaussiano.", EffectCategory.Stylize, [new("radius", "Raio", ParameterKind.Slider, 0, 100, 8, " px")]),
        new(Sharpen, "Nitidez", "Realça detalhes (máscara de nitidez).", EffectCategory.Stylize, [new("amount", "Intensidade", ParameterKind.Slider, 0, 200, 60, "%")]),
        new(Vignette, "Vinheta", "Escurece as bordas do quadro.", EffectCategory.Stylize,
        [
            new("amount", "Intensidade", ParameterKind.Slider, 0, 100, 50, "%"),
            new("size", "Tamanho", ParameterKind.Slider, 0, 100, 50, "%"),
        ]),
        new(Grayscale, "Preto e branco", "Remove a cor.", EffectCategory.Stylize, [new("intensity", "Intensidade", ParameterKind.Slider, 0, 100, 100, "%")]),
        new(Sepia, "Sépia", "Tom envelhecido.", EffectCategory.Stylize, [new("intensity", "Intensidade", ParameterKind.Slider, 0, 100, 100, "%")]),
        new(Invert, "Negativo", "Inverte as cores.", EffectCategory.Stylize, [new("intensity", "Intensidade", ParameterKind.Slider, 0, 100, 100, "%")]),
        new(Pixelate, "Pixelizar", "Mosaico de blocos.", EffectCategory.Stylize, [new("size", "Tamanho do bloco", ParameterKind.Slider, 2, 100, 16, " px")]),
        new(Noise, "Granulado", "Ruído de filme.", EffectCategory.Stylize, [new("amount", "Intensidade", ParameterKind.Slider, 0, 100, 20, "%")]),
        new(Posterize, "Posterizar", "Reduz o número de tons.", EffectCategory.Stylize, [new("levels", "Níveis", ParameterKind.Slider, 2, 32, 6)]),
        new(Glow, "Brilho suave", "Halo luminoso nas áreas claras.", EffectCategory.Stylize,
        [
            new("amount", "Intensidade", ParameterKind.Slider, 0, 100, 40, "%"),
            new("radius", "Raio", ParameterKind.Slider, 1, 100, 20, " px"),
            new("threshold", "Limiar", ParameterKind.Slider, 0, 100, 60, "%"),
        ]),

        new(ChromaKey, "Chroma key", "Remove o fundo verde, azul ou de outra cor (RF112–RF118).", EffectCategory.Key,
        [
            new("preset", "Cor de remoção", ParameterKind.Choice, 0, 2, 0, Options: ["Verde", "Azul", "Cor personalizada"]),
            new("keyColor", "Cor personalizada", ParameterKind.Color, DefaultColor: (0.0, 0.8, 0.2)),
            new("tolerance", "Tolerância", ParameterKind.Slider, 0, 100, 30, "%"),
            new("softness", "Suavidade da transição", ParameterKind.Slider, 0, 100, 15, "%"),
            new("edge", "Suavizar bordas", ParameterKind.Slider, 0, 10, 1, " px"),
            new("spill", "Remover reflexo da cor", ParameterKind.Slider, 0, 100, 60, "%"),
            new("transparency", "Transparência do fundo removido", ParameterKind.Slider, 0, 100, 100, "%"),
            new("background", "Fundo", ParameterKind.Choice, 0, 1, 0, Options: ["Transparente (mostra as trilhas abaixo)", "Cor sólida"]),
            new("backgroundColor", "Cor do fundo", ParameterKind.Color, DefaultColor: (0.0, 0.0, 0.0)),
        ], AffectsAlpha: true),

        new(Equalizer, "Equalizador", "Equalizador paramétrico: graves, médios ajustáveis, agudos e corte de graves.", EffectCategory.Audio,
        [
            new("lowcut", "Corte de graves", ParameterKind.Slider, 0, 400, 0, " Hz"),
            new("lowGain", "Graves", ParameterKind.Slider, -15, 15, 0, " dB"),
            new("lowFreq", "Frequência dos graves", ParameterKind.Slider, 40, 500, 120, " Hz"),
            new("midGain", "Médios", ParameterKind.Slider, -15, 15, 0, " dB"),
            new("midFreq", "Frequência dos médios", ParameterKind.Slider, 200, 8000, 1000, " Hz"),
            new("midQ", "Largura dos médios (Q)", ParameterKind.Slider, 0.3, 8, 1),
            new("highGain", "Agudos", ParameterKind.Slider, -15, 15, 0, " dB"),
            new("highFreq", "Frequência dos agudos", ParameterKind.Slider, 2000, 16000, 8000, " Hz"),
        ]),
        new(Compressor, "Compressor", "Compressão dinâmica: aproxima trechos fortes e fracos.", EffectCategory.Audio,
        [
            new("threshold", "Limiar", ParameterKind.Slider, -60, 0, -18, " dB"),
            new("ratio", "Razão", ParameterKind.Slider, 1, 20, 4, ":1"),
            new("attack", "Ataque", ParameterKind.Slider, 0.1, 100, 10, " ms"),
            new("release", "Liberação", ParameterKind.Slider, 10, 1000, 150, " ms"),
            new("makeup", "Ganho de compensação", ParameterKind.Slider, 0, 24, 0, " dB"),
        ]),
        new(Denoise, "Redução de ruído", "Reduz chiado e ruído de fundo constante (ventilador, ar-condicionado).", EffectCategory.Audio,
        [
            new("amount", "Intensidade", ParameterKind.Slider, 0, 100, 70, "%"),
            new("sensitivity", "Sensibilidade", ParameterKind.Slider, 0, 100, 50, "%"),
        ]),
        new(Echo, "Eco", "Repetições do som com atraso.", EffectCategory.Audio,
        [
            new("delay", "Atraso", ParameterKind.Slider, 20, 2000, 300, " ms"),
            new("feedback", "Repetições", ParameterKind.Slider, 0, 90, 35, "%"),
            new("mix", "Mistura", ParameterKind.Slider, 0, 100, 35, "%"),
        ]),
        new(Reverb, "Reverberação", "Simula a ambiência de uma sala.", EffectCategory.Audio,
        [
            new("room", "Tamanho da sala", ParameterKind.Slider, 0, 100, 60, "%"),
            new("damping", "Abafamento", ParameterKind.Slider, 0, 100, 50, "%"),
            new("mix", "Mistura", ParameterKind.Slider, 0, 100, 25, "%"),
        ]),
        new(Telephone, "Telefone / rádio", "Som de telefone ou rádio antigo (banda estreita).", EffectCategory.Audio,
        [
            new("intensity", "Intensidade", ParameterKind.Slider, 0, 100, 100, "%"),
        ]),
        new(Distortion, "Distorção", "Saturação do som.", EffectCategory.Audio,
        [
            new("drive", "Intensidade", ParameterKind.Slider, 0, 100, 40, "%"),
            new("mix", "Mistura", ParameterKind.Slider, 0, 100, 100, "%"),
        ]),
    ];

    /// <summary>Efeitos de áudio do clipe (ativos), na ordem de aplicação.</summary>
    public static ImmutableList<EffectInstance> ActiveAudioEffects(Clip clip) =>
        clip.Effects.Where(e => !IsNeutral(e) && Find(e.Type)?.Category == EffectCategory.Audio).ToImmutableList();

    public static bool IsAudio(string type) => Find(type)?.Category == EffectCategory.Audio;

    public static EffectDefinition? Find(string type) => All.FirstOrDefault(d => d.Type == type);

    public static EffectInstance Create(string type, string? resource = null) =>
        Find(type) is null
            ? throw new ArgumentException($"Efeito desconhecido: {type}", nameof(type))
            : new EffectInstance { Id = Guid.NewGuid(), Type = type, Resource = resource };

    /// <summary>Valor de um parâmetro numérico (ou o padrão do catálogo).</summary>
    public static double Value(EffectInstance e, string key)
    {
        if (e.Parameters.TryGetValue(key, out var v)) return v;
        var p = Find(e.Type)?.Parameters.FirstOrDefault(x => x.Key == key);
        return p?.Default ?? 0;
    }

    public static (double R, double G, double B) ColorValue(EffectInstance e, string key)
    {
        var def = Find(e.Type)?.Parameters.FirstOrDefault(x => x.Key == key)?.DefaultColor ?? (0, 0, 0);
        return (e.Parameters.GetValueOrDefault(key + ".r", def.R),
                e.Parameters.GetValueOrDefault(key + ".g", def.G),
                e.Parameters.GetValueOrDefault(key + ".b", def.B));
    }

    public static EffectInstance WithColor(EffectInstance e, string key, double r, double g, double b) => e with
    {
        Parameters = e.Parameters.SetItem(key + ".r", Math.Clamp(r, 0, 1)).SetItem(key + ".g", Math.Clamp(g, 0, 1)).SetItem(key + ".b", Math.Clamp(b, 0, 1)),
    };

    /// <summary>Pontos da curva (ordenados por x); sem pontos gravados = identidade.</summary>
    public static IReadOnlyList<(double X, double Y)> CurvePoints(EffectInstance e, string channel)
    {
        var n = (int)e.Parameters.GetValueOrDefault(channel + ".n", 0);
        if (n < 2) return [(0, 0), (1, 1)];
        var list = new List<(double, double)>(n);
        for (var i = 0; i < n; i++)
            list.Add((Math.Clamp(e.Parameters.GetValueOrDefault(Key(channel, i, "x"), 0), 0, 1),
                      Math.Clamp(e.Parameters.GetValueOrDefault(Key(channel, i, "y"), 0), 0, 1)));
        return list.OrderBy(p => p.Item1).ToList();
    }

    public static EffectInstance WithCurve(EffectInstance e, string channel, IReadOnlyList<(double X, double Y)> points)
    {
        var b = e.Parameters.ToBuilder();
        foreach (var k in b.Keys.Where(k => k.StartsWith(channel + ".", StringComparison.Ordinal)).ToList()) b.Remove(k);
        var sorted = points.OrderBy(p => p.X).ToList();
        b[channel + ".n"] = sorted.Count;
        for (var i = 0; i < sorted.Count; i++)
        {
            b[Key(channel, i, "x")] = Math.Clamp(sorted[i].X, 0, 1);
            b[Key(channel, i, "y")] = Math.Clamp(sorted[i].Y, 0, 1);
        }
        return e with { Parameters = b.ToImmutable() };
    }

    public static (double X, double Y, double L) WheelValue(EffectInstance e, string key) =>
        (e.Parameters.GetValueOrDefault(key + ".x", 0), e.Parameters.GetValueOrDefault(key + ".y", 0), e.Parameters.GetValueOrDefault(key + ".l", 0));

    public static EffectInstance WithWheel(EffectInstance e, string key, double x, double y, double l)
    {
        // Mantém o ponto dentro do círculo unitário.
        var r = Math.Sqrt(x * x + y * y);
        if (r > 1) { x /= r; y /= r; }
        return e with { Parameters = e.Parameters.SetItem(key + ".x", x).SetItem(key + ".y", y).SetItem(key + ".l", Math.Clamp(l, -1, 1)) };
    }

    public static EffectInstance WithValue(EffectInstance e, string key, double value)
    {
        var p = Find(e.Type)?.Parameters.FirstOrDefault(x => x.Key == key);
        if (p is { Kind: ParameterKind.Slider or ParameterKind.Choice }) value = Math.Clamp(value, p.Min, p.Max);
        return e with { Parameters = e.Parameters.SetItem(key, value) };
    }

    private static string Key(string channel, int i, string axis) => string.Create(CultureInfo.InvariantCulture, $"{channel}.{i}.{axis}");

    /// <summary>O efeito não altera a imagem com os valores atuais (pode ser pulado).</summary>
    public static bool IsNeutral(EffectInstance e)
    {
        if (!e.Enabled) return true;
        return e.Type switch
        {
            ColorAdjust => Find(e.Type)!.Parameters.All(p => Math.Abs(Value(e, p.Key)) < 1e-9),
            Curves => CurveChannels.All(c => CurvePoints(e, c) is var pts && pts.All(p => Math.Abs(p.X - p.Y) < 1e-9)),
            Wheels => new[] { "lift", "gamma", "gain" }.All(k => WheelValue(e, k) == (0, 0, 0)),
            Lut => string.IsNullOrEmpty(e.Resource) || Value(e, "intensity") <= 0,
            Blur => Value(e, "radius") <= 0,
            Equalizer => new[] { "lowGain", "midGain", "highGain", "lowcut" }.All(k => Math.Abs(Value(e, k)) < 1e-9),
            Denoise or Telephone => Value(e, e.Type == Denoise ? "amount" : "intensity") <= 0,
            Echo or Reverb or Distortion => Value(e, "mix") <= 0,
            _ => false,
        };
    }

    /// <summary>Efeitos visuais ativos do clipe.</summary>
    public static ImmutableList<EffectInstance> ActiveEffects(Clip clip) =>
        clip.Effects.Where(e => !IsNeutral(e) && Find(e.Type)?.Category != EffectCategory.Audio).ToImmutableList();
}
