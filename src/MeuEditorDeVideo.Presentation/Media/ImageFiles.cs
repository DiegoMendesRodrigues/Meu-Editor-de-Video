// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using System.Runtime.InteropServices;
using MeuEditorDeVideo.Core.Media;
using SkiaSharp;

namespace MeuEditorDeVideo.Presentation.Media;

/// <summary>Gravação de imagens BGRA (alfa não pré-multiplicado) em PNG ou JPEG.</summary>
public static class ImageFiles
{
    public static bool IsJpeg(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";

    /// <summary>PNG mantém a transparência; JPEG é achatado sobre preto (fundo do monitor), qualidade 95.</summary>
    public static void Save(BgraImage image, string path)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            using var source = new SKBitmap();
            if (!source.InstallPixels(info, handle.AddrOfPinnedObject(), image.Stride))
                throw new InvalidOperationException("Não foi possível preparar a imagem.");
            using var bitmap = IsJpeg(path) ? Flatten(source) : null;
            using var data = (bitmap ?? source).Encode(IsJpeg(path) ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, IsJpeg(path) ? 95 : 100)
                ?? throw new InvalidOperationException("Não foi possível codificar a imagem.");
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        finally
        {
            handle.Free();
        }
    }

    private static SKBitmap Flatten(SKBitmap source)
    {
        var opaque = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(opaque);
        canvas.Clear(SKColors.Black);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return opaque;
    }
}
