using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Export;

public sealed class CompositorAlphaTests
{
    private static readonly CpuCompositor Compositor = new();

    private static readonly MediaAsset Asset = new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Image,
        Name = "imagem.png",
        OriginalPath = @"C:\m\imagem.png",
    };

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

    private static LayerImage Layer(BgraImage image, double opacity = 1) =>
        new(new VisualLayer(Guid.NewGuid(), Guid.NewGuid(), Asset, MediaTime.Zero, Transform.Identity, opacity, []), image, HasAlpha: false);

    private static (int B, int G, int R, int A) PixelAt(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    [Fact]
    public void Transparent_background_without_layers_has_zero_alpha_everywhere()
    {
        var frame = Compositor.Compose(64, 36, 64, [], transparentBackground: true);

        for (var i = 3; i < frame.Pixels.Length; i += 4) Assert.Equal(0, frame.Pixels[i]);
    }

    [Fact]
    public void Opaque_background_without_layers_has_full_alpha_everywhere()
    {
        var frame = Compositor.Compose(64, 36, 64, [], transparentBackground: false);

        for (var i = 3; i < frame.Pixels.Length; i += 4) Assert.Equal(255, frame.Pixels[i]);
    }

    [Fact]
    public void Half_transparent_layer_over_transparent_background_keeps_its_color_and_about_half_alpha()
    {
        var layer = Solid(64, 36, b: 200, g: 100, r: 50);

        var frame = Compositor.Compose(64, 36, 64, [Layer(layer, opacity: 0.5)], transparentBackground: true);

        var (b, g, r, a) = PixelAt(frame, 32, 18);
        Assert.InRange(a, 126, 130);
        Assert.InRange(b, 199, 201);
        Assert.InRange(g, 99, 101);
        Assert.InRange(r, 49, 51);
    }

    [Fact]
    public void Opaque_layer_over_transparent_background_gives_full_alpha()
    {
        var layer = Solid(64, 36, b: 10, g: 20, r: 30);

        var frame = Compositor.Compose(64, 36, 64, [Layer(layer)], transparentBackground: true);

        Assert.Equal((10, 20, 30, 255), PixelAt(frame, 32, 18));
        Assert.Equal((10, 20, 30, 255), PixelAt(frame, 0, 0));
    }

    [Fact]
    public void Half_transparent_layer_over_opaque_black_is_not_darkened_in_alpha_terms()
    {
        var layer = Solid(64, 36, b: 200, g: 100, r: 50);

        var frame = Compositor.Compose(64, 36, 64, [Layer(layer, opacity: 0.5)], transparentBackground: false);

        var (b, g, r, a) = PixelAt(frame, 32, 18);
        Assert.Equal(255, a);
        Assert.InRange(b, 99, 101);
        Assert.InRange(g, 49, 51);
        Assert.InRange(r, 24, 26);
    }
}
