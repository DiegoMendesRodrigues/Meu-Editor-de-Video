// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Desfocar rostos (<see cref="EffectCatalog.FaceBlur"/>): elipse em volta dos pontos do rosto (Face Mesh, coordenadas em
/// pixels da imagem) com desfoque, pixelização ou tarja preta, e borda suave.
/// </summary>
public static class FaceBlur
{
    public static BgraImage Apply(BgraImage image, FaceLandmarks face, EffectInstance e)
    {
        if (face.Count == 0) return image;
        var size = EffectCatalog.Value(e, "size") / 100;
        var amount = EffectCatalog.Value(e, "amount") / 100;
        var mode = (int)Math.Round(EffectCatalog.Value(e, "mode"));

        float minX = face.X.Min(), maxX = face.X.Max(), minY = face.Y.Min(), maxY = face.Y.Max();
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var rx = (maxX - minX) / 2 * size;
        // A malha não cobre a testa inteira: a elipse cresce um pouco mais para cima.
        var ry = (maxY - minY) / 2 * size * 1.1;
        cy -= (float)((maxY - minY) * 0.06);
        if (rx < 2 || ry < 2) return image;

        using var src = CvImage.ToMat(image);
        using var covered = new Mat();
        var faceSize = Math.Max(rx, ry) * 2;
        switch (mode)
        {
            case 1:
                {
                    var block = Math.Max(2, (int)(faceSize / (6 + 30 * (1 - amount))));
                    using var small = new Mat();
                    Cv2.Resize(src, small, new Size(Math.Max(1, image.Width / block), Math.Max(1, image.Height / block)), 0, 0, InterpolationFlags.Area);
                    Cv2.Resize(small, covered, new Size(image.Width, image.Height), 0, 0, InterpolationFlags.Nearest);
                    break;
                }
            case 2:
                src.CopyTo(covered);
                covered.SetTo(new Scalar(0, 0, 0, 255));
                break;
            default:
                {
                    var k = Math.Max(3, (int)(faceSize * (0.05 + 0.25 * amount))) | 1;
                    Cv2.GaussianBlur(src, covered, new Size(k, k), 0);
                    break;
                }
        }

        using var mask = new Mat(image.Height, image.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Ellipse(mask, new RotatedRect(new Point2f(cx, cy), new Size2f(rx * 2, ry * 2), 0), Scalar.All(255), -1, LineTypes.AntiAlias);
        var feather = Math.Max(3, (int)(faceSize * 0.08)) | 1;
        Cv2.GaussianBlur(mask, mask, new Size(feather, feather), 0);

        var m = CvImage.Bytes(mask);
        var a = image.Pixels;
        var b = CvImage.ToImage(covered).Pixels;
        var px = new byte[a.Length];
        Parallel.For(0, image.Height, y =>
        {
            for (var x = 0; x < image.Width; x++)
            {
                var i = y * image.Width + x;
                var w = m[i];
                for (var c = 0; c < 3; c++)
                {
                    var j = i * 4 + c;
                    px[j] = (byte)((b[j] * w + a[j] * (255 - w) + 127) / 255);
                }
                px[i * 4 + 3] = a[i * 4 + 3];
            }
        });
        return new BgraImage(image.Width, image.Height, px);
    }
}

/// <summary>
/// Interpolação de quadros por fluxo óptico (Farneback, OpenCV; não precisa de modelo): estima o movimento de A para B e
/// desloca os dois quadros até o instante intermediário, misturando-os. Usado na câmera lenta por interpolação.
/// </summary>
public static class FrameInterpolator
{
    /// <summary>Lado maior da análise do fluxo (o fluxo é ampliado para o tamanho do quadro).</summary>
    private const int AnalysisSize = 480;

    public static BgraImage Interpolate(BgraImage a, BgraImage b, double weight)
    {
        if (a.Width != b.Width || a.Height != b.Height) return weight < 0.5 ? a : b;
        weight = Math.Clamp(weight, 0, 1);
        var scale = Math.Min(1.0, (double)AnalysisSize / Math.Max(a.Width, a.Height));
        var size = new Size(Math.Max(16, (int)(a.Width * scale)), Math.Max(16, (int)(a.Height * scale)));

        using var ma = CvImage.ToMat(a);
        using var mb = CvImage.ToMat(b);
        using var ga = Gray(ma, size);
        using var gb = Gray(mb, size);
        using var flow = new Mat();
        Cv2.CalcOpticalFlowFarneback(ga, gb, flow, 0.5, 3, 15, 3, 5, 1.2, 0);
        using var full = new Mat();
        Cv2.Resize(flow, full, new Size(a.Width, a.Height));
        var parts = Cv2.Split(full);
        using var fx = parts[0];
        using var fy = parts[1];
        Cv2.Multiply(fx, Scalar.All(1 / scale), fx);
        Cv2.Multiply(fy, Scalar.All(1 / scale), fy);

        // Pixel do quadro intermediário em p vem de A em p − w·f e de B em p + (1 − w)·f (aproximação com o fluxo em p).
        using var gridX = new Mat(a.Height, a.Width, MatType.CV_32FC1);
        using var gridY = new Mat(a.Height, a.Width, MatType.CV_32FC1);
        var gx = new float[a.Width * a.Height];
        var gy = new float[gx.Length];
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                gx[y * a.Width + x] = x;
                gy[y * a.Width + x] = y;
            }
        System.Runtime.InteropServices.Marshal.Copy(gx, 0, gridX.Data, gx.Length);
        System.Runtime.InteropServices.Marshal.Copy(gy, 0, gridY.Data, gy.Length);

        using var mapAx = new Mat();
        using var mapAy = new Mat();
        using var mapBx = new Mat();
        using var mapBy = new Mat();
        Cv2.ScaleAdd(fx, -weight, gridX, mapAx);
        Cv2.ScaleAdd(fy, -weight, gridY, mapAy);
        Cv2.ScaleAdd(fx, 1 - weight, gridX, mapBx);
        Cv2.ScaleAdd(fy, 1 - weight, gridY, mapBy);

        using var wa = new Mat();
        using var wb = new Mat();
        Cv2.Remap(ma, wa, mapAx, mapAy, InterpolationFlags.Linear, BorderTypes.Replicate);
        Cv2.Remap(mb, wb, mapBx, mapBy, InterpolationFlags.Linear, BorderTypes.Replicate);
        using var mixed = new Mat();
        Cv2.AddWeighted(wa, 1 - weight, wb, weight, 0, mixed);
        return CvImage.ToImage(mixed);
    }

    private static Mat Gray(Mat bgra, Size size)
    {
        using var small = new Mat();
        Cv2.Resize(bgra, small, size, 0, 0, InterpolationFlags.Area);
        var gray = new Mat();
        Cv2.CvtColor(small, gray, ColorConversionCodes.BGRA2GRAY);
        return gray;
    }
}
