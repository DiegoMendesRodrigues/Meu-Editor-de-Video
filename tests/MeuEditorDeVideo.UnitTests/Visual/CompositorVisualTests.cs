using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class CompositorVisualTests
{
    private static readonly CpuCompositor Compositor = new();

    private static readonly MediaAsset Asset = new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Image,
        Name = "imagem.png",
        OriginalPath = @"C:\m\imagem.png",
    };

    private static BgraImage Solid(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        return new BgraImage(width, height, pixels);
    }

    private static LayerImage Layer(BgraImage image, BlendMode mode = BlendMode.Normal, TransitionMask? mask = null, Transform? transform = null)
    {
        var visual = new VisualLayer(Guid.NewGuid(), Guid.NewGuid(), Asset, MediaTime.Zero, transform ?? Transform.Identity, 1, []);
        visual = visual with { BlendMode = mode, Mask = mask };
        return new LayerImage(visual, image, HasAlpha: false);
    }

    private static (int B, int G, int R, int A) PixelAt(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    // ---------------------------------------------------------------- modos de mistura

    [Fact]
    public void Multiply_de_128_sobre_200_fica_perto_de_100()
    {
        var frame = Compositor.Compose(16, 16, 16, [Layer(Solid(16, 16, 200, 200, 200)), Layer(Solid(16, 16, 128, 128, 128), BlendMode.Multiply)]);

        var (b, g, r, _) = PixelAt(frame, 8, 8);
        Assert.InRange(b, 99, 101);
        Assert.InRange(g, 99, 101);
        Assert.InRange(r, 99, 101);
    }

    [Fact]
    public void Screen_de_128_sobre_200_clareia_para_perto_de_228()
    {
        var frame = Compositor.Compose(16, 16, 16, [Layer(Solid(16, 16, 200, 200, 200)), Layer(Solid(16, 16, 128, 128, 128), BlendMode.Screen)]);

        var (b, _, _, _) = PixelAt(frame, 8, 8);
        Assert.InRange(b, 227, 229);
    }

    [Fact]
    public void Difference_entre_200_e_128_e_72()
    {
        var frame = Compositor.Compose(16, 16, 16, [Layer(Solid(16, 16, 200, 200, 200)), Layer(Solid(16, 16, 128, 128, 128), BlendMode.Difference)]);

        Assert.Equal(72, PixelAt(frame, 8, 8).B);
    }

    [Fact]
    public void Add_satura_em_255()
    {
        var frame = Compositor.Compose(16, 16, 16, [Layer(Solid(16, 16, 200, 200, 200)), Layer(Solid(16, 16, 200, 200, 200), BlendMode.Add)]);

        Assert.Equal(255, PixelAt(frame, 8, 8).B);
    }

    // ---------------------------------------------------------------- recorte

    [Fact]
    public void Recorte_da_esquerda_em_50_por_cento_deixa_a_metade_esquerda_com_o_fundo_preto()
    {
        var transform = new Transform { CropLeft = 0.5 };

        var frame = Compositor.Compose(16, 16, 16, [Layer(Solid(16, 16, 255, 255, 255), transform: transform)]);

        var left = PixelAt(frame, 3, 8);
        Assert.Equal((0, 0, 0), (left.B, left.G, left.R));
        Assert.Equal((255, 255, 255, 255), PixelAt(frame, 12, 8));
    }

    // ---------------------------------------------------------------- máscara de transição

    [Fact]
    public void Mascara_WipeRight_com_progresso_meio_cobre_apenas_a_metade_esquerda()
    {
        var mask = new TransitionMask(TransitionKind.WipeRight, 0.5);

        var frame = Compositor.Compose(32, 16, 32, [Layer(Solid(32, 16, 255, 255, 255), mask: mask)]);

        Assert.Equal(255, PixelAt(frame, 4, 8).R);
        Assert.Equal(0, PixelAt(frame, 28, 8).R);
    }

    [Fact]
    public void Mascara_com_progresso_zero_nao_desenha_a_camada()
    {
        var mask = new TransitionMask(TransitionKind.WipeRight, 0);

        var frame = Compositor.Compose(32, 16, 32, [Layer(Solid(32, 16, 255, 255, 255), mask: mask)]);

        Assert.Equal((0, 0, 0), (PixelAt(frame, 4, 8).B, PixelAt(frame, 4, 8).G, PixelAt(frame, 4, 8).R));
    }

    [Fact]
    public void Circle_revela_o_centro_antes_dos_cantos()
    {
        var aspect = 16.0 / 9.0;

        var centre = TransitionMasks.Coverage(new TransitionMask(TransitionKind.Circle, 0.05), 0.5, 0.5, aspect);
        var corner = TransitionMasks.Coverage(new TransitionMask(TransitionKind.Circle, 0.05), 0, 0, aspect);

        Assert.Equal(1, centre, 9);
        Assert.Equal(0, corner, 9);
    }

    [Fact]
    public void Progresso_completo_da_cobertura_total_e_progresso_zero_esconde()
    {
        Assert.Equal(1, TransitionMasks.Coverage(new TransitionMask(TransitionKind.WipeRight, 1), 0.99, 0.5, 1), 9);
        Assert.True(TransitionMasks.IsHidden(new TransitionMask(TransitionKind.WipeRight, 0)));
        Assert.False(TransitionMasks.IsHidden(new TransitionMask(TransitionKind.WipeRight, 0.1)));
    }
}
