// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Rastreamento de câmera (<see cref="EffectCatalog.CameraTrack"/>): a máscara da pessoa (modelo de segmentação) dá a
/// posição da cabeça ou do corpo, e uma câmera virtual acompanha esse ponto. Recursos: fundo desfocado (só onde a
/// máscara indica fundo), corte automático (zoom centrado no alvo) e agitar (tremor de câmera em torno do alvo).
/// Sem pessoa detectada (ou sem modelo), a câmera fica no centro do quadro.
/// </summary>
public static class CameraTracker
{
    /// <summary>Ponto acompanhado, em frações do quadro (0…1); null se a máscara não tem pessoa.</summary>
    public static (double X, double Y)? Target(Matte matte, bool head)
    {
        int w = matte.Width, h = matte.Height;
        int top = -1, bottom = -1;
        var rowMass = new long[h];
        var rowX = new double[h];
        for (var y = 0; y < h; y++)
        {
            long mass = 0;
            double sx = 0;
            for (var x = 0; x < w; x++)
            {
                var a = matte.Alpha[y * w + x];
                if (a < 128) continue;
                mass += a;
                sx += (double)x * a;
            }
            rowMass[y] = mass;
            rowX[y] = sx;
            if (mass <= 0) continue;
            if (top < 0) top = y;
            bottom = y;
        }
        if (top < 0) return null;

        // Cabeça: faixa de cima da silhueta (≈ 22% da altura); corpo: a silhueta inteira.
        var last = head ? Math.Min(bottom, top + Math.Max(1, (int)Math.Round((bottom - top + 1) * 0.22))) : bottom;
        long total = 0;
        double cx = 0, cy = 0;
        for (var y = top; y <= last; y++)
        {
            total += rowMass[y];
            cx += rowX[y];
            cy += (double)y * rowMass[y];
        }
        return total <= 0 ? null : (cx / total / w, cy / total / h);
    }

    public static BgraImage Apply(BgraImage image, Matte? matte, EffectInstance effect, double seconds, double pixelScale)
    {
        var head = (int)Math.Round(EffectCatalog.Value(effect, "target")) == 0;
        var follow = EffectCatalog.Value(effect, "follow") / 100;
        var zoom = 1 + 1.5 * EffectCatalog.Value(effect, "autocrop") / 100;
        var shake = EffectCatalog.Value(effect, "shake") / 100;
        var blur = EffectCatalog.Value(effect, "bgblur") / 100;

        using var mat = CvImage.ToMat(image);
        var result = mat;

        // 1. Fundo desfocado: mistura o quadro desfocado onde a máscara indica fundo.
        Mat? blurred = null;
        if (blur > 0 && matte is not null)
        {
            blurred = new Mat();
            var sigma = Math.Max(0.5, blur * 30 * pixelScale);
            Cv2.GaussianBlur(mat, blurred, new Size(0, 0), sigma);
            using var small = CvImage.Gray(matte.Alpha, matte.Width, matte.Height);
            using var alpha = new Mat();
            Cv2.Resize(small, alpha, new Size(image.Width, image.Height), 0, 0, InterpolationFlags.Linear);
            Cv2.GaussianBlur(alpha, alpha, new Size(0, 0), Math.Max(1, 3 * pixelScale));
            var px = new byte[image.Pixels.Length];
            var a = CvImage.Bytes(alpha);
            var sharp = image.Pixels;
            var soft = CvImage.ToImage(blurred).Pixels;
            Parallel.For(0, image.Height, y =>
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var k = y * image.Width + x;
                    var f = a[k] / 255f;
                    var i = k * 4;
                    for (var c = 0; c < 3; c++) px[i + c] = (byte)(sharp[i + c] * f + soft[i + c] * (1 - f) + 0.5f);
                    px[i + 3] = sharp[i + 3];
                }
            });
            blurred.Dispose();
            blurred = CvImage.ToMat(new BgraImage(image.Width, image.Height, px));
            result = blurred;
        }

        // 2. Câmera virtual: zoom centrado no alvo (interpolado com o centro por "seguir") e tremor.
        var amp = shake * 0.035;
        var scale = Math.Max(zoom, 1 + 2 * amp + (shake > 0 ? 0.02 : 0));
        if (scale > 1.0001)
        {
            var target = matte is null ? null : Target(matte, head);
            var (tx, ty) = target ?? (0.5, 0.5);
            tx = 0.5 + (tx - 0.5) * follow;
            ty = 0.5 + (ty - 0.5) * follow;
            if (shake > 0)
            {
                tx += amp * (Math.Sin(seconds * 13.7) * 0.6 + Math.Sin(seconds * 31.1 + 1.3) * 0.4);
                ty += amp * (Math.Sin(seconds * 17.3 + 0.7) * 0.6 + Math.Sin(seconds * 27.9 + 2.1) * 0.4);
            }
            double cw = image.Width / scale, ch = image.Height / scale;
            var x0 = Math.Clamp(tx * image.Width - cw / 2, 0, image.Width - cw);
            var y0 = Math.Clamp(ty * image.Height - ch / 2, 0, image.Height - ch);
            // Transformação afim: recorte (x0, y0, cw, ch) → quadro inteiro, com precisão sub-pixel.
            using var m = new Mat(2, 3, MatType.CV_64FC1);
            m.Set(0, 0, scale); m.Set(0, 1, 0.0); m.Set(0, 2, -x0 * scale);
            m.Set(1, 0, 0.0); m.Set(1, 1, scale); m.Set(1, 2, -y0 * scale);
            using var warped = new Mat();
            Cv2.WarpAffine(result, warped, m, new Size(image.Width, image.Height), InterpolationFlags.Linear, BorderTypes.Replicate);
            blurred?.Dispose();
            return CvImage.ToImage(warped);
        }

        var output = CvImage.ToImage(result);
        blurred?.Dispose();
        return output;
    }
}
