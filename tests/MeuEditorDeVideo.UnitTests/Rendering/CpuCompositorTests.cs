using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Rendering;

public sealed class CpuCompositorTests
{
    private static readonly CpuCompositor Compositor = new();

    private static BgraImage Solid(int width, int height, byte b, byte g, byte r, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
        return new BgraImage(width, height, pixels);
    }

    private static BgraImage Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = (byte)x;
            pixels[i + 1] = (byte)y;
            pixels[i + 2] = (byte)(x + y);
            pixels[i + 3] = 255;
        }
        return new BgraImage(width, height, pixels);
    }

    private static (int B, int G, int R, int A) PixelAt(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static readonly MediaAsset Asset = new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Image,
        Name = "imagem.png",
        OriginalPath = @"C:\m\imagem.png",
    };

    private static LayerImage Layer(BgraImage image, Transform? transform = null, double opacity = 1, bool hasAlpha = false) =>
        new(new VisualLayer(Guid.NewGuid(), Guid.NewGuid(), Asset, MediaTime.Zero, transform ?? Transform.Identity, opacity, []),
            image, hasAlpha);

    [Fact]
    public void No_layers_produces_opaque_black_frame()
    {
        var frame = Compositor.Compose(64, 36, 64, []);

        Assert.Equal(64, frame.Width);
        Assert.Equal(36, frame.Height);
        Assert.Equal(64 * 36 * 4, frame.Pixels.Length);
        for (var i = 0; i < frame.Pixels.Length; i += 4)
        {
            Assert.Equal(0, frame.Pixels[i]);
            Assert.Equal(0, frame.Pixels[i + 1]);
            Assert.Equal(0, frame.Pixels[i + 2]);
            Assert.Equal(255, frame.Pixels[i + 3]);
        }
    }

    [Fact]
    public void Single_opaque_frame_sized_layer_is_copied_exactly()
    {
        var image = Gradient(64, 36);

        var frame = Compositor.Compose(64, 36, 64, [Layer(image)]);

        Assert.Equal(image.Pixels, frame.Pixels);
    }

    [Fact]
    public void Smaller_image_is_scaled_to_fit_and_letterboxed()
    {
        // 50x50 em um quadro 200x100: ajuste "contain" ⇒ escala 2 ⇒ 100x100 centralizado (colunas 50..149).
        var red = Solid(50, 50, b: 0, g: 0, r: 255);

        var frame = Compositor.Compose(200, 100, 200, [Layer(red)]);

        Assert.Equal((0, 0, 255, 255), PixelAt(frame, 100, 50));
        Assert.Equal((0, 0, 0, 255), PixelAt(frame, 10, 10));
        Assert.Equal((0, 0, 0, 255), PixelAt(frame, 190, 90));
    }

    [Fact]
    public void Half_opacity_over_black_gives_about_half_intensity()
    {
        var white = Solid(64, 36, 255, 255, 255);

        var frame = Compositor.Compose(64, 36, 64, [Layer(white, opacity: 0.5)]);

        var (b, g, r, a) = PixelAt(frame, 32, 18);
        Assert.InRange(b, 126, 130);
        Assert.InRange(g, 126, 130);
        Assert.InRange(r, 126, 130);
        Assert.Equal(255, a);
    }

    [Fact]
    public void X_offset_is_in_project_pixels_and_scaled_to_output()
    {
        // Projeto 1920 px de largura renderizado em 960 px: X = 192 px do projeto = 96 px de saída.
        var full = Solid(960, 540, 255, 255, 255);
        var transform = new Transform { X = 192 };

        var frame = Compositor.Compose(960, 540, 1920, [Layer(full, transform)]);

        Assert.Equal((0, 0, 0, 255), PixelAt(frame, 95, 270));
        Assert.Equal((255, 255, 255, 255), PixelAt(frame, 96, 270));
        Assert.Equal((255, 255, 255, 255), PixelAt(frame, 959, 270));
    }

    [Fact]
    public void Fully_transparent_pixels_leave_background_untouched()
    {
        var green = Solid(64, 36, 0, 255, 0);
        var transparentRed = Solid(64, 36, 0, 0, 255, a: 0);

        var frame = Compositor.Compose(64, 36, 64, [Layer(green), Layer(transparentRed, hasAlpha: true)]);

        Assert.Equal((0, 255, 0, 255), PixelAt(frame, 10, 10));
        Assert.Equal((0, 255, 0, 255), PixelAt(frame, 63, 35));
    }

    [Fact]
    public void Upper_layer_wins_over_lower_layer()
    {
        var red = Solid(64, 36, 0, 0, 255);
        var blue = Solid(64, 36, 255, 0, 0);

        var frame = Compositor.Compose(64, 36, 64, [Layer(red), Layer(blue)]);

        Assert.Equal((255, 0, 0, 255), PixelAt(frame, 20, 20));
    }
}
