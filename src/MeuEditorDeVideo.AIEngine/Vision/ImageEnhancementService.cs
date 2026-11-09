using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Super-resolução 2× com Swin2SR (entrada RGB [1,3,H,W] em 0…1; saída [1,3,2H',2W'] com H' e W' completados para
/// múltiplos de 8). Processa em blocos com sobreposição para limitar a memória.
/// </summary>
public sealed class Swin2Sr(InferenceSession session)
{
    private const int Tile = 192, Overlap = 16, Window = 8;
    private readonly string _input = session.InputMetadata.Keys.First();
    private readonly string _output = session.OutputMetadata.Keys.First();

    public int Scale { get; } = 2;

    /// <summary>Amplia uma imagem BGR 8 bits (3 canais).</summary>
    public Mat Upscale(Mat bgr, IProgress<double>? progress, CancellationToken ct)
    {
        int w = bgr.Width, h = bgr.Height, s = Scale;
        using var acc = new Mat(h * s, w * s, MatType.CV_32FC3, Scalar.All(0));
        using var weight = new Mat(h * s, w * s, MatType.CV_32FC1, Scalar.All(0));
        var step = Tile - 2 * Overlap;
        var xs = Starts(w, step);
        var ys = Starts(h, step);
        var done = 0;
        foreach (var y0 in ys)
        foreach (var x0 in xs)
        {
            ct.ThrowIfCancellationRequested();
            var rect = new Rect(x0, y0, Math.Min(Tile, w - x0), Math.Min(Tile, h - y0));
            using var tile = new Mat(bgr, rect);
            using var up = Run(tile);
            var target = new Rect(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s);
            using var mask = Feather(rect.Width * s, rect.Height * s, Overlap * s, x0 > 0, y0 > 0, x0 + rect.Width < w, y0 + rect.Height < h);
            using var mask3 = new Mat();
            Cv2.Merge([mask, mask, mask], mask3);
            using var weighted = up.Mul(mask3);
            using (var accRoi = new Mat(acc, target)) Cv2.Add(accRoi, weighted, accRoi);
            using (var wRoi = new Mat(weight, target)) Cv2.Add(wRoi, mask, wRoi);
            progress?.Report(++done / (double)(xs.Count * ys.Count));
        }
        using var w3 = new Mat();
        Cv2.Merge([weight, weight, weight], w3);
        using var normalized = new Mat();
        Cv2.Divide(acc, w3, normalized);
        var result = new Mat();
        normalized.ConvertTo(result, MatType.CV_8UC3, 255);
        return result;
    }

    private static List<int> Starts(int size, int step)
    {
        var list = new List<int>();
        if (size <= Tile) return [0];
        for (var p = 0; ; p += step)
        {
            if (p + Tile >= size)
            {
                list.Add(size - Tile);
                break;
            }
            list.Add(p);
        }
        return list;
    }

    /// <summary>Peso com rampa linear nas bordas internas (mistura suave entre blocos).</summary>
    private static Mat Feather(int w, int h, int ramp, bool left, bool top, bool right, bool bottom)
    {
        var data = new float[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            float v = 1;
            if (left) v = Math.Min(v, (x + 1f) / ramp);
            if (top) v = Math.Min(v, (y + 1f) / ramp);
            if (right) v = Math.Min(v, (w - x) / (float)ramp);
            if (bottom) v = Math.Min(v, (h - y) / (float)ramp);
            data[y * w + x] = Math.Clamp(v, 1e-3f, 1);
        }
        var m = new Mat(h, w, MatType.CV_32FC1);
        System.Runtime.InteropServices.Marshal.Copy(data, 0, m.Data, data.Length);
        return m;
    }

    /// <summary>Executa a rede num bloco BGR 8 bits; devolve BGR float 0…1 com o dobro do tamanho.</summary>
    private Mat Run(Mat tile)
    {
        int w = tile.Width, h = tile.Height;
        var pw = (w + Window - 1) / Window * Window;
        var ph = (h + Window - 1) / Window * Window;
        using var padded = new Mat();
        Cv2.CopyMakeBorder(tile, padded, 0, ph - h, 0, pw - w, BorderTypes.Reflect101);
        using var cont = padded.Clone();
        var plane = pw * ph;
        var data = new float[3 * plane];
        var bytes = new byte[plane * 3];
        System.Runtime.InteropServices.Marshal.Copy(cont.Data, bytes, 0, bytes.Length);
        for (var i = 0; i < plane; i++)
        {
            data[i] = bytes[i * 3 + 2] / 255f;
            data[plane + i] = bytes[i * 3 + 1] / 255f;
            data[2 * plane + i] = bytes[i * 3] / 255f;
        }
        using var input = OrtValue.CreateTensorValueFromMemory(data, [1, 3, ph, pw]);
        using var outputs = session.Run(new RunOptions(), [_input], [input], [_output]);
        var shape = outputs[0].GetTensorTypeAndShape().Shape;
        int oh = (int)shape[2], ow = (int)shape[3];
        var o = outputs[0].GetTensorDataAsSpan<float>();
        var oplane = ow * oh;
        var rgb = new float[oplane * 3];
        for (var i = 0; i < oplane; i++)
        {
            rgb[i * 3] = Math.Clamp(o[2 * oplane + i], 0, 1);
            rgb[i * 3 + 1] = Math.Clamp(o[oplane + i], 0, 1);
            rgb[i * 3 + 2] = Math.Clamp(o[i], 0, 1);
        }
        using var full = new Mat(oh, ow, MatType.CV_32FC3);
        System.Runtime.InteropServices.Marshal.Copy(rgb, 0, full.Data, rgb.Length);
        return new Mat(full, new Rect(0, 0, w * Scale, h * Scale)).Clone();
    }
}

/// <summary>Melhoria de imagem por IA (RF164): amplia fotos 2× com reconstrução de detalhes; o alfa é ampliado por interpolação.</summary>
public sealed class ImageEnhancementService(IModelRegistry registry, OnnxSessionFactory sessions) : IImageEnhancementService
{
    public bool IsAvailable => registry.IsAvailable(ModelCapability.ImageEnhancement);

    public Task EnhanceImageAsync(string inputPath, string outputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var model = registry.Find(ModelCapability.ImageEnhancement)
                ?? throw new InvalidOperationException("O modelo de melhoria de imagem não está instalado.");
            using var session = sessions.Create(model);
            var sr = new Swin2Sr(session.Session);
            using var image = CvImage.Read(inputPath);
            using var bgr = new Mat();
            Mat? alpha = null;
            try
            {
                switch (image.Channels())
                {
                    case 4:
                        Cv2.CvtColor(image, bgr, ColorConversionCodes.BGRA2BGR);
                        alpha = image.ExtractChannel(3);
                        break;
                    case 1:
                        Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
                        break;
                    default:
                        image.ConvertTo(bgr, MatType.CV_8UC3, image.Depth() == MatType.CV_16U ? 1 / 257.0 : 1);
                        break;
                }
                using var up = sr.Upscale(bgr, progress, cancellationToken);
                if (alpha is not null)
                {
                    using var bigAlpha = new Mat();
                    Cv2.Resize(alpha, bigAlpha, up.Size(), 0, 0, InterpolationFlags.Cubic);
                    using var bgra = new Mat();
                    Cv2.Merge([.. up.Split(), bigAlpha], bgra);
                    CvImage.WritePng(outputPath, bgra);
                }
                else
                {
                    CvImage.WritePng(outputPath, up);
                }
            }
            finally
            {
                alpha?.Dispose();
            }
            progress?.Report(1);
        }, cancellationToken);
}
