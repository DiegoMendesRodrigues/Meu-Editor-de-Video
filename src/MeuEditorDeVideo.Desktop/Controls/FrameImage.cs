using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MeuEditorDeVideo.Presentation.Media;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Mostra os quadros da prévia (<see cref="DisplayFrame"/>): cada quadro é copiado para um <see cref="WriteableBitmap"/>
/// próprio do controle, recriado só quando o tamanho muda, e desenhado centralizado com ajuste uniforme (como o
/// <c>Image Stretch=Uniform</c> do WPF). O motor reaproveita o buffer de pixels, por isso a cópia acontece no momento
/// em que <see cref="Frame"/> muda, na thread da interface.
/// </summary>
public sealed class FrameImage : Control
{
    public static readonly StyledProperty<DisplayFrame?> FrameProperty = AvaloniaProperty.Register<FrameImage, DisplayFrame?>(nameof(Frame));

    private WriteableBitmap? _bitmap;

    static FrameImage()
    {
        AffectsRender<FrameImage>(FrameProperty);
        FrameProperty.Changed.AddClassHandler<FrameImage>((c, _) => c.CopyFrame());
    }

    public DisplayFrame? Frame
    {
        get => GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    /// <summary>Superfície atual (null sem quadro); o mesmo objeto enquanto o tamanho dos quadros não muda.</summary>
    public WriteableBitmap? Bitmap => _bitmap;

    private void CopyFrame()
    {
        if (Frame is not { } frame)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            InvalidateMeasure();
            return;
        }
        var image = frame.Image;
        if (image.Width <= 0 || image.Height <= 0) return;
        if (_bitmap is null || _bitmap.PixelSize.Width != image.Width || _bitmap.PixelSize.Height != image.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            InvalidateMeasure();
        }
        using var fb = _bitmap.Lock();
        var rowBytes = image.Width * 4;
        if (fb.RowBytes == image.Stride)
        {
            Marshal.Copy(image.Pixels, 0, fb.Address, Math.Min(image.Pixels.Length, rowBytes * image.Height));
        }
        else
        {
            for (var y = 0; y < image.Height; y++)
                Marshal.Copy(image.Pixels, y * image.Stride, fb.Address + y * fb.RowBytes, rowBytes);
        }
    }

    /// <summary>
    /// Tamanho do <c>Image Stretch=Uniform</c> do WPF: o quadro ajustado ao espaço disponível, sem sobras. Assim os
    /// controles empilhados sobre a imagem (alças, máscaras, régua) ocupam exatamente a área da imagem. Sem quadro, 0.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_bitmap is null) return default;
        var size = _bitmap.Size;
        var scale = Math.Min(availableSize.Width / size.Width, availableSize.Height / size.Height);
        if (!double.IsFinite(scale)) scale = double.IsFinite(availableSize.Width) ? availableSize.Width / size.Width
            : double.IsFinite(availableSize.Height) ? availableSize.Height / size.Height : 1;
        return new Size(size.Width * scale, size.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null) return;
        var size = Bounds.Size;
        var scale = Math.Min(size.Width / _bitmap.Size.Width, size.Height / _bitmap.Size.Height);
        if (!double.IsFinite(scale) || scale <= 0) return;
        var w = _bitmap.Size.Width * scale;
        var h = _bitmap.Size.Height * scale;
        var dest = new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
        context.DrawImage(_bitmap, new Rect(_bitmap.Size), dest);
    }
}
