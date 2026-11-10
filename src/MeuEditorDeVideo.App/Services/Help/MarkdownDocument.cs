using System.Text;

namespace MeuEditorDeVideo.App.Services.Help;

/// <summary>Trecho de texto de um parágrafo: normal, <b>negrito</b> ou <c>código</c>.</summary>
public sealed record MdInline(string Text, bool Bold = false, bool Code = false);

/// <summary>Bloco do manual (subconjunto de Markdown usado pela ajuda offline).</summary>
public abstract record MdBlock;

public sealed record MdHeading(int Level, string Text, string Anchor) : MdBlock;

public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdNote(IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdList(bool Numbered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : MdBlock;

public sealed record MdTable(IReadOnlyList<IReadOnlyList<MdInline>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows) : MdBlock;

/// <summary>
/// Interpreta o manual do usuário (Markdown simples: títulos #/##/###, parágrafos, listas "- " e "1. ", tabelas com "|",
/// notas "> ", **negrito** e `código`). Sem HTML, imagens ou links: a ajuda é local e não abre nada externo.
/// </summary>
public static class MarkdownDocument
{
    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new StringBuilder();
        var anchors = new HashSet<string>(StringComparer.Ordinal);

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            blocks.Add(new MdParagraph(Inlines(paragraph.ToString())));
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (HeadingLevel(trimmed) is { } level)
            {
                FlushParagraph();
                var text = trimmed[(level + 1)..].Trim();
                blocks.Add(new MdHeading(level, text, UniqueAnchor(text, anchors)));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                blocks.Add(new MdNote(Inlines(trimmed.TrimStart('>').Trim())));
                continue;
            }

            if (ListItem(trimmed) is { } first)
            {
                FlushParagraph();
                var numbered = first.Numbered;
                var items = new List<IReadOnlyList<MdInline>> { Inlines(first.Text) };
                while (i + 1 < lines.Length && ListItem(lines[i + 1].Trim()) is { } next && next.Numbered == numbered)
                {
                    items.Add(Inlines(next.Text));
                    i++;
                }
                blocks.Add(new MdList(numbered, items));
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1].Trim()))
            {
                FlushParagraph();
                var header = Cells(trimmed).Select(Inlines).ToList();
                var rows = new List<IReadOnlyList<IReadOnlyList<MdInline>>>();
                i++; // separador
                while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith('|'))
                {
                    i++;
                    rows.Add(Cells(lines[i].Trim()).Select(Inlines).ToList());
                }
                blocks.Add(new MdTable(header, rows));
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    private static int? HeadingLevel(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;
        return level is >= 1 and <= 6 && level < line.Length && line[level] == ' ' ? level : null;
    }

    private static (bool Numbered, string Text)? ListItem(string line)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) return (false, line[2..].Trim());
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
        return digits > 0 && digits + 1 < line.Length && line[digits] == '.' && line[digits + 1] == ' '
            ? (true, line[(digits + 2)..].Trim())
            : null;
    }

    private static bool IsTableSeparator(string line) =>
        line.StartsWith('|') && line.Contains('-') && line.All(c => c is '|' or '-' or ':' or ' ');

    private static IEnumerable<string> Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(c => c.Trim());

    /// <summary>Âncora estável para o sumário (texto do título em minúsculas, sem pontuação).</summary>
    private static string UniqueAnchor(string text, HashSet<string> used)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var anchor = sb.ToString().Trim('-');
        var candidate = anchor;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{anchor}-{n}";
        return candidate;
    }

    /// <summary>Divide o texto em trechos normais, **negrito** e `código`. Marcadores sem par ficam como texto.</summary>
    public static IReadOnlyList<MdInline> Inlines(string text)
    {
        var result = new List<MdInline>();
        var current = new StringBuilder();
        var i = 0;

        void Flush()
        {
            if (current.Length > 0) result.Add(new MdInline(current.ToString()));
            current.Clear();
        }

        while (i < text.Length)
        {
            if (text[i] == '`' && text.IndexOf('`', i + 1) is var endCode and > 0)
            {
                Flush();
                result.Add(new MdInline(text[(i + 1)..endCode], Code: true));
                i = endCode + 1;
            }
            else if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*' && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var endBold and > 0)
            {
                Flush();
                result.Add(new MdInline(text[(i + 2)..endBold], Bold: true));
                i = endBold + 2;
            }
            else
            {
                current.Append(text[i]);
                i++;
            }
        }

        Flush();
        return result;
    }
}
