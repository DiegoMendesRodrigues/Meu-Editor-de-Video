using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.Desktop.Converters;

/// <summary>Imagens BGRA dos ViewModels (miniaturas, efeitos) → bitmaps do Avalonia, com cache por instância.</summary>
public sealed class BgraBitmaps : IValueConverter
{
    public static BgraBitmaps Instance { get; } = new();

    private static readonly ConditionalWeakTable<BgraImage, Bitmap> Cache = [];

    public static Bitmap ToBitmap(BgraImage image) => Cache.GetValue(image, Create);

    private static Bitmap Create(BgraImage image)
    {
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            // O construtor copia os pixels: a imagem BGRA pode ser liberada depois.
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BgraImage image ? ToBitmap(image) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
