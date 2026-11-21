using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuEditorDeVideo.AudioEngine.Speech;

/// <summary>Uma voz de síntese de um programa externo: <see cref="Name"/> é o que vai na linha de comando e <see cref="Display"/> é o que o usuário vê.</summary>
public sealed record SpeechVoice(string Name, string Language, string Display);

/// <summary>Leitura da lista de vozes do <c>say</c> (macOS) e do <c>espeak-ng</c> (Linux) e mapeamento da velocidade. Funções puras.</summary>
public static partial class SpeechParsing
{
    [GeneratedRegex(@"^[A-Za-z]{2,3}([_-][A-Za-z0-9]+)*$")]
    private static partial Regex LocaleRegex();

    /// <summary>
    /// Saída de <c>say -v '?'</c>: <c>Luciana             pt_BR    # Olá, meu nome é Luciana.</c>. O nome pode ter espaços e parênteses
    /// (<c>Eddy (Português (Brasil))</c>); o idioma é o último termo antes do <c>#</c>. O texto exibido é "Nome (idioma)".
    /// </summary>
    public static IReadOnlyList<SpeechVoice> ParseSayVoices(string output)
    {
        var voices = new List<SpeechVoice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var hash = line.IndexOf('#');
            var head = (hash >= 0 ? line[..hash] : line).Trim();
            if (head.Length == 0) continue;
            var cut = head.LastIndexOfAny([' ', '\t']);
            if (cut <= 0) continue;
            var locale = head[(cut + 1)..];
            var name = head[..cut].Trim();
            if (name.Length == 0 || !LocaleRegex().IsMatch(locale)) continue;
            voices.Add(new SpeechVoice(name, locale, $"{name} ({locale})"));
        }
        return voices;
    }

    /// <summary>
    /// Saída de <c>espeak-ng --voices</c> (colunas Pty, Language, Age/Gender, VoiceName, File, Other Languages). O nome vem com "_" no lugar
    /// de espaços. Vozes que dependem do MBROLA (arquivo em <c>mb/</c>) ficam de fora. O texto exibido é "Nome (idioma)" e o idioma vai ao <c>-v</c>.
    /// </summary>
    public static IReadOnlyList<SpeechVoice> ParseEspeakVoices(string output)
    {
        var voices = new List<SpeechVoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var parts = raw.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _)) continue;
            var language = parts[1];
            var file = parts[4];
            if (file.StartsWith("mb/", StringComparison.Ordinal) || !LocaleRegex().IsMatch(language) || !seen.Add(language)) continue;
            var name = parts[3].Replace('_', ' ');
            voices.Add(new SpeechVoice(language, language, $"{name} ({language})"));
        }
        return voices;
    }

    /// <summary>Acha o argumento da linha de comando (<see cref="SpeechVoice.Name"/>) para o texto escolhido (exibido ou nome puro); nulo se não houver.</summary>
    public static string? ResolveVoice(IReadOnlyList<SpeechVoice> voices, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;
        var text = requested.Trim();
        return voices.FirstOrDefault(v => v.Display == text)?.Name
            ?? voices.FirstOrDefault(v => v.Name == text)?.Name;
    }

    /// <summary>Coloca primeiro as vozes do idioma da interface (região igual antes) e depois as demais; dentro de cada grupo, por nome.</summary>
    public static IReadOnlyList<SpeechVoice> OrderForCulture(IEnumerable<SpeechVoice> voices, CultureInfo culture)
    {
        var lang = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        var full = culture.Name.Replace('-', '_').ToLowerInvariant();
        int Rank(SpeechVoice v)
        {
            var l = v.Language.Replace('-', '_').ToLowerInvariant();
            if (full.Length > 0 && l == full) return 0;
            return l == lang || l.StartsWith(lang + "_", StringComparison.Ordinal) ? 1 : 2;
        }
        return voices.OrderBy(Rank).ThenBy(v => v.Display, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Velocidade −10..10 em palavras por minuto: lento até a metade de <paramref name="baseWpm"/>, rápido até o dobro; limitada a [min, max].</summary>
    public static int RateToWordsPerMinute(int rate, int baseWpm, int min, int max)
    {
        var r = Math.Clamp(rate, -10, 10);
        var factor = r >= 0 ? 1 + r / 10.0 : 1 + r / 20.0;
        return Math.Clamp((int)Math.Round(baseWpm * factor), min, max);
    }

    /// <summary><c>say -r</c>: o padrão do sistema fica em torno de 180 palavras por minuto.</summary>
    public static int RateToSayWpm(int rate) => RateToWordsPerMinute(rate, 180, 90, 360);

    /// <summary><c>espeak-ng -s</c>: o padrão é 175 palavras por minuto.</summary>
    public static int RateToEspeakWpm(int rate) => RateToWordsPerMinute(rate, 175, 87, 350);
}
