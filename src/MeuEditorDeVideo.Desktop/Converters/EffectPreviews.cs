using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Presentation.Media;

namespace MeuEditorDeVideo.Desktop.Converters;

/// <summary>Chave do efeito/predefinição → miniatura (<see cref="EffectPreviews"/>).</summary>
public sealed class EffectPreviewConverter : IValueConverter
{
    public static EffectPreviewConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        EffectPreviews.Get(value as string) is { } image ? BgraBitmaps.ToBitmap(image) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Faixa em gradiente que mostra o que um parâmetro de cor faz (temperatura azul→laranja, matiz em arco-íris…);
/// null para parâmetros sem representação de cor.
/// </summary>
public sealed class ParameterGradientConverter : IValueConverter
{
    public static ParameterGradientConverter Instance { get; } = new();

    private static readonly ConcurrentDictionary<string, IBrush?> Brushes = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Brushes.GetOrAdd(key, Create) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    private static IBrush? Create(string key)
    {
        Color[]? stops = HslStops(key) ?? key switch
        {
            "temperature" => [Rgb(70, 140, 255), Rgb(235, 235, 235), Rgb(255, 160, 40)],
            "tint" => [Rgb(60, 200, 80), Rgb(235, 235, 235), Rgb(220, 70, 220)],
            "saturation" => [Rgb(128, 128, 128), Rgb(255, 40, 40), Rgb(255, 210, 0), Rgb(40, 200, 80), Rgb(40, 120, 255)],
            "hue" => [Rgb(0, 255, 255), Rgb(0, 0, 255), Rgb(255, 0, 255), Rgb(255, 0, 0), Rgb(255, 255, 0), Rgb(0, 255, 0), Rgb(0, 255, 255)],
            "exposure" or "brightness" or "highlights" or "luminance" => [Rgb(0, 0, 0), Rgb(255, 255, 255)],
            "shadows" => [Rgb(0, 0, 0), Rgb(110, 110, 110)],
            "contrast" => [Rgb(128, 128, 128), Rgb(0, 0, 0), Rgb(255, 255, 255)],
            "intensity" or "amount" => [Color.FromArgb(40, 77, 141, 255), Color.FromArgb(255, 77, 141, 255)],
            _ => null,
        };
        if (stops is null) return null;
        var gradient = new List<ImmutableGradientStop>();
        for (var i = 0; i < stops.Length; i++) gradient.Add(new ImmutableGradientStop(i / (double)(stops.Length - 1), stops[i]));
        return new ImmutableLinearGradientBrush(gradient, startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
            endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>Faixas do HSL ("blue.h", "blue.s", "blue.l"): matizes vizinhas, cinza→cor e preto→cor→branco.</summary>
    private static Color[]? HslStops(string key)
    {
        var dot = key.LastIndexOf('.');
        if (dot <= 0) return null;
        var band = EffectCatalog.HslBands.FirstOrDefault(b => b.Key == key[..dot]);
        if (band.Key is null) return null;
        var c = Hue(band.Hue, 0.5);
        return key[(dot + 1)..] switch
        {
            "h" => [Hue(band.Hue - 30, 0.5), c, Hue(band.Hue + 30, 0.5)],
            "s" => [Rgb(128, 128, 128), c],
            "l" => [Rgb(0, 0, 0), c, Rgb(255, 255, 255)],
            _ => null,
        };
    }

    private static Color Hue(double degrees, double lightness)
    {
        var h = ((degrees % 360) + 360) % 360 / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch { 0 => (1d, x, 0d), 1 => (x, 1d, 0d), 2 => (0d, 1d, x), 3 => (0d, x, 1d), 4 => (x, 0d, 1d), _ => (1d, 0d, x) };
        var k = lightness * 2;
        return Rgb((byte)(255 * Math.Min(1, r * k)), (byte)(255 * Math.Min(1, g * k)), (byte)(255 * Math.Min(1, b * k)));
    }
}

/// <summary>(chave, conjunto de chaves aplicadas) → a opção do catálogo já está aplicada ao clipe.</summary>
public sealed class AppliedKeyConverter : IMultiValueConverter
{
    public static AppliedKeyConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [string key, IReadOnlySet<string> applied] && applied.Contains(key);
}
