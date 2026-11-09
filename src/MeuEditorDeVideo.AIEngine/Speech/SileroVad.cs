using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// Silero VAD v5 (ONNX) em streaming: probabilidade de fala por janela de 512 amostras a 16 kHz.
/// Cada chamada recebe 64 amostras de contexto (fim da janela anterior) seguidas da janela atual, como no wrapper oficial.
/// </summary>
public sealed class SileroVad(InferenceSession session)
{
    /// <summary>Tamanho da janela (amostras a 16 kHz).</summary>
    public const int WindowSamples = 512;

    /// <summary>Amostras de contexto prefixadas a cada janela.</summary>
    public const int ContextSamples = 64;

    private const int StateSize = 2 * 1 * 128;
    private const int SampleRate = 16000;

    /// <summary>Probabilidade de fala de cada janela (a última é preenchida com zeros).</summary>
    /// <param name="mono16k">Áudio mono a 16 kHz.</param>
    public float[] Probabilities(ReadOnlySpan<float> mono16k, IProgress<double>? progress, CancellationToken ct)
    {
        var windows = (mono16k.Length + WindowSamples - 1) / WindowSamples;
        var probs = new float[windows];
        if (windows == 0) return probs;

        var input = new DenseTensor<float>(new[] { 1, ContextSamples + WindowSamples });
        var state = new DenseTensor<float>(new[] { 2, 1, 128 });
        var sr = new DenseTensor<long>(new Memory<long>([SampleRate]), ReadOnlySpan<int>.Empty); // escalar (posto 0)
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", input),
            NamedOnnxValue.CreateFromTensor("state", state),
            NamedOnnxValue.CreateFromTensor("sr", sr),
        };
        var outputNames = new[] { "output", "stateN" };

        for (var w = 0; w < windows; w++)
        {
            ct.ThrowIfCancellationRequested();
            var buf = input.Buffer.Span;
            // Janela atual (com preenchimento de zeros no fim) após o contexto.
            var offset = w * WindowSamples;
            for (var i = 0; i < WindowSamples; i++)
            {
                var src = offset + i;
                buf[ContextSamples + i] = src < mono16k.Length ? mono16k[src] : 0f;
            }

            using (var results = session.Run(inputs, outputNames))
            {
                probs[w] = results.First(r => r.Name == "output").AsEnumerable<float>().First();
                var stateN = results.First(r => r.Name == "stateN").AsEnumerable<float>();
                CopyTo(stateN, state.Buffer.Span);
            }

            // O contexto da próxima janela são as últimas 64 amostras da atual.
            buf.Slice(WindowSamples, ContextSamples).CopyTo(buf[..ContextSamples]);

            if (progress is not null && (w % 32 == 31 || w == windows - 1)) progress.Report((double)(w + 1) / windows);
        }
        return probs;
    }

    private static void CopyTo(IEnumerable<float> values, Span<float> destination)
    {
        var i = 0;
        foreach (var v in values)
        {
            if (i >= destination.Length) break;
            destination[i++] = v;
        }
    }
}
