// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using MeuEditorDeVideo.Core.Help;

namespace MeuEditorDeVideo.Application.Help;

/// <summary>
/// Base de conhecimento do assistente virtual: os manuais do usuário (<c>MANUAL_DO_USUARIO[.idioma].md</c>, na pasta
/// <c>ajuda</c> do aplicativo) divididos nas seções <c>##</c>/<c>###</c>, com busca BM25 local.
/// A busca ignora acentos, maiúsculas e palavras vazias e compara radicais (os 4 e os 7 primeiros caracteres), o que aproxima
/// "legenda/legendas", "exportar/exportação" e "remove/removing" sem dicionário nem modelo de IA. O título da seção
/// pesa mais que o texto. Cada idioma é lido uma vez, na primeira busca.
/// </summary>
public sealed class ManualKnowledgeBase(string folder) : IHelpKnowledgeBase
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const int ShortStem = 4, LongStem = 7;
    private const int TitleWeight = 3;

    private readonly ConcurrentDictionary<string, Index> _indexes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivo do manual no idioma do aplicativo (o pt-BR é o original; idiomas sem manual usam o pt-BR).</summary>
    public string ManualPath(string language)
    {
        if (!string.IsNullOrEmpty(language) && !language.Equals("pt-BR", StringComparison.OrdinalIgnoreCase))
        {
            var localized = Path.Combine(folder, $"MANUAL_DO_USUARIO.{language}.md");
            if (File.Exists(localized)) return localized;
        }
        return Path.Combine(folder, "MANUAL_DO_USUARIO.md");
    }

    public IReadOnlyList<HelpSection> Sections(string language) => IndexFor(language).Sections;

    public IReadOnlyList<HelpSearchHit> Search(string question, string language, int max = 4)
    {
        ArgumentNullException.ThrowIfNull(question);
        var index = IndexFor(language);
        var terms = Terms(question).Distinct().ToList();
        if (terms.Count == 0 || index.Sections.Count == 0) return [];

        var scores = new double[index.Sections.Count];
        foreach (var term in terms)
        {
            if (!index.DocumentFrequency.TryGetValue(term, out var df)) continue;
            var idf = Math.Log(1 + (index.Sections.Count - df + 0.5) / (df + 0.5));
            for (var i = 0; i < scores.Length; i++)
            {
                if (!index.Frequencies[i].TryGetValue(term, out var tf)) continue;
                var norm = K1 * (1 - B + B * index.Lengths[i] / index.AverageLength);
                scores[i] += idf * tf * (K1 + 1) / (tf + norm);
            }
        }
        return scores
            .Select((s, i) => new HelpSearchHit(index.Sections[i], s))
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Section.Line)
            .Take(Math.Max(1, max))
            .ToList();
    }

    private Index IndexFor(string language) =>
        _indexes.GetOrAdd(string.IsNullOrEmpty(language) ? "pt-BR" : language, lang => Build(lang, ManualPath(lang)));

    private static Index Build(string language, string path)
    {
        var sections = File.Exists(path) ? Parse(language, File.ReadAllText(path)) : [];
        var frequencies = new Dictionary<string, int>[sections.Count];
        var lengths = new int[sections.Count];
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var tf = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(IEnumerable<string> terms, int weight)
            {
                foreach (var t in terms)
                {
                    tf[t] = tf.GetValueOrDefault(t) + weight;
                    lengths[i] += weight;
                }
            }
            Add(Terms(s.Title), TitleWeight);
            if (s.Chapter != s.Title) Add(Terms(s.Chapter), 1);
            Add(Terms(s.Text), 1);
            frequencies[i] = tf;
            foreach (var t in tf.Keys) df[t] = df.GetValueOrDefault(t) + 1;
        }
        var average = sections.Count == 0 ? 1 : Math.Max(1, lengths.Average());
        return new Index(sections, frequencies, lengths, average, df);
    }

    /// <summary>Divide o Markdown nas seções <c>##</c> e <c>###</c> (títulos sem texto são ignorados).</summary>
    public static IReadOnlyList<HelpSection> Parse(string language, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sections = new List<HelpSection>();
        string chapter = "";
        int start = -1, level = 0;
        string title = "";
        void Close(int end)
        {
            if (start < 0) return;
            // As linhas de imagem ("![descrição](arquivo)") não entram no texto pesquisado nem nas respostas.
            var text = string.Join("\n", lines[(start + 1)..end].Where(l => !l.TrimStart().StartsWith("![", StringComparison.Ordinal))).Trim();
            if (text.Length > 0) sections.Add(new HelpSection(language, start + 1, level, chapter, title, text));
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var headingLevel = line.StartsWith("### ", StringComparison.Ordinal) ? 3
                : line.StartsWith("## ", StringComparison.Ordinal) ? 2
                : line.StartsWith("# ", StringComparison.Ordinal) ? 1 : 0;
            if (headingLevel == 0) continue;
            Close(i);
            if (headingLevel == 1)
            {
                start = -1; // título do documento: a introdução não vira seção
                continue;
            }
            title = line[(headingLevel + 1)..].Trim();
            if (headingLevel == 2) chapter = title;
            start = i;
            level = headingLevel;
        }
        Close(lines.Length);
        return sections;
    }

    /// <summary>Termos de busca: sem acentos, minúsculos, sem palavras vazias, reduzidos ao radical.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        var folded = Fold(text);
        var word = new StringBuilder();
        foreach (var c in folded.Append(' '))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }
            if (word.Length == 0) continue;
            var w = word.ToString();
            word.Clear();
            if (w.Length < 2 || StopWords.Contains(w)) continue;
            // Dois radicais: o curto aproxima conjugações e plurais (junto, juntar); o longo mantém a precisão.
            if (w.Length <= ShortStem)
            {
                yield return w;
                continue;
            }
            yield return w[..ShortStem];
            yield return w.Length > LongStem ? w[..LongStem] : w;
        }
    }

    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c == 'ß' ? 's' : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // Palavras sem conteúdo nos cinco idiomas dos manuais (já sem acento), mais termos de pergunta ("como", "how", "posso").
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // português
        "a", "o", "as", "os", "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "por",
        "pelo", "pela", "para", "pra", "com", "sem", "que", "se", "e", "ou", "mas", "como", "qual", "quais", "quando", "onde",
        "eu", "me", "meu", "minha", "meus", "minhas", "voce", "seu", "sua", "seus", "suas", "ele", "ela", "isso", "isto", "esse",
        "essa", "este", "esta", "ao", "aos", "ja", "nao", "sim", "mais", "muito", "tem", "ter", "ha", "foi", "ser", "sao", "e",
        "esta", "estao", "faco", "fazer", "faz", "posso", "pode", "consigo", "quero", "preciso", "devo", "da", "dar", "tambem",
        "so", "ate", "entre", "sobre", "programa", "editor", "alguma", "algum", "cada", "todo", "toda", "todos", "todas", "lhe",
        // español
        "el", "la", "los", "las", "un", "una", "del", "al", "en", "con", "por", "para", "que", "como", "cual", "cuando", "donde",
        "yo", "mi", "mis", "tu", "su", "sus", "es", "son", "hay", "hago", "puedo", "quiero", "necesito", "lo", "le", "les", "y",
        // français
        "le", "les", "un", "une", "des", "du", "de", "au", "aux", "et", "ou", "en", "dans", "sur", "pour", "par", "avec", "sans",
        "que", "qui", "quoi", "comment", "quel", "quelle", "je", "mon", "ma", "mes", "vous", "votre", "vos", "il", "elle", "est",
        "sont", "peux", "puis", "faire", "fait", "ce", "cette", "ces", "ne", "pas", "plus", "aussi",
        // deutsch
        "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "und", "oder", "mit", "ohne", "fur", "von",
        "zu", "zum", "zur", "im", "auf", "ist", "sind", "wie", "was", "wo", "wann", "ich", "mein", "meine", "sie", "ihr", "ihre",
        "es", "kann", "konnen", "mochte", "will", "muss", "nicht", "auch", "noch", "bei", "aus", "man",
        // english
        "the", "an", "of", "to", "in", "on", "at", "for", "by", "with", "without", "and", "or", "but", "how", "what", "which",
        "when", "where", "do", "does", "did", "can", "could", "should", "would", "is", "are", "was", "be", "it", "its", "this",
        "that", "these", "those", "my", "your", "you", "me", "want", "need", "there", "into", "from", "also", "program",
    };

    private sealed record Index(IReadOnlyList<HelpSection> Sections, Dictionary<string, int>[] Frequencies, int[] Lengths,
        double AverageLength, Dictionary<string, int> DocumentFrequency);
}
