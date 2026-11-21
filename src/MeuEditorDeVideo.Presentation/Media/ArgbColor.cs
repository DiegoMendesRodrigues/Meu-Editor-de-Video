using System.Globalization;

namespace MeuEditorDeVideo.Presentation.Media;

/// <summary>Cor ARGB de 8 bits por canal, independente do toolkit (a view converte para o pincel dela).</summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static ArgbColor Transparent => default;

    /// <summary>"#AARRGGBB": formato que os toolkits aceitam direto em bindings de pincel.</summary>
    public string ToArgbHex() => string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");

    public override string ToString() => ToArgbHex();
}
