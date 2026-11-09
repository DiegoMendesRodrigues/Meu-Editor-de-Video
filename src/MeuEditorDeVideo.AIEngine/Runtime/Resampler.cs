namespace MeuEditorDeVideo.AIEngine.Runtime;

/// <summary>Conversão de taxa 48 kHz ↔ 16 kHz (fator 3) com filtro FIR janelado (corte em 7,2 kHz).</summary>
public static class Resampler
{
    private const int Taps = 63;
    private const int Mid = (Taps - 1) / 2;
    private static readonly float[] Kernel = BuildKernel();

    private static float[] BuildKernel()
    {
        var k = new double[Taps];
        const double cutoff = 7200.0 / 48000.0; // fração da taxa alta
        double sum = 0;
        for (var i = 0; i < Taps; i++)
        {
            var n = i - Mid;
            var sinc = n == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * n) / (Math.PI * n);
            var window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (Taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (Taps - 1)); // Blackman
            k[i] = sinc * window;
            sum += k[i];
        }
        return k.Select(v => (float)(v / sum)).ToArray();
    }

    /// <summary>48 kHz → 16 kHz.</summary>
    public static float[] Downsample3(ReadOnlySpan<float> input)
    {
        var result = new float[input.Length / 3];
        for (var o = 0; o < result.Length; o++)
        {
            var first = o * 3 - Mid;
            double acc = 0;
            for (var t = 0; t < Taps; t++)
            {
                var j = first + t;
                if ((uint)j < (uint)input.Length) acc += input[j] * Kernel[t];
            }
            result[o] = (float)acc;
        }
        return result;
    }

    /// <summary>16 kHz → 48 kHz (inserção de zeros + o mesmo filtro com ganho 3).</summary>
    public static float[] Upsample3(ReadOnlySpan<float> input, int outputLength)
    {
        var result = new float[outputLength];
        for (var o = 0; o < outputLength; o++)
        {
            var first = o - Mid;
            // Só as posições múltiplas de 3 (amostras reais) contribuem.
            var t0 = ((-first % 3) + 3) % 3;
            double acc = 0;
            for (var t = t0; t < Taps; t += 3)
            {
                var j = first + t;
                var src = j / 3;
                if (j >= 0 && src < input.Length) acc += input[src] * Kernel[t];
            }
            result[o] = (float)(acc * 3);
        }
        return result;
    }
}
