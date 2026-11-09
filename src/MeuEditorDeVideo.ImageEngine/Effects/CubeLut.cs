using System.IO;
using System.Collections.Concurrent;
using System.Globalization;

namespace MeuEditorDeVideo.ImageEngine.Effects;

/// <summary>Tabela de cores 3D no formato .cube (Adobe/Resolve), com interpolação trilinear (RF099).</summary>
public sealed class CubeLut
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, CubeLut Lut)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly float[] _data; // r varia mais rápido, depois g, depois b
    private readonly float _min;
    private readonly float _scale;

    private CubeLut(int size, float[] data, float min, float max, string title)
    {
        Size = size;
        _data = data;
        _min = min;
        _scale = max > min ? 1f / (max - min) : 1f;
        Title = title;
    }

    public int Size { get; }
    public string Title { get; }

    /// <summary>Carrega (com cache pela data de modificação). Lança <see cref="InvalidDataException"/> com mensagem em pt-BR.</summary>
    public static CubeLut Load(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Lut;
        CubeLut lut;
        using (var reader = new StreamReader(path)) lut = Parse(reader);
        Cache[path] = (stamp, lut);
        return lut;
    }

    public static CubeLut Parse(TextReader reader)
    {
        var size = 0;
        float min = 0, max = 1;
        var title = "";
        List<float>? values = null;
        string? line;
        var lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0].ToUpperInvariant())
            {
                case "TITLE":
                    title = line[5..].Trim().Trim('"');
                    continue;
                case "LUT_3D_SIZE":
                    if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size is < 2 or > 256)
                        throw new InvalidDataException($"Tamanho de LUT inválido na linha {lineNo}.");
                    values = new List<float>(size * size * size * 3);
                    continue;
                case "LUT_1D_SIZE":
                    throw new InvalidDataException("LUTs 1D não são suportadas; use uma LUT 3D (.cube).");
                case "DOMAIN_MIN":
                    min = ParseFloat(parts, 1, lineNo);
                    continue;
                case "DOMAIN_MAX":
                    max = ParseFloat(parts, 1, lineNo);
                    continue;
            }
            if (char.IsLetter(parts[0][0])) continue; // palavras-chave desconhecidas
            if (values is null) throw new InvalidDataException("Arquivo .cube sem LUT_3D_SIZE antes dos dados.");
            if (parts.Length < 3) throw new InvalidDataException($"Linha {lineNo} da LUT deveria ter 3 valores.");
            for (var i = 0; i < 3; i++) values.Add(ParseFloat(parts, i, lineNo));
        }
        if (values is null || size == 0) throw new InvalidDataException("O arquivo não é uma LUT .cube válida.");
        if (values.Count != size * size * size * 3)
            throw new InvalidDataException($"A LUT deveria ter {size * size * size} cores, mas tem {values.Count / 3}.");
        return new CubeLut(size, [.. values], min, max, title);
    }

    private static float ParseFloat(string[] parts, int index, int lineNo) =>
        index < parts.Length && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new InvalidDataException($"Valor inválido na linha {lineNo} da LUT.");

    /// <summary>Aplica a LUT a uma cor (componentes 0–1).</summary>
    public (float R, float G, float B) Apply(float r, float g, float b)
    {
        var n = Size - 1;
        var fr = Math.Clamp((r - _min) * _scale, 0, 1) * n;
        var fg = Math.Clamp((g - _min) * _scale, 0, 1) * n;
        var fb = Math.Clamp((b - _min) * _scale, 0, 1) * n;
        int r0 = Math.Min((int)fr, n - 1), g0 = Math.Min((int)fg, n - 1), b0 = Math.Min((int)fb, n - 1);
        float dr = fr - r0, dg = fg - g0, db = fb - b0;
        var s = Size;

        float Lerp3(int c)
        {
            float At(int ri, int gi, int bi) => _data[((bi * s + gi) * s + ri) * 3 + c];
            var c00 = At(r0, g0, b0) + (At(r0 + 1, g0, b0) - At(r0, g0, b0)) * dr;
            var c10 = At(r0, g0 + 1, b0) + (At(r0 + 1, g0 + 1, b0) - At(r0, g0 + 1, b0)) * dr;
            var c01 = At(r0, g0, b0 + 1) + (At(r0 + 1, g0, b0 + 1) - At(r0, g0, b0 + 1)) * dr;
            var c11 = At(r0, g0 + 1, b0 + 1) + (At(r0 + 1, g0 + 1, b0 + 1) - At(r0, g0 + 1, b0 + 1)) * dr;
            var c0 = c00 + (c10 - c00) * dg;
            var c1 = c01 + (c11 - c01) * dg;
            return c0 + (c1 - c0) * db;
        }

        return (Lerp3(0), Lerp3(1), Lerp3(2));
    }
}
