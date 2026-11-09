using MeuEditorDeVideo.AudioEngine.Dsp;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class ProducerTests
{
    private const int Rate = 48000;

    /// <summary>Fonte de entrada: seno estéreo calculado a partir da posição absoluta (negativas = silêncio).</summary>
    private static FrameReader Tone(double freq) => (pos, dst) =>
    {
        for (var i = 0; i < dst.Length / 2; i++)
        {
            var p = pos + i;
            var v = p >= 0 ? (float)(0.5 * Math.Sin(2 * Math.PI * freq * p / Rate)) : 0f;
            dst[i * 2] = dst[i * 2 + 1] = v;
        }
    };

    /// <summary>Frequência estimada por cruzamentos ascendentes por zero no canal esquerdo.</summary>
    private static double Frequency(float[] stereo, int fromFrame, int frames)
    {
        var crossings = 0;
        for (var i = fromFrame + 1; i < fromFrame + frames; i++)
            if (stereo[(i - 1) * 2] < 0 && stereo[i * 2] >= 0) crossings++;
        return crossings * (double)Rate / frames;
    }

    private static double Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    [Fact]
    public void Direct_producer_reads_consecutive_positions()
    {
        var seen = new List<long>();
        var producer = new DirectProducer((pos, dst) => { seen.Add(pos); dst.Clear(); }, 5);

        producer.Read(new float[200]); // 100 quadros
        producer.Read(new float[200]);

        Assert.Equal(new long[] { 5, 105 }, seen);
    }

    [Fact]
    public void Resample_at_half_speed_halves_the_frequency()
    {
        var producer = new ResampleProducer(Tone(440), 0, 0.5);
        var output = new float[Rate * 2]; // 1 s de saída

        producer.Read(output);

        Assert.InRange(Frequency(output, 4800, 38400), 209, 231); // 440 / 2 = 220 Hz (±5 %)
    }

    [Fact]
    public void Time_stretch_at_half_speed_keeps_the_pitch()
    {
        var producer = new TimeStretchProducer(Tone(440), 0, 0.5);
        var output = new float[Rate * 2]; // 1 s de saída = 0,5 s de entrada

        producer.Read(output);

        Assert.InRange(Frequency(output, 9600, 38400), 418, 462); // 440 Hz ±5 %
        Assert.InRange(Rms(output), 0.25, 0.4); // seno de amplitude 0,5 → RMS ≈ 0,354
    }
}
