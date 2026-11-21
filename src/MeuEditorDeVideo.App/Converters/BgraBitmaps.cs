using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.App.Converters;

/// <summary>
/// Imagens BGRA dos ViewModels (miniaturas) como bitmaps do WPF. Cada imagem vira um bitmap congelado uma só vez,
/// mesmo que a lista virtualizada recrie o item ao rolar.
/// </summary>
public sealed class BgraBitmaps : IValueConverter
{
    private static readonly ConditionalWeakTable<BgraImage, BitmapSource> Cache = new();

    public static BgraBitmaps Instance { get; } = new();

    public static BitmapSource ToBitmap(BgraImage image) => Cache.GetValue(image, static i =>
    {
        var bmp = BitmapSource.Create(i.Width, i.Height, 96, 96, PixelFormats.Bgra32, null, i.Pixels, i.Stride);
        bmp.Freeze();
        return bmp;
    });

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is BgraImage image ? ToBitmap(image) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
