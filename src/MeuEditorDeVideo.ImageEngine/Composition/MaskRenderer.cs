using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using OpenCvSharp;

namespace MeuEditorDeVideo.ImageEngine.Composition;

/// <summary>
/// Rasteriza as máscaras de uma camada (RF110, RF149, RF111) e multiplica o alfa da imagem pela cobertura combinada.
/// Contornos com antisserrilhado e precisão subpixel, contração/expansão por morfologia e suavização gaussiana (OpenCV).
/// </summary>
public static class MaskRenderer
{
    private const int Shift = 4; // precisão de 1/16 de pixel no preenchimento

    /// <summary>Cobertura combinada (0…255) das máscaras, no tamanho da imagem. Nulo quando não há máscara ativa.</summary>
    public static byte[]? Coverage(int width, int height, IReadOnlyList<Mask> masks)
    {
        var active = masks.Where(m => m.Enabled).ToList();
        if (active.Count == 0 || width <= 0 || height <= 0) return null;
        var n = width * height;
        float[]? acc = null;
        foreach (var mask in active)
        {
            var m = Rasterize(mask, width, height);
            var opacity = (float)Math.Clamp(mask.Opacity, 0, 1);
            if (acc is null)
            {
                acc = new float[n];
                // Primeira máscara: "subtrair" parte da imagem inteira; as demais partem do vazio.
                for (var i = 0; i < n; i++)
                {
                    var v = m[i] / 255f * opacity;
                    acc[i] = mask.Mode == MaskMode.Subtract ? 1 - v : v;
                }
                continue;
            }
            for (var i = 0; i < n; i++)
            {
                var v = m[i] / 255f * opacity;
                acc[i] = mask.Mode switch
                {
                    MaskMode.Subtract => acc[i] * (1 - v),
                    MaskMode.Intersect => Math.Min(acc[i], v),
                    _ => Math.Max(acc[i], v),
                };
            }
        }
        var result = new byte[n];
        for (var i = 0; i < n; i++) result[i] = (byte)Math.Clamp((int)Math.Round(acc![i] * 255), 0, 255);
        return result;
    }

    /// <summary>Aplica as máscaras ao alfa da imagem (nova imagem; a entrada não é alterada).</summary>
    public static BgraImage Apply(BgraImage image, IReadOnlyList<Mask> masks)
    {
        if (Coverage(image.Width, image.Height, masks) is not { } cov) return image;
        var px = (byte[])image.Pixels.Clone();
        Parallel.For(0, image.Height, y =>
        {
            var row = y * image.Width;
            for (var x = 0; x < image.Width; x++)
            {
                var i = row + x;
                px[i * 4 + 3] = (byte)(px[i * 4 + 3] * cov[i] / 255);
            }
        });
        return new BgraImage(image.Width, image.Height, px);
    }

    /// <summary>Uma máscara isolada (contorno preenchido, contração/expansão, suavização e inversão).</summary>
    public static byte[] Rasterize(Mask mask, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        var outline = CompositionMath.Outline(mask);
        if (outline.Count >= 3)
        {
            var scale = 1 << Shift;
            var pts = outline.Select(p => new Point(
                (int)Math.Round(p.X * width * scale - scale / 2.0),
                (int)Math.Round(p.Y * height * scale - scale / 2.0))).ToArray();
            Cv2.FillPoly(mat, [pts], Scalar.All(255), LineTypes.AntiAlias, Shift);
        }

        var r = (int)Math.Round(Math.Abs(mask.Expansion) * width);
        if (r > 0)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * r + 1, 2 * r + 1));
            if (mask.Expansion > 0) Cv2.Erode(mat, mat, kernel);
            else Cv2.Dilate(mat, mat, kernel);
        }

        var feather = Math.Clamp(mask.Feather, 0, 0.25) * width;
        if (feather >= 0.5) Cv2.GaussianBlur(mat, mat, new Size(0, 0), feather / 2, feather / 2, BorderTypes.Replicate);

        if (mask.Invert) Cv2.BitwiseNot(mat, mat);
        mat.GetArray(out byte[] data);
        return data;
    }
}
