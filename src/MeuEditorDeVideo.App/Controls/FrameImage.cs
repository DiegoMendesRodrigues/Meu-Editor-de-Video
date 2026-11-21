using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeuEditorDeVideo.Presentation.Media;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Liga um <see cref="Image"/> aos quadros da prévia (<see cref="DisplayFrame"/>): cada quadro é copiado para um
/// <see cref="WriteableBitmap"/> próprio da imagem, recriado só quando o tamanho muda.
/// </summary>
public static class FrameImage
{
    public static readonly DependencyProperty FrameProperty = DependencyProperty.RegisterAttached(
        "Frame", typeof(DisplayFrame), typeof(FrameImage), new PropertyMetadata(null, OnFrameChanged));

    public static DisplayFrame? GetFrame(DependencyObject d) => (DisplayFrame?)d.GetValue(FrameProperty);

    public static void SetFrame(DependencyObject d, DisplayFrame? value) => d.SetValue(FrameProperty, value);

    private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image target) return;
        if (e.NewValue is not DisplayFrame frame)
        {
            target.Source = null;
            return;
        }
        var img = frame.Image;
        if (target.Source is not WriteableBitmap bitmap || bitmap.PixelWidth != img.Width || bitmap.PixelHeight != img.Height)
        {
            bitmap = new WriteableBitmap(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null);
            target.Source = bitmap;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, img.Width, img.Height), img.Pixels, img.Stride, 0);
    }
}
