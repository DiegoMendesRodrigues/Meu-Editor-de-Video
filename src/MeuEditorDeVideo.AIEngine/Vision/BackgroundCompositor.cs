using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

public enum BackgroundMode
{
    Transparent = 0,
    Color = 1,
    Blur = 2,
    Media = 3,
}

/// <summary>
/// Remoção e substituição de fundo a partir da máscara da IA (RF102–RF109): ajusta a máscara (limiar, inversão,
/// contração/expansão e suavização das bordas — RF109) e combina o primeiro plano com transparência, cor sólida, fundo
/// desfocado ou outra imagem/vídeo.
/// </summary>
public static class BackgroundCompositor
{
    /// <summary>Máscara final (0…255) no tamanho da imagem, com os ajustes do efeito aplicados.</summary>
    public static byte[] RefineMatte(Matte matte, int width, int height, EffectInstance effect, double pixelScale)
    {
        using var src = CvImage.Gray(matte.Alpha, matte.Width, matte.Height);
        using var m = new Mat();
        Cv2.Resize(src, m, new Size(width, height), 0, 0, InterpolationFlags.Linear);

        var threshold = EffectCatalog.Value(effect, "threshold") / 100;
        if (threshold > 0.001)
        {
            // Reescala: abaixo do limiar vira fundo; acima, estica para 0…255.
            var lo = threshold * 255;
            m.ConvertTo(m, MatType.CV_8UC1, 255 / Math.Max(1, 255 - lo), -lo * 255 / Math.Max(1, 255 - lo));
        }
        if ((int)Math.Round(EffectCatalog.Value(effect, "keep")) == 1) Cv2.BitwiseNot(m, m);

        var choke = EffectCatalog.Value(effect, "choke") * pixelScale;
        var radius = (int)Math.Round(Math.Abs(choke));
        if (radius > 0)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * radius + 1, 2 * radius + 1));
            if (choke > 0) Cv2.Erode(m, m, kernel);
            else Cv2.Dilate(m, m, kernel);
        }
        var feather = EffectCatalog.Value(effect, "feather") * pixelScale;
        if (feather >= 0.5) Cv2.GaussianBlur(m, m, new Size(0, 0), feather / 2);
        return CvImage.Bytes(m);
    }

    /// <summary>
    /// Aplica o efeito <see cref="EffectCatalog.AiBackground"/>. <paramref name="background"/> fornece o quadro de fundo
    /// do modo "imagem ou vídeo" no tamanho pedido (null = transparente).
    /// </summary>
    public static (BgraImage Image, bool AlphaChanged) Apply(BgraImage image, Matte matte, EffectInstance effect,
        Func<int, int, BgraImage?> background, double pixelScale)
    {
        int w = image.Width, h = image.Height;
        var m = RefineMatte(matte, w, h, effect, pixelScale);
        var mode = (BackgroundMode)Math.Clamp((int)Math.Round(EffectCatalog.Value(effect, "mode")), 0, 3);
        var src = image.Pixels;
        var dst = new byte[src.Length];

        byte[]? back = null;
        switch (mode)
        {
            case BackgroundMode.Blur:
            {
                var sigma = Math.Max(0.5, EffectCatalog.Value(effect, "blur") * pixelScale / 2);
                using var mat = CvImage.ToMat(image);
                using var blurred = new Mat();
                // Desfoque em resolução reduzida (mais rápido e com halo menor da pessoa).
                var f = Math.Clamp(sigma / 4, 1, 8);
                using var small = new Mat();
                Cv2.Resize(mat, small, new Size(Math.Max(1, (int)(w / f)), Math.Max(1, (int)(h / f))), 0, 0, InterpolationFlags.Area);
                Cv2.GaussianBlur(small, small, new Size(0, 0), Math.Max(0.5, sigma / f));
                Cv2.Resize(small, blurred, new Size(w, h), 0, 0, InterpolationFlags.Linear);
                back = CvImage.ToImage(blurred).Pixels;
                break;
            }
            case BackgroundMode.Media:
                back = background(w, h) is { } b && b.Width == w && b.Height == h ? b.Pixels : null;
                break;
        }
        var (cr, cg, cb) = EffectCatalog.ColorValue(effect, "color");
        byte r = ToByte(cr), g = ToByte(cg), bl = ToByte(cb);
        var transparent = mode == BackgroundMode.Transparent || (mode == BackgroundMode.Media && back is null);

        Parallel.For(0, h, y =>
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var i = (row + x) * 4;
                var a = m[row + x];
                if (transparent)
                {
                    dst[i] = src[i];
                    dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i + 2];
                    dst[i + 3] = (byte)(src[i + 3] * a / 255);
                    continue;
                }
                int inv = 255 - a;
                int b0, b1, b2;
                if (back is not null)
                {
                    b0 = back[i];
                    b1 = back[i + 1];
                    b2 = back[i + 2];
                }
                else
                {
                    b0 = bl;
                    b1 = g;
                    b2 = r;
                }
                dst[i] = (byte)((src[i] * a + b0 * inv + 127) / 255);
                dst[i + 1] = (byte)((src[i + 1] * a + b1 * inv + 127) / 255);
                dst[i + 2] = (byte)((src[i + 2] * a + b2 * inv + 127) / 255);
                dst[i + 3] = src[i + 3];
            }
        });
        return (new BgraImage(w, h, dst), transparent);
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}
