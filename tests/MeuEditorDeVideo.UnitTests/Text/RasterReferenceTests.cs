// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Globalization;
using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.ImageEngine.Composition;
using MeuEditorDeVideo.ImageEngine.Text;
using SkiaSharp;
using Xunit.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Text;

/// <summary>
/// Textos e formas desenhados com Skia/HarfBuzz comparados com as imagens geradas pela implementação anterior (WPF):
/// mesmo tamanho de bloco, mesma silhueta (sobreposição das áreas opacas) e mesmas cores. As fontes podem diferir em
/// detalhes de suavização, então a comparação usa tolerâncias, não igualdade de pixels.
/// </summary>
public sealed class RasterReferenceTests(ITestOutputHelper output)
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Text", "Referencias");

    public static TheoryData<string> TextNames() => [.. RasterReferenceCases.Texts.Select(c => c.Name)];

    public static TheoryData<string> ShapeNames() => [.. RasterReferenceCases.Shapes.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(TextNames))]
    public void Texto_mantem_a_aparencia_da_versao_anterior(string name)
    {
        var c = RasterReferenceCases.Texts.Single(t => t.Name == name);
        // As referências usam fontes do Windows; em outro sistema a fonte substituta muda as medidas.
        if (!FontCatalog.Families().Contains(c.Style.FontFamily, StringComparer.OrdinalIgnoreCase)) return;

        var r = TextRasterizer.Render(c.Text, Math.Min(c.Visible, c.Text.Length), c.Style, c.Scale, c.FrameWidth);
        var box = File.ReadAllText(Path.Combine(Folder, $"texto_{name}.txt")).Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var reference = Load($"texto_{name}.png");
        var m = Compare(reference, r.Image);
        output.WriteLine(FormattableString.Invariant(
            $"{name}: caixa {r.BoxWidth}x{r.BoxHeight} (ref {box[0]}x{box[1]}), imagem {r.Image.Width}x{r.Image.Height} (ref {reference.Width}x{reference.Height}), IoU {m.IoU:F3}, cor {m.ColorDelta:F1}"));

        Assert.InRange(r.BoxWidth, box[0] * 0.94 - 2, box[0] * 1.06 + 2);
        Assert.InRange(r.BoxHeight, box[1] * 0.94 - 2, box[1] * 1.06 + 2);
        Assert.True(m.IoU >= 0.8, $"Silhueta diferente da referência (IoU {m.IoU:F3}).");
        Assert.True(m.ColorDelta <= 12, $"Cores diferentes da referência ({m.ColorDelta:F1}).");
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void Forma_mantem_a_aparencia_da_versao_anterior(string name)
    {
        var c = RasterReferenceCases.Shapes.Single(t => t.Name == name);
        var img = ShapeRasterizer.Render(c.Style, c.Scale);
        var reference = Load($"forma_{name}.png");
        var m = Compare(reference, img);
        output.WriteLine(FormattableString.Invariant($"{name}: {img.Width}x{img.Height} (ref {reference.Width}x{reference.Height}), IoU {m.IoU:F3}, cor {m.ColorDelta:F1}"));

        Assert.Equal((reference.Width, reference.Height), (img.Width, img.Height));
        Assert.True(m.IoU >= 0.97, $"Silhueta diferente da referência (IoU {m.IoU:F3}).");
        Assert.True(m.ColorDelta <= 6, $"Cores diferentes da referência ({m.ColorDelta:F1}).");
    }

    private static BgraImage Load(string file)
    {
        using var bmp = SKBitmap.Decode(Path.Combine(Folder, file));
        using var bgra = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        Assert.True(bmp.CopyTo(bgra, SKColorType.Bgra8888));
        return new BgraImage(bgra.Width, bgra.Height, bgra.Bytes);
    }

    /// <summary>
    /// Sobreposição das áreas opacas (alfa ≥ 128) com as imagens centradas, e diferença média de cor nos pixels opacos
    /// das duas. Vale o melhor alinhamento com até 1 pixel de deslocamento (arredondamento da linha de base).
    /// </summary>
    private static (double IoU, double ColorDelta) Compare(BgraImage a, BgraImage b)
    {
        (double IoU, double ColorDelta) best = (-1, 0);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var m = Compare(a, b, dx, dy);
                if (m.IoU > best.IoU) best = m;
            }
        }
        return best;
    }

    private static (double IoU, double ColorDelta) Compare(BgraImage a, BgraImage b, int dx, int dy)
    {
        int both = 0, either = 0, colorCount = 0;
        double colorSum = 0;
        var w = Math.Max(a.Width, b.Width);
        var h = Math.Max(a.Height, b.Height);
        int ax = (w - a.Width) / 2, ay = (h - a.Height) / 2, bx = (w - b.Width) / 2 + dx, by = (h - b.Height) / 2 + dy;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var pa = Pixel(a, x - ax, y - ay);
                var pb = Pixel(b, x - bx, y - by);
                bool oa = pa.A >= 128, ob = pb.A >= 128;
                if (oa || ob) either++;
                if (!(oa && ob)) continue;
                both++;
                if (pa.A < 250 || pb.A < 250) continue;
                colorSum += (Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B)) / 3.0;
                colorCount++;
            }
        }
        return (either == 0 ? 1 : (double)both / either, colorCount == 0 ? 0 : colorSum / colorCount);
    }

    private static (int B, int G, int R, int A) Pixel(BgraImage img, int x, int y)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return (0, 0, 0, 0);
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }
}
