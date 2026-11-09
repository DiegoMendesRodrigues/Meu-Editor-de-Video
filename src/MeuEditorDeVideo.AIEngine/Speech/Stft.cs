using MeuEditorDeVideo.AudioEngine.Dsp;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// STFT/ISTFT para o GTCRN: 16 kHz, janela de 512 amostras, salto de 256 e raiz quadrada de Hann periódica
/// (análise e síntese). Com sobreposição de 50% a reconstrução sem processamento é exata.
/// </summary>
public static class Stft
{
    /// <summary>Tamanho da janela (amostras).</summary>
    public const int FrameSize = 512;

    /// <summary>Salto entre quadros (amostras).</summary>
    public const int Hop = FrameSize / 2;

    /// <summary>Quantidade de bins úteis do espectro (0…N/2).</summary>
    public const int Bins = FrameSize / 2 + 1;

    private static readonly double[] Window = BuildWindow();

    private static double[] BuildWindow()
    {
        var w = new double[FrameSize];
        for (var n = 0; n < FrameSize; n++) w[n] = Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * n / FrameSize));
        return w;
    }

    /// <summary>Quantidade de quadros usados para processar <paramref name="length"/> amostras.</summary>
    public static int FrameCount(int length)
    {
        if (length <= 0) return 0;
        var lastPadded = length - 1 + Hop; // posição (com preenchimento inicial) da última amostra
        return lastPadded / Hop + 1;
    }

    /// <summary>
    /// Analisa o sinal, chama <paramref name="processFrame"/> com o índice do quadro e os <see cref="Bins"/> valores
    /// real/imaginário (podem ser alterados in-place) e sintetiza a saída por sobreposição. Retorna exatamente
    /// <paramref name="signal"/>.Length amostras alinhadas à entrada.
    /// </summary>
    public static float[] Process(ReadOnlySpan<float> signal, Action<int, double[], double[]> processFrame)
    {
        var length = signal.Length;
        if (length == 0) return [];

        var frames = FrameCount(length);
        // Preenchimento de Hop zeros no início; o buffer cobre todos os quadros.
        var padded = new double[(frames + 1) * Hop];
        for (var i = 0; i < length; i++) padded[Hop + i] = signal[i];
        var output = new double[padded.Length];

        var re = new double[FrameSize];
        var im = new double[FrameSize];
        var binRe = new double[Bins];
        var binIm = new double[Bins];

        for (var f = 0; f < frames; f++)
        {
            var start = f * Hop;
            for (var n = 0; n < FrameSize; n++)
            {
                re[n] = padded[start + n] * Window[n];
                im[n] = 0;
            }
            Fft.Transform(re, im);
            Array.Copy(re, binRe, Bins);
            Array.Copy(im, binIm, Bins);

            processFrame(f, binRe, binIm);

            // Espectro hermitiano: X[N-k] = conj(X[k]), para que a IFFT seja real.
            for (var k = 0; k < Bins; k++)
            {
                re[k] = binRe[k];
                im[k] = binIm[k];
            }
            for (var k = 1; k < Bins - 1; k++)
            {
                re[FrameSize - k] = binRe[k];
                im[FrameSize - k] = -binIm[k];
            }
            Fft.Transform(re, im, inverse: true);
            for (var n = 0; n < FrameSize; n++) output[start + n] += re[n] * Window[n];
        }

        var result = new float[length];
        for (var i = 0; i < length; i++) result[i] = (float)output[Hop + i];
        return result;
    }
}
