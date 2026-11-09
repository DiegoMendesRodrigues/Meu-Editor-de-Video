using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Segmentação de pessoas com MODNet (matting sem trimap): entrada RGB [1,3,H,W] normalizada para −1…1, com H e W
/// múltiplos de 32 e o lado menor em <see cref="ReferenceSize"/>; saída alfa [1,1,H,W] em 0…1.
/// </summary>
public sealed class ModnetSegmenter(InferenceSession session, int referenceSize = 512)
{
    private readonly string _input = session.InputMetadata.Keys.First();
    private readonly string _output = session.OutputMetadata.Keys.First();
    private readonly Lock _gate = new();

    public int ReferenceSize { get; } = Math.Clamp(referenceSize, 128, 1024);

    /// <summary>Tamanho de inferência para uma imagem: lado menor = referência, ambos múltiplos de 32.</summary>
    public static (int Width, int Height) InferenceSize(int width, int height, int reference)
    {
        var scale = (double)reference / Math.Min(width, height);
        static int Round32(double v) => Math.Max(32, (int)Math.Round(v / 32) * 32);
        return (Round32(width * scale), Round32(height * scale));
    }

    public Matte Segment(BgraImage frame)
    {
        var (w, h) = InferenceSize(frame.Width, frame.Height, ReferenceSize);
        using var src = CvImage.ToMat(frame);
        using var resized = new Mat();
        Cv2.Resize(src, resized, new Size(w, h), 0, 0, InterpolationFlags.Area);

        var plane = w * h;
        var data = new float[3 * plane];
        unsafe
        {
            var p = (byte*)resized.Data;
            var step = (int)resized.Step();
            for (var y = 0; y < h; y++)
            {
                var row = p + y * step;
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    // BGRA → RGB planar, (v/255 − 0,5)/0,5
                    data[i] = row[x * 4 + 2] / 127.5f - 1f;
                    data[plane + i] = row[x * 4 + 1] / 127.5f - 1f;
                    data[2 * plane + i] = row[x * 4] / 127.5f - 1f;
                }
            }
        }

        var alpha = new byte[plane];
        using var input = OrtValue.CreateTensorValueFromMemory(data, [1, 3, h, w]);
        lock (_gate)
        {
            using var outputs = session.Run(new RunOptions(), [_input], [input], [_output]);
            var m = outputs[0].GetTensorDataAsSpan<float>();
            for (var i = 0; i < plane; i++) alpha[i] = (byte)Math.Clamp((int)(m[i] * 255 + 0.5f), 0, 255);
        }
        return new Matte(w, h, alpha);
    }

    /// <summary>Caixa (centro e tamanho normalizados) da maior região de primeiro plano, ou null se vazia.</summary>
    public static (double X, double Y, double W, double H)? Bounds(Matte matte, byte threshold = 128)
    {
        using var gray = CvImage.Gray(matte.Alpha, matte.Width, matte.Height);
        using var bin = new Mat();
        Cv2.Threshold(gray, bin, threshold, 255, ThresholdTypes.Binary);
        Cv2.FindContours(bin, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return null;
        var best = contours.MaxBy(c => Cv2.ContourArea(c))!;
        var r = Cv2.BoundingRect(best);
        if (r.Width * r.Height < 16) return null;
        return ((r.X + r.Width / 2.0) / matte.Width, (r.Y + r.Height / 2.0) / matte.Height,
            (double)r.Width / matte.Width, (double)r.Height / matte.Height);
    }
}
