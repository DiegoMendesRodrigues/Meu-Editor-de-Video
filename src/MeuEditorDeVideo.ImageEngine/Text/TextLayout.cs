using System.Collections.Concurrent;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace MeuEditorDeVideo.ImageEngine.Text;

/// <summary>
/// Diagramação de texto com HarfBuzz (kerning, ligaduras e alfabetos complexos) e substituição de fonte por caractere:
/// quebra de linha por palavras (e por caractere quando uma palavra não cabe), quebras explícitas e contornos dos glifos.
/// </summary>
internal sealed class TextLayout
{
    /// <summary>Fração da altura usada para inclinar o texto quando a família não tem itálico (como o WPF).</summary>
    private const float FakeItalicSkew = -0.2f;

    private static readonly ConcurrentDictionary<SKTypeface, Shaper> Shapers = new();

    private readonly ResolvedFont _font;
    private readonly float _size;

    public TextLayout(ResolvedFont font, double size)
    {
        _font = font;
        _size = (float)size;
        using var f = MakeFont(font.Typeface);
        f.GetFontMetrics(out var m);
        Ascent = -m.Ascent;
        Descent = m.Descent;
        Leading = Math.Max(0, m.Leading);
        UnderlinePosition = m.UnderlinePosition is > 0 and var up ? up : _size * 0.1f;
        UnderlineThickness = m.UnderlineThickness is > 0 and var ut ? ut : Math.Max(1, _size * 0.05f);
    }

    public float Ascent { get; }
    public float Descent { get; }
    public float Leading { get; }
    public float UnderlinePosition { get; }
    public float UnderlineThickness { get; }

    /// <summary>Altura de linha padrão da fonte.</summary>
    public float NaturalLineHeight => Ascent + Descent + Leading;

    /// <summary>Linha diagramada: intervalo [Start, End) do texto original, sem a quebra de linha.</summary>
    public readonly record struct Line(int Start, int End);

    /// <summary>Quebra o texto em linhas que cabem em <paramref name="maxWidth"/>.</summary>
    public List<Line> Wrap(string text, double maxWidth)
    {
        var lines = new List<Line>();
        var paragraphStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n') continue;
            var end = i > paragraphStart && text[i - 1] == '\r' ? i - 1 : i;
            WrapParagraph(text, paragraphStart, end, maxWidth, lines);
            paragraphStart = i + 1;
        }
        return lines;
    }

    private void WrapParagraph(string text, int start, int end, double maxWidth, List<Line> lines)
    {
        if (start >= end)
        {
            lines.Add(new Line(start, start));
            return;
        }
        var lineStart = start;
        var pos = start;
        while (pos < end)
        {
            // Próxima palavra com os espaços que a seguem.
            var wordEnd = pos;
            while (wordEnd < end && !char.IsWhiteSpace(text[wordEnd])) wordEnd++;
            var tokenEnd = wordEnd;
            while (tokenEnd < end && char.IsWhiteSpace(text[tokenEnd])) tokenEnd++;

            if (Measure(text, lineStart, wordEnd) <= maxWidth || lineStart == pos && wordEnd == pos)
            {
                pos = tokenEnd;
                continue;
            }
            if (lineStart < pos)
            {
                // A palavra vai para a próxima linha.
                lines.Add(new Line(lineStart, pos));
                lineStart = pos;
                continue;
            }
            // Palavra maior que a linha inteira: quebra entre caracteres.
            var cut = pos + char.ConvertFromUtf32(char.ConvertToUtf32(text, pos)).Length;
            while (cut < wordEnd)
            {
                var next = cut + (char.IsSurrogatePair(text, cut) ? 2 : 1);
                if (Measure(text, lineStart, next) > maxWidth) break;
                cut = next;
            }
            lines.Add(new Line(lineStart, cut));
            lineStart = pos = cut;
        }
        if (lineStart < end) lines.Add(new Line(lineStart, end));
    }

    /// <summary>Largura do trecho sem os espaços finais.</summary>
    public float Measure(string text, int start, int end)
    {
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (end <= start) return 0;
        float width = 0;
        foreach (var run in Runs(text, start, end)) width += Shape(run).Width;
        return width;
    }

    /// <summary>Contornos dos glifos de [start, end) a partir da origem (x, linha de base).</summary>
    public void AddGlyphs(SKPathBuilder path, string text, int start, int end, float x, float baseline)
    {
        foreach (var run in Runs(text, start, end))
        {
            var shaped = Shape(run);
            using var font = MakeFont(run.Face);
            for (var i = 0; i < shaped.Codepoints.Length; i++)
            {
                using var glyph = font.GetGlyphPath((ushort)shaped.Codepoints[i]);
                if (glyph is null || glyph.IsEmpty) continue;
                var p = shaped.Points[i];
                var m = SKMatrix.CreateTranslation(x + p.X, baseline + p.Y);
                if (_font.FakeItalic) m = m.PreConcat(SKMatrix.CreateSkew(FakeItalicSkew, 0));
                path.AddPath(glyph, in m);
            }
            x += shaped.Width;
        }
    }

    private readonly record struct Run(SKTypeface Face, string Text);

    /// <summary>Divide o trecho em partes de mesma face (fonte pedida ou substituta).</summary>
    private IEnumerable<Run> Runs(string text, int start, int end)
    {
        var sb = new StringBuilder();
        SKTypeface? current = null;
        var i = start;
        while (i < end)
        {
            var cp = char.ConvertToUtf32(text, i);
            var len = char.IsSurrogatePair(text, i) ? 2 : 1;
            // Seletores de variação e marcas combinantes ficam com o caractere anterior.
            var face = current is not null && IsCombining(cp) ? current : FontCatalog.ForCodepoint(_font, cp);
            if (current is not null && !ReferenceEquals(face, current))
            {
                yield return new Run(current, sb.ToString());
                sb.Clear();
            }
            current = face;
            sb.Append(text, i, len);
            i += len;
        }
        if (current is not null && sb.Length > 0) yield return new Run(current, sb.ToString());
    }

    private static bool IsCombining(int cp) =>
        cp is >= 0xFE00 and <= 0xFE0F or 0x200D or >= 0x1F3FB and <= 0x1F3FF ||
        cp <= 0xFFFF && char.GetUnicodeCategory((char)cp) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.EnclosingMark;

    private SKShaper.Result Shape(Run run)
    {
        var shaper = Shapers.GetOrAdd(run.Face, f => new Shaper(new SKShaper(f)));
        using var font = MakeFont(run.Face);
        lock (shaper.Gate) return shaper.Value.Shape(run.Text, font);
    }

    private SKFont MakeFont(SKTypeface face) => new(face, _size)
    {
        Subpixel = true,
        Hinting = SKFontHinting.None,
        Edging = SKFontEdging.Antialias,
        LinearMetrics = true,
    };

    /// <summary>O HarfBuzz altera o estado do shaper a cada uso: um por face, usado com trava.</summary>
    private sealed class Shaper(SKShaper value)
    {
        public SKShaper Value { get; } = value;
        public Lock Gate { get; } = new();
    }
}
