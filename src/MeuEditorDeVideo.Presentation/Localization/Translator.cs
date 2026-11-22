using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeuEditorDeVideo.Presentation.Localization;

/// <summary>
/// Tradução da interface a partir do texto em português do Brasil (a língua de origem do código). Os textos de origem
/// ficam em <c>i18n\origem.json</c> e cada idioma em <c>i18n\&lt;código&gt;.tsv</c> (linhas "id⇥tradução", em que o id são os
/// 8 primeiros hexadecimais do SHA-256 do texto de origem em UTF-8). Textos com valores ({0}, {1}…) viram modelos:
/// "Projeto reenquadrado para 1080×1920 (2 clipes)" é reconhecido por "Projeto reenquadrado para {0}×{1} ({2} {3})" e os
/// valores também são traduzidos ("clipes"); um valor todo em minúsculas também acha o rótulo com maiúscula
/// ("Ajustar deslocamento x" usa "Deslocamento X"). Sem tradução, o texto fica em português do Brasil.
/// </summary>
public static partial class Translator
{
    private static Dictionary<string, string>? _exact;
    private static List<Template> _templates = [];
    private static Dictionary<string, string> _anyCase = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(string Text, int Depth, bool Lowered), string> Cache = new();

    /// <summary>Idioma em uso (pt-BR = sem tradução).</summary>
    public static string Language { get; private set; } = "pt-BR";

    public static bool IsActive => _exact is not null;

    private sealed record Template(Regex Pattern, Regex? AnyCase, string Target, string Prefix);

    /// <summary>Id de um texto de origem (o mesmo usado nos arquivos .tsv).</summary>
    public static string IdOf(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..8];

    /// <summary>Carrega as traduções de <paramref name="language"/> da pasta <paramref name="folder"/>.</summary>
    public static void Load(string language, string folder)
    {
        Language = language;
        Cache.Clear();
        _exact = null;
        _templates = [];
        if (language == "pt-BR") return;
        var origin = Path.Combine(folder, "origem.json");
        var file = Path.Combine(folder, language + ".tsv");
        if (!File.Exists(origin) || !File.Exists(file)) return;

        var sources = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(origin)) ?? [];
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in sources) byId.TryAdd(IdOf(s), s);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(file))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (!byId.TryGetValue(line[..tab], out var source)) continue;
            var text = Unescape(line[(tab + 1)..]);
            if (text.Length > 0 && text != source) map[source] = text;
        }
        Use(map);
    }

    /// <summary>Usa um dicionário origem → tradução (testes e carregamento).</summary>
    public static void Use(IReadOnlyDictionary<string, string> map)
    {
        Cache.Clear();
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var templates = new List<Template>();
        foreach (var (source, target) in map)
        {
            exact[source] = target;
            if (Placeholder().IsMatch(source)) templates.Add(Build(source, target));
        }
        // Os modelos mais longos (mais específicos) primeiro.
        var anyCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, target) in exact) anyCase.TryAdd(source, target);
        _anyCase = anyCase;
        templates.Sort((a, b) => b.Pattern.ToString().Length.CompareTo(a.Pattern.ToString().Length));
        _templates = templates;
        _exact = exact;
    }

    public static void Reset()
    {
        Cache.Clear();
        _exact = null;
        _templates = [];
        Language = "pt-BR";
    }

    /// <summary>Traduz um texto exibido (exato, por modelo ou linha a linha). Sem tradução, devolve o próprio texto.</summary>
    public static string T(string? text) => T(text, 0);

    /// <param name="lowered">O texto é um valor fora de aspas: pode ser um rótulo posto em minúsculas.</param>
    private static string T(string? text, int depth, bool lowered = false)
    {
        if (_exact is null || string.IsNullOrEmpty(text)) return text ?? "";
        if (Cache.TryGetValue((text, depth, lowered), out var cached)) return cached;
        var result = Translate(text, depth);
        if (lowered && result == text) result = TranslateLowered(text, depth);
        if (Cache.Count < 50_000) Cache[(text, depth, lowered)] = result;
        return result;
    }

    private static string Translate(string text, int depth)
    {
        var exact = _exact!;
        if (exact.TryGetValue(text, out var t)) return t;

        // Rótulo com dois-pontos ("Idioma:"): traduz o rótulo sem eles.
        if (text.Length > 2 && text[^1] == ':' && exact.TryGetValue(text[..^1], out t)) return t + ":";

        // Tecla de acesso ("_Arquivo"): traduz sem o sublinhado e o põe na primeira letra.
        var underscore = text.IndexOf('_');
        if (underscore >= 0 && text.IndexOf('_', underscore + 1) < 0 && !text.Contains('\n'))
        {
            var plain = text.Remove(underscore, 1);
            if (exact.TryGetValue(plain, out t)) return "_" + t;
        }

        // Espaços nas pontas (rótulos montados) e reticências.
        var trimmed = text.Trim();
        if (trimmed.Length != text.Length && trimmed.Length > 0 && exact.TryGetValue(trimmed, out t))
            return text[..text.IndexOf(trimmed[0])] + t + text[(text.IndexOf(trimmed[0]) + trimmed.Length)..];

        if (depth < 3)
        {
            // Item de lista ("• Recurso"): traduz o que vem depois do marcador.
            if (text.StartsWith("• ", StringComparison.Ordinal) && !text.Contains('\n'))
                return "• " + T(text[2..], depth + 1);

            foreach (var template in _templates)
            {
                if (template.Prefix.Length > 0 && !text.StartsWith(template.Prefix, StringComparison.Ordinal)) continue;
                var m = template.Pattern.Match(text);
                if (!m.Success) continue;
                var args = new object[m.Groups.Count - 1];
                for (var i = 1; i < m.Groups.Count; i++)
                {
                    // Entre aspas vem um nome dado pelo usuário ("praia" não é o rótulo "Praia").
                    var at = m.Groups[i].Index;
                    var quoted = at > 0 && text[at - 1] is '"' or '“' or '«';
                    args[i - 1] = T(m.Groups[i].Value, depth + 1, lowered: !quoted);
                }
                try
                {
                    return string.Format(System.Globalization.CultureInfo.CurrentCulture, template.Target, args);
                }
                catch (FormatException)
                {
                    break;
                }
            }

            // Mensagens montadas por linhas ou parágrafos.
            if (text.Contains('\n'))
            {
                var lines = text.Split('\n');
                var changed = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;
                    var tr = T(line, depth + 1);
                    if (tr != line) { lines[i] = tr; changed = true; }
                }
                if (changed) return string.Join('\n', lines);
            }

            // "Rótulo: valor" com rótulo conhecido.
            var colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && colon < 60 && exact.TryGetValue(text[..(colon + 1)], out var label))
                return label + " " + T(text[(colon + 2)..], depth + 1, lowered: true);
            if (colon > 0 && colon < 60 && exact.TryGetValue(text[..colon], out label))
                return label + ": " + T(text[(colon + 2)..], depth + 1, lowered: true);
        }
        return text;
    }

    /// <summary>Rótulo posto em minúsculas pelo código: procura sem diferenciar maiúsculas.</summary>
    private static string TranslateLowered(string text, int depth)
    {
        if (!char.IsLower(text[0]) || text.Any(char.IsUpper)) return text;
        string? found = null;
        if (_anyCase.TryGetValue(text, out var exact)) found = exact;
        else if (depth < 3)
            foreach (var template in _templates)
            {
                if (template.AnyCase is not { } pattern || !text.StartsWith(template.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var m = pattern.Match(text);
                if (!m.Success) continue;
                var args = m.Groups.Values.Skip(1).Select(g => (object)T(g.Value, depth + 1)).ToArray();
                try
                {
                    found = string.Format(System.Globalization.CultureInfo.CurrentCulture, template.Target, args);
                }
                catch (FormatException)
                {
                    return text;
                }
                break;
            }
        if (string.IsNullOrEmpty(found)) return text;
        // Em alemão os substantivos têm maiúscula; siglas e letras de eixo ("X", "LUT 3D") ficam como estão.
        var keep = Language == "de" || found.Length < 2 || !char.IsLower(found[1]);
        return keep ? found : char.ToLowerInvariant(found[0]) + found[1..];
    }

    /// <summary>Formata com o modelo traduzido (para textos montados no código).</summary>
    public static string F(string format, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, T(format), args);

    private static Template Build(string source, string target)
    {
        // Modelos com pouco texto fixo ("D{0}", "{0} s") só valem para valores numéricos: senão "D{0}" pegaria
        // qualquer texto começando com D.
        var letters = Placeholder().Replace(source, "").Count(char.IsLetter);
        var value = letters < 3 ? @"([-+]?\d[\d.,:]*)" : "(.+?)";
        var sb = new StringBuilder("^");
        var last = 0;
        var prefix = "";
        var first = true;
        foreach (Match m in Placeholder().Matches(source))
        {
            var literal = source[last..m.Index].Replace("{{", "{").Replace("}}", "}");
            if (first) { prefix = literal; first = false; }
            sb.Append(Regex.Escape(literal)).Append(value);
            last = m.Index + m.Length;
        }
        sb.Append(Regex.Escape(source[last..].Replace("{{", "{").Replace("}}", "}"))).Append('$');
        // O destino usa só o índice: os valores já chegam formatados.
        var plainTarget = Placeholder().Replace(target, m => "{" + m.Groups[1].Value + "}");
        const RegexOptions options = RegexOptions.Singleline | RegexOptions.CultureInvariant;
        var pattern = sb.ToString();
        // Sem diferenciar maiúsculas só com começo fixo, para não pegar qualquer texto ("Máscara {0}").
        var anyCase = prefix.Count(char.IsLetter) >= 3 ? new Regex(pattern, options | RegexOptions.IgnoreCase) : null;
        return new Template(new Regex(pattern, options), anyCase, plainTarget, prefix);
    }

    private static string Unescape(string s) =>
        s.Contains('\\') ? s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\") : s;

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();
}
