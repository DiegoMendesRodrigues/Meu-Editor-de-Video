using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Malha do rosto (MediaPipe Face Mesh V2, 478 pontos): entrada RGB [1,256,256,3] em 0…1 (NHWC) com o rosto
/// recortado num quadrado; saídas [1,1,1,1434] (x, y, z em pixels do recorte) e a presença do rosto (logit).
/// </summary>
public sealed class FaceMeshDetector(InferenceSession session)
{
    public const int InputSize = 256, PointCount = 478;

    private readonly string _input = session.InputMetadata.Keys.First();
    private readonly string[] _outputs = [.. session.OutputMetadata.Keys];
    private readonly Lock _gate = new();

    /// <summary>
    /// Calcula os pontos num recorte quadrado (centro cx, cy e lado em pixels do quadro). Devolve os pontos em pixels
    /// do quadro e a presença do rosto (0…1). Com o rosto pequeno no recorte a presença cai, mas os pontos ainda servem
    /// para enquadrar um segundo recorte.
    /// </summary>
    public (FaceLandmarks Points, double Score)? Detect(BgraImage frame, double cx, double cy, double side)
    {
        if (side < 8) return null;
        using var src = CvImage.ToMat(frame);
        // Recorte quadrado → 256×256 (bordas replicadas quando o quadrado sai do quadro).
        var s = InputSize / side;
        using var m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set(0, 0, s); m.Set(0, 1, 0.0); m.Set(0, 2, -(cx - side / 2) * s);
        m.Set(1, 0, 0.0); m.Set(1, 1, s); m.Set(1, 2, -(cy - side / 2) * s);
        using var crop = new Mat();
        Cv2.WarpAffine(src, crop, m, new Size(InputSize, InputSize), InterpolationFlags.Area, BorderTypes.Replicate);

        var data = new float[InputSize * InputSize * 3];
        unsafe
        {
            var p = (byte*)crop.Data;
            var step = (int)crop.Step();
            for (var y = 0; y < InputSize; y++)
            {
                var row = p + y * step;
                for (var x = 0; x < InputSize; x++)
                {
                    var i = (y * InputSize + x) * 3;
                    data[i] = row[x * 4 + 2] / 255f;
                    data[i + 1] = row[x * 4 + 1] / 255f;
                    data[i + 2] = row[x * 4] / 255f;
                }
            }
        }

        float[] coords;
        double? score = null;
        using var input = OrtValue.CreateTensorValueFromMemory(data, [1, InputSize, InputSize, 3]);
        lock (_gate)
        {
            using var results = session.Run(new RunOptions(), [_input], [input], _outputs);
            coords = [];
            foreach (var r in results)
            {
                var span = r.GetTensorDataAsSpan<float>();
                if (span.Length >= PointCount * 3) coords = span[..(PointCount * 3)].ToArray();
                // Presença do rosto: a primeira saída escalar (logit); a seguinte não indica rosto.
                else if (span.Length == 1) score ??= 1 / (1 + Math.Exp(-span[0]));
            }
        }
        if (coords.Length == 0) return null;

        var xs = new float[PointCount];
        var ys = new float[PointCount];
        var inv = side / InputSize;
        for (var k = 0; k < PointCount; k++)
        {
            xs[k] = (float)(cx - side / 2 + coords[k * 3] * inv);
            ys[k] = (float)(cy - side / 2 + coords[k * 3 + 1] * inv);
        }
        return (new FaceLandmarks(xs, ys), score ?? 1);
    }
}
