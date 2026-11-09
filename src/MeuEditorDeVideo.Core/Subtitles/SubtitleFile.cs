using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Core.Subtitles;

/// <summary>Formatos de legenda suportados.</summary>
public enum SubtitleFormat { Srt, Vtt }

/// <summary>Uma legenda: intervalo na linha do tempo e texto (linhas separadas por '\n').</summary>
public sealed record SubtitleCue(MediaTime Start, MediaTime End, string Text);

/// <summary>Erro de leitura de um arquivo de legenda.</summary>
public sealed class SubtitleParseException(string message) : Exception(message);

/// <summary>Leitura e escrita de legendas SRT e WebVTT, sem dependência de interface gráfica.</summary>
public static class SubtitleFile
{
    private const long TicksPerMillisecond = MediaTime.TicksPerSecond / 1000;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1252 = RegisterCodePages();

    // Intervalo "HH:MM:SS,mmm --> HH:MM:SS,mmm" (SRT) ou "MM:SS.mmm --> ..." (VTT), com configurações opcionais.
    private static readonly Regex TimingRegex = new(
        @"^\s*(?:(\d{1,3}):)?(\d{1,2}):(\d{2})[,.](\d{1,3})\s*-->\s*(?:(\d{1,3}):)?(\d{1,2}):(\d{2})[,.](\d{1,3})(?:\s.*)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AssOverrideRegex = new(@"\{\\[^}]*\}", RegexOptions.CultureInvariant);
    private static readonly Regex VttTimestampRegex = new(@"<\d{1,2}:\d{2}(?::\d{2})?\.\d{1,3}>", RegexOptions.CultureInvariant);
    private static readonly Regex MarkupTagRegex = new(@"</?[A-Za-z][^<>]*>", RegexOptions.CultureInvariant);

    /// <summary>Retorna o formato pela extensão (.srt/.vtt, sem distinguir maiúsculas), ou null.</summary>
    public static SubtitleFormat? FormatFromExtension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)) return SubtitleFormat.Srt;
        if (extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase)) return SubtitleFormat.Vtt;
        return null;
    }

    /// <summary>Interpreta o conteúdo de uma legenda. Lança <see cref="SubtitleParseException"/> se o formato for inválido.</summary>
    public static IReadOnlyList<SubtitleCue> Parse(string content, SubtitleFormat format)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = Normalize(content);

        if (format == SubtitleFormat.Vtt)
        {
            if (!IsVttHeader(FirstLine(text.TrimStart())))
                throw new SubtitleParseException("Arquivo WebVTT inválido: falta o cabeçalho WEBVTT.");
        }
        else if (text.Trim().Length == 0)
        {
            return Array.Empty<SubtitleCue>();
        }

        var cues = new List<SubtitleCue>();
        var timedBlocks = 0;
        foreach (var block in SplitBlocks(text))
        {
            if (format == SubtitleFormat.Vtt && IsIgnoredVttBlock(block[0])) continue;

            var timingIndex = FindTiming(block, out var start, out var end);
            if (timingIndex < 0) continue;
            timedBlocks++;

            if (end <= start) continue;
            var body = CleanText(block, timingIndex + 1);
            if (body.Length == 0) continue;
            cues.Add(new SubtitleCue(start, end, body));
        }

        if (format == SubtitleFormat.Srt && timedBlocks == 0)
            throw new SubtitleParseException("O arquivo não contém legendas válidas.");

        return cues.OrderBy(c => c.Start).ToList();
    }

    /// <summary>Lê um arquivo de legenda. O formato vem da extensão; sem extensão reconhecida, do cabeçalho do conteúdo.</summary>
    public static IReadOnlyList<SubtitleCue> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var content = Decode(File.ReadAllBytes(path));
        var format = FormatFromExtension(path)
            ?? (Normalize(content).TrimStart().StartsWith("WEBVTT", StringComparison.Ordinal) ? SubtitleFormat.Vtt : SubtitleFormat.Srt);
        return Parse(content, format);
    }

    /// <summary>Gera o texto de uma legenda no formato indicado (quebras de linha '\n'). Cues vazios ou inválidos são omitidos.</summary>
    public static string Write(IEnumerable<SubtitleCue> cues, SubtitleFormat format)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var sb = new StringBuilder();
        if (format == SubtitleFormat.Vtt) sb.Append("WEBVTT\n\n");

        var index = 0;
        foreach (var cue in cues)
        {
            var start = MediaTime.Max(cue.Start, MediaTime.Zero);
            var end = MediaTime.Max(cue.End, MediaTime.Zero);
            if (end <= start) continue;

            var lines = (cue.Text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => l.Length > 0)
                .ToList();
            if (lines.Count == 0) continue;

            index++;
            if (format == SubtitleFormat.Srt)
                sb.Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');

            sb.Append(FormatTime(start, format)).Append(" --> ").Append(FormatTime(end, format)).Append('\n');
            foreach (var line in lines) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Grava a legenda em UTF-8 sem BOM (CRLF para SRT, LF para VTT).</summary>
    public static void Save(string path, IEnumerable<SubtitleCue> cues, SubtitleFormat format)
    {
        ArgumentNullException.ThrowIfNull(path);
        var text = Write(cues, format);
        if (format == SubtitleFormat.Srt) text = text.Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Decodifica bytes: honra BOM UTF-8/UTF-16; sem BOM tenta UTF-8 estrito e, se inválido, Windows-1252.</summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        string text;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
        {
            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = Windows1252.GetString(bytes);
            }
        }

        return text.TrimStart('﻿');
    }

    private static Encoding RegisterCodePages()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimStart('﻿');

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? text : text[..newline];
    }

    private static bool IsVttHeader(string line) =>
        line.StartsWith("WEBVTT", StringComparison.Ordinal)
        && (line.Length == 6 || line[6] == ' ' || line[6] == '\t');

    private static bool IsIgnoredVttBlock(string firstLine) =>
        IsKeyword(firstLine, "NOTE") || IsKeyword(firstLine, "STYLE") || IsKeyword(firstLine, "REGION");

    private static bool IsKeyword(string line, string keyword) =>
        line.StartsWith(keyword, StringComparison.Ordinal)
        && (line.Length == keyword.Length || line[keyword.Length] == ' ' || line[keyword.Length] == '\t');

    /// <summary>Separa o texto em blocos delimitados por uma ou mais linhas em branco.</summary>
    private static List<List<string>> SplitBlocks(string text)
    {
        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                if (current.Count > 0)
                {
                    blocks.Add(current);
                    current = new List<string>();
                }
            }
            else
            {
                current.Add(line);
            }
        }

        if (current.Count > 0) blocks.Add(current);
        return blocks;
    }

    /// <summary>Retorna o índice da linha de tempo do bloco (linhas antes dela, como índice ou identificador, são ignoradas), ou -1.</summary>
    private static int FindTiming(List<string> block, out MediaTime start, out MediaTime end)
    {
        for (var i = 0; i < block.Count; i++)
        {
            var match = TimingRegex.Match(block[i]);
            if (!match.Success) continue;

            start = ToTime(match.Groups[1], match.Groups[2], match.Groups[3], match.Groups[4]);
            end = ToTime(match.Groups[5], match.Groups[6], match.Groups[7], match.Groups[8]);
            return i;
        }

        start = MediaTime.Zero;
        end = MediaTime.Zero;
        return -1;
    }

    private static MediaTime ToTime(Group hours, Group minutes, Group seconds, Group fraction)
    {
        var h = hours.Success ? long.Parse(hours.Value, CultureInfo.InvariantCulture) : 0;
        var m = long.Parse(minutes.Value, CultureInfo.InvariantCulture);
        var s = long.Parse(seconds.Value, CultureInfo.InvariantCulture);
        var ms = long.Parse(fraction.Value.PadRight(3, '0'), CultureInfo.InvariantCulture);
        var totalMs = ((h * 60 + m) * 60 + s) * 1000 + ms;
        return new MediaTime(totalMs * TicksPerMillisecond);
    }

    private static string CleanText(List<string> block, int from)
    {
        var lines = new List<string>();
        for (var i = from; i < block.Count; i++)
        {
            var line = CleanLine(block[i]);
            if (line.Length > 0) lines.Add(line);
        }

        return string.Join('\n', lines);
    }

    private static string CleanLine(string line)
    {
        var s = AssOverrideRegex.Replace(line, string.Empty);
        s = VttTimestampRegex.Replace(s, string.Empty);
        s = MarkupTagRegex.Replace(s, string.Empty);
        s = s.Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.Ordinal)
            .Replace("&lrm;", string.Empty, StringComparison.Ordinal)
            .Replace("&rlm;", string.Empty, StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
        return s.TrimEnd();
    }

    private static string FormatTime(MediaTime time, SubtitleFormat format)
    {
        var ms = (long)Math.Round(time.Ticks / (double)TicksPerMillisecond);
        var hours = ms / 3_600_000;
        var minutes = ms / 60_000 % 60;
        var seconds = ms / 1000 % 60;
        var millis = ms % 1000;
        var separator = format == SubtitleFormat.Srt ? "," : ".";
        return FormattableString.Invariant($"{hours:00}:{minutes:00}:{seconds:00}{separator}{millis:000}");
    }
}
