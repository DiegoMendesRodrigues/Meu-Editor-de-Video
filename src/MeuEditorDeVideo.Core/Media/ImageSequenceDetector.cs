using System.Globalization;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Core.Media;

/// <summary>Detecta sequências de imagens numeradas a partir de um dos arquivos (RF025).</summary>
public static partial class ImageSequenceDetector
{
    [GeneratedRegex(@"^(?<prefix>.*?)(?<num>\d+)(?<suffix>\.[^.]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedName();

    /// <summary>
    /// Retorna a sequência contínua que contém <paramref name="anyFrame"/> (mínimo 2 quadros), ou null.
    /// </summary>
    public static ImageSequenceInfo? Detect(string anyFrame)
    {
        if (!MediaFormats.IsImage(anyFrame) || anyFrame.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null; // SVG é vetorial (não vira sequência)
        var directory = Path.GetDirectoryName(Path.GetFullPath(anyFrame));
        if (directory is null || !Directory.Exists(directory)) return null;

        var match = NumberedName().Match(Path.GetFileName(anyFrame));
        if (!match.Success) return null;

        var prefix = match.Groups["prefix"].Value;
        var suffix = match.Groups["suffix"].Value;
        var selectedDigits = match.Groups["num"].Value;
        if (!int.TryParse(selectedDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var selected)) return null;

        // Coleta números de todos os arquivos com o mesmo prefixo/sufixo.
        var numbers = new Dictionary<int, string>();
        foreach (var file in Directory.EnumerateFiles(directory, prefix + "*" + suffix))
        {
            var m = NumberedName().Match(Path.GetFileName(file));
            if (!m.Success
                || !string.Equals(m.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(m.Groups["suffix"].Value, suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(m.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                numbers.TryAdd(n, m.Groups["num"].Value);
        }

        if (!numbers.ContainsKey(selected)) return null;

        var first = selected;
        while (numbers.ContainsKey(first - 1)) first--;
        var last = selected;
        while (numbers.ContainsKey(last + 1)) last++;
        var count = last - first + 1;
        if (count < 2) return null;

        // Preenchimento com zeros: todos os nomes da sequência com o mesmo número de dígitos e algum com zero à esquerda.
        var lengths = Enumerable.Range(first, count).Select(n => numbers[n].Length).Distinct().ToList();
        var padded = lengths.Count == 1 && Enumerable.Range(first, count).Any(n => numbers[n].StartsWith('0') && numbers[n].Length > 1);
        var digits = padded ? lengths[0] : 0;

        // Sem zeros à esquerda mas nomes de tamanho fixo (ex.: 100…999) também são representáveis sem preenchimento.
        return new ImageSequenceInfo(directory, prefix, suffix, digits, first, count);
    }
}
