using MeuEditorDeVideo.Core.Media;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// TransNetV2 (detecção de transições de cena): janelas de 100 quadros RGB 48×27 (valores 0…255), das quais se usam os
/// 50 quadros centrais; a primeira saída é a probabilidade (logit) de transição em cada quadro.
/// </summary>
public sealed class TransNetV2(InferenceSession session)
{
    public const int FrameWidth = 48, FrameHeight = 27;
    private const int Window = 100, Step = 50, Pad = 25;
    private const int FrameSize = FrameWidth * FrameHeight * 3;

    private readonly string _input = session.InputMetadata.Keys.First();
    private readonly string _output = session.OutputMetadata.Keys.First();

    /// <summary>Quadro reduzido para a rede (RGB intercalado, 48×27).</summary>
    public static byte[] Thumbnail(BgraImage frame)
    {
        using var src = CvImage.ToMat(frame);
        using var small = new Mat();
        Cv2.Resize(src, small, new Size(FrameWidth, FrameHeight), 0, 0, InterpolationFlags.Area);
        var bgra = CvImage.ToImage(small).Pixels;
        var rgb = new byte[FrameSize];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }
        return rgb;
    }

    /// <summary>Probabilidade de transição (0…1) para cada quadro.</summary>
    public float[] Predict(IReadOnlyList<byte[]> frames, IProgress<double>? progress, CancellationToken ct)
    {
        var n = frames.Count;
        var result = new float[n];
        if (n == 0) return result;
        // Preenchimento: 25 cópias do primeiro quadro no início; no fim, o último quadro até completar janelas de 50.
        var tail = Pad + Step - (n % Step == 0 ? Step : n % Step);
        byte[] At(int padded) => frames[Math.Clamp(padded - Pad, 0, n - 1)];
        var total = Pad + n + tail;
        var data = new float[Window * FrameSize];
        var windows = Math.Max(1, (total - Window) / Step + 1);
        for (var w = 0; w < windows; w++)
        {
            ct.ThrowIfCancellationRequested();
            var start = w * Step;
            for (var f = 0; f < Window; f++)
            {
                var frame = At(start + f);
                var offset = f * FrameSize;
                for (var k = 0; k < FrameSize; k++) data[offset + k] = frame[k];
            }
            using var input = OrtValue.CreateTensorValueFromMemory(data, [1, Window, FrameHeight, FrameWidth, 3]);
            using var outputs = session.Run(new RunOptions(), [_input], [input], [_output]);
            var p = outputs[0].GetTensorDataAsSpan<float>();
            for (var f = Pad; f < Pad + Step; f++)
            {
                var index = start + f - Pad;
                if (index >= n) break;
                var v = p[f];
                result[index] = v is >= 0 and <= 1 ? v : 1f / (1f + MathF.Exp(-v));
            }
            progress?.Report((w + 1.0) / windows);
        }
        return result;
    }

    /// <summary>Quadros de corte: picos acima do limiar, com distância mínima entre cortes.</summary>
    public static IReadOnlyList<int> Cuts(IReadOnlyList<float> predictions, double threshold, int minGap)
    {
        var cuts = new List<int>();
        var i = 0;
        while (i < predictions.Count)
        {
            if (predictions[i] < threshold)
            {
                i++;
                continue;
            }
            // Região acima do limiar → o pico é a transição.
            var best = i;
            while (i < predictions.Count && predictions[i] >= threshold)
            {
                if (predictions[i] > predictions[best]) best = i;
                i++;
            }
            if (best > 0 && (cuts.Count == 0 || best - cuts[^1] >= minGap)) cuts.Add(best);
        }
        return cuts;
    }
}

/// <summary>
/// Detecção de cenas sem modelo (sempre disponível): diferença de histogramas HSV entre quadros consecutivos, com limiar
/// adaptativo à média local (evita cortes falsos em cenas de muito movimento).
/// </summary>
public static class HistogramSceneDetector
{
    public const int Width = 64, Height = 36;

    /// <summary>Histograma HSV normalizado (16×4×4) de um quadro.</summary>
    public static float[] Histogram(BgraImage frame)
    {
        using var src = CvImage.ToMat(frame);
        using var small = new Mat();
        Cv2.Resize(src, small, new Size(Width, Height), 0, 0, InterpolationFlags.Area);
        using var bgr = new Mat();
        Cv2.CvtColor(small, bgr, ColorConversionCodes.BGRA2BGR);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var hist = new float[16 * 4 * 4];
        var data = new byte[Width * Height * 3];
        System.Runtime.InteropServices.Marshal.Copy(hsv.Data, data, 0, data.Length);
        for (var i = 0; i < data.Length; i += 3)
        {
            var h = Math.Min(15, data[i] * 16 / 180);
            var s = data[i + 1] / 64;
            var v = data[i + 2] / 64;
            hist[h * 16 + s * 4 + v] += 1;
        }
        var total = (float)(Width * Height);
        for (var i = 0; i < hist.Length; i++) hist[i] /= total;
        return hist;
    }

    /// <summary>Distância entre histogramas: 0 (iguais) … 1 (sem sobreposição).</summary>
    public static double Distance(float[] a, float[] b)
    {
        double overlap = 0;
        for (var i = 0; i < a.Length; i++) overlap += Math.Min(a[i], b[i]);
        return 1 - overlap;
    }

    /// <summary>Índices dos quadros que iniciam uma nova cena.</summary>
    public static IReadOnlyList<int> Detect(IReadOnlyList<float[]> histograms, double sensitivity, int minGap)
    {
        var n = histograms.Count;
        var d = new double[n];
        for (var i = 1; i < n; i++) d[i] = Distance(histograms[i - 1], histograms[i]);
        var threshold = 0.55 - 0.35 * Math.Clamp(sensitivity, 0, 1);
        var cuts = new List<int>();
        for (var i = 1; i < n; i++)
        {
            if (d[i] < threshold) continue;
            // Média local (excluindo o próprio quadro): o corte precisa se destacar do movimento normal.
            double sum = 0;
            var count = 0;
            for (var k = Math.Max(1, i - 8); k <= Math.Min(n - 1, i + 8); k++)
            {
                if (k == i) continue;
                sum += d[k];
                count++;
            }
            var local = count > 0 ? sum / count : 0;
            if (d[i] < local * 2.5) continue;
            if (i + 1 < n && d[i + 1] > d[i]) continue; // pico (transição de dois quadros)
            if (cuts.Count > 0 && i - cuts[^1] < minGap) continue;
            cuts.Add(i);
        }
        return cuts;
    }
}
