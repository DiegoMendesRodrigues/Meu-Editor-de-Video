using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// GTCRN em modo streaming: realce de fala quadro a quadro (STFT de 512 amostras a 16 kHz). Os caches recorrentes
/// são repassados entre os quadros; os tensores são reaproveitados para evitar alocações por quadro.
/// </summary>
public sealed class GtcrnEnhancer(InferenceSession session)
{
    private static readonly string[] InputNames = ["mix", "conv_cache", "tra_cache", "inter_cache"];
    private static readonly string[] OutputNames = ["enh", "conv_cache_out", "tra_cache_out", "inter_cache_out"];

    /// <summary>Voz realçada, no mesmo comprimento de <paramref name="mono16k"/>.</summary>
    public float[] Enhance(ReadOnlySpan<float> mono16k, IProgress<double>? progress, CancellationToken ct)
    {
        var frames = Stft.FrameCount(mono16k.Length);
        var mix = new DenseTensor<float>(new[] { 1, Stft.Bins, 1, 2 });
        var convCache = new DenseTensor<float>(new[] { 2, 1, 16, 16, 33 });
        var traCache = new DenseTensor<float>(new[] { 2, 3, 1, 1, 16 });
        var interCache = new DenseTensor<float>(new[] { 2, 1, 33, 16 });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputNames[0], mix),
            NamedOnnxValue.CreateFromTensor(InputNames[1], convCache),
            NamedOnnxValue.CreateFromTensor(InputNames[2], traCache),
            NamedOnnxValue.CreateFromTensor(InputNames[3], interCache),
        };

        return Stft.Process(mono16k, (f, re, im) =>
        {
            ct.ThrowIfCancellationRequested();
            var m = mix.Buffer.Span;
            for (var k = 0; k < Stft.Bins; k++)
            {
                m[2 * k] = (float)re[k];
                m[2 * k + 1] = (float)im[k];
            }

            using (var results = session.Run(inputs, OutputNames))
            {
                var enh = results.First(r => r.Name == OutputNames[0]).AsEnumerable<float>();
                var i = 0;
                foreach (var v in enh)
                {
                    if (i >= 2 * Stft.Bins) break;
                    var k = i / 2;
                    if (i % 2 == 0) re[k] = v;
                    else im[k] = v;
                    i++;
                }
                CopyCache(results.First(r => r.Name == OutputNames[1]), convCache);
                CopyCache(results.First(r => r.Name == OutputNames[2]), traCache);
                CopyCache(results.First(r => r.Name == OutputNames[3]), interCache);
            }

            if (progress is not null && (f % 64 == 63 || f == frames - 1)) progress.Report((double)(f + 1) / frames);
        });
    }

    private static void CopyCache(DisposableNamedOnnxValue value, DenseTensor<float> cache)
    {
        var dst = cache.Buffer.Span;
        var i = 0;
        foreach (var v in value.AsEnumerable<float>())
        {
            if (i >= dst.Length) break;
            dst[i++] = v;
        }
    }
}
