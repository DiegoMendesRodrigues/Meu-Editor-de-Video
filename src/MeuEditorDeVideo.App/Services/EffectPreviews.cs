using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeuEditorDeVideo.App.ViewModels.Inspector;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.App.Services;

/// <summary>
/// Miniaturas dos efeitos e predefinições de cor: uma paisagem gerada em código passa pelo próprio
/// <see cref="EffectProcessor"/>, então a miniatura mostra o que o efeito realmente faz. Geradas uma vez e mantidas em cache.
/// </summary>
public static class EffectPreviews
{
    public const int Width = 128;
    public const int Height = 80;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.Ordinal);
    private static readonly Lazy<BgraImage> Sample = new(CreateSample);

    /// <summary>Miniatura de um tipo de efeito do catálogo ou de uma predefinição ("preset:id"); null se desconhecido.</summary>
    public static ImageSource? Get(string? key) => key is null ? null : Cache.GetOrAdd(key, Create);

    private static ImageSource? Create(string key)
    {
        IReadOnlyList<EffectInstance>? effects = key.StartsWith("preset:", StringComparison.Ordinal)
            ? ColorPresets.All.FirstOrDefault(p => p.PreviewKey == key)?.Build()
            : Demo(key);
        if (effects is null) return null;
        try
        {
            var (image, alpha) = EffectProcessor.Apply(Sample.Value, effects, new EffectContext(0.3, 7));
            return ToBitmap(alpha ? OverChecker(image) : image);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return ToBitmap(Sample.Value);
        }
    }

    /// <summary>Instância de demonstração: valores que deixam o efeito evidente numa imagem pequena.</summary>
    private static IReadOnlyList<EffectInstance>? Demo(string type)
    {
        if (EffectCatalog.Find(type) is null) return null;
        var e = EffectCatalog.Create(type);
        e = type switch
        {
            EffectCatalog.ColorAdjust => With(e, ("saturation", 40), ("temperature", 30), ("contrast", 20)),
            EffectCatalog.WhiteBalance => EffectCatalog.WithColor(e, "neutral", 0.42, 0.5, 0.66),
            EffectCatalog.Curves => EffectCatalog.WithCurve(e, "master", [(0, 0), (0.25, 0.12), (0.75, 0.88), (1, 1)]),
            EffectCatalog.Wheels => EffectCatalog.WithWheel(EffectCatalog.WithWheel(e, "lift", -0.4, -0.3, 0), "gain", 0.35, 0.25, 0.05),
            // Sem arquivo .cube, a LUT é ilustrada por uma gradação estilizada.
            EffectCatalog.Lut => With(EffectCatalog.Create(EffectCatalog.ColorAdjust), ("temperature", -30), ("tint", 25), ("contrast", 30), ("hue", 15)),
            EffectCatalog.Hsl => With(e, ("blue.h", 25), ("blue.s", 60), ("green.h", -25), ("green.s", 50), ("orange.s", 70)),
            EffectCatalog.Blur => With(e, ("radius", 12)),
            EffectCatalog.Sharpen => With(e, ("amount", 200)),
            EffectCatalog.Vignette => With(e, ("amount", 90)),
            EffectCatalog.Pixelate => With(e, ("size", 26)),
            EffectCatalog.Noise => With(e, ("amount", 60)),
            EffectCatalog.Posterize => With(e, ("levels", 4)),
            _ => e,
        };
        // Refinar bordas só faz sentido sobre uma transparência: demonstra depois do chroma key.
        return type == EffectCatalog.EdgeRefine ? [EffectCatalog.Create(EffectCatalog.ChromaKey), e] : [e];
    }

    private static EffectInstance With(EffectInstance e, params (string Key, double Value)[] values) =>
        values.Aggregate(e, (acc, v) => EffectCatalog.WithValue(acc, v.Key, v.Value));

    /// <summary>Paisagem: céu em gradiente, sol, montanhas, lago e gramado (tem verde para o chroma key).</summary>
    private static BgraImage CreateSample()
    {
        var px = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                double fy = y / (double)Height, fx = x / (double)Width;
                // Céu: azul no alto, alaranjado perto do horizonte.
                var t = Math.Clamp(fy / 0.55, 0, 1);
                double r = Lerp(70, 250, t), g = Lerp(130, 190, t), b = Lerp(220, 150, t);
                // Sol.
                var d = Math.Sqrt(Math.Pow((fx - 0.7) * 1.6, 2) + Math.Pow(fy - 0.36, 2));
                if (d < 0.09) { r = 255; g = 236; b = 170; }
                else if (d < 0.2) { var k = (0.2 - d) / 0.11 * 0.5; r = Lerp(r, 255, k); g = Lerp(g, 225, k); b = Lerp(b, 160, k); }
                // Montanhas (duas camadas).
                var far = 0.42 + 0.1 * Math.Sin(fx * 7.5 + 1.2) + 0.05 * Math.Sin(fx * 19);
                var near = 0.55 + 0.08 * Math.Sin(fx * 5 + 3.4) + 0.03 * Math.Sin(fx * 23 + 1);
                if (fy > far) { r = Lerp(120, 95, fy); g = Lerp(110, 90, fy); b = Lerp(150, 130, fy); }
                if (fy > near) { r = 70; g = Lerp(105, 80, fy); b = 85; }
                // Lago à esquerda e gramado.
                if (fy > 0.68)
                {
                    var lake = fx < 0.5 && fy < 0.82 + 0.05 * Math.Sin(fx * 9);
                    if (lake) { r = 90; g = 150 + 6 * Math.Sin(y * 1.4); b = 205; }
                    else { var n = Math.Sin(x * 1.7) * Math.Cos(y * 2.3) * 14; r = 40 + n; g = Lerp(175, 135, fy) + n; b = 45; }
                }
                var i = (y * Width + x) * 4;
                px[i] = ToByte(b);
                px[i + 1] = ToByte(g);
                px[i + 2] = ToByte(r);
                px[i + 3] = 255;
            }
        }
        return new BgraImage(Width, Height, px);
    }

    /// <summary>Compõe a imagem com transparência sobre um xadrez, para a área removida ficar visível.</summary>
    private static BgraImage OverChecker(BgraImage image)
    {
        var px = (byte[])image.Pixels.Clone();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                double a = px[i + 3] / 255.0, bg = ((x / 8) + (y / 8)) % 2 == 0 ? 205 : 150;
                for (var c = 0; c < 3; c++) px[i + c] = ToByte(px[i + c] * a + bg * (1 - a));
                px[i + 3] = 255;
            }
        }
        return image with { Pixels = px };
    }

    private static BitmapSource ToBitmap(BgraImage image)
    {
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}

/// <summary>Chave do efeito/predefinição → miniatura (<see cref="EffectPreviews"/>).</summary>
public sealed class EffectPreviewConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => EffectPreviews.Get(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

/// <summary>
/// Faixa em gradiente que mostra o que um parâmetro de cor faz (temperatura azul→laranja, matiz em arco-íris…);
/// null para parâmetros sem representação de cor.
/// </summary>
public sealed class ParameterGradientConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Brush?> Brushes = new(StringComparer.Ordinal);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Brushes.GetOrAdd(key, Create) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;

    private static Brush? Create(string key)
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
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i < stops.Length; i++) brush.GradientStops.Add(new GradientStop(stops[i], i / (double)(stops.Length - 1)));
        brush.Freeze();
        return brush;
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
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [string key, IReadOnlySet<string> applied] && applied.Contains(key);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}
