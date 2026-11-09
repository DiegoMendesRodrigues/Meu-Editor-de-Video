using MeuEditorDeVideo.AudioEngine.Dsp;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class BiquadFftTests
{
    private const int Rate = 48000;

    private static float[] StereoSine(double freq, double amplitude, int frames)
    {
        var s = new float[frames * 2];
        for (var i = 0; i < frames; i++)
            s[i * 2] = s[i * 2 + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / Rate));
        return s;
    }

    private static double Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    /// <summary>Razão RMS saída/entrada de um seno de 1 s, medida nos 0,5 s finais (regime permanente).</summary>
    private static double GainRatio(Biquad filter, double freq)
    {
        var input = StereoSine(freq, 0.1, Rate);
        var output = (float[])input.Clone();
        filter.Process(output);
        return Rms(output.AsSpan(Rate, Rate)) / Rms(input.AsSpan(Rate, Rate));
    }

    // ---------------------------------------------------------------- Biquad: resposta em frequência

    [Fact]
    public void Peaking_boost_reaches_full_gain_at_center_frequency()
    {
        Assert.InRange(Biquad.Peaking(1000, 6, 1, Rate).MagnitudeDb(1000, Rate), 5.95, 6.05);
    }

    [Fact]
    public void Peaking_is_flat_far_from_its_center_frequency()
    {
        var f = Biquad.Peaking(1000, 6, 1, Rate);
        Assert.InRange(f.MagnitudeDb(30, Rate), -0.5, 0.5);
        Assert.InRange(f.MagnitudeDb(15000, Rate), -0.5, 0.5);
    }

    [Fact]
    public void Low_shelf_gain_applies_below_the_corner_and_not_above_it()
    {
        var f = Biquad.LowShelf(120, 6, Rate);
        Assert.InRange(f.MagnitudeDb(30, Rate), 5.5, 6.2);
        Assert.InRange(f.MagnitudeDb(10000, Rate), -0.3, 0.3);
    }

    [Fact]
    public void High_pass_removes_bass_and_keeps_treble()
    {
        var f = Biquad.HighPass(1000, 0.7071, Rate);
        Assert.True(f.MagnitudeDb(100, Rate) < -20);
        Assert.InRange(f.MagnitudeDb(10000, Rate), -0.2, 0.2);
    }

    [Fact]
    public void Low_pass_removes_treble_and_keeps_bass()
    {
        var f = Biquad.LowPass(1000, 0.7071, Rate);
        Assert.True(f.MagnitudeDb(10000, Rate) < -30);
        Assert.InRange(f.MagnitudeDb(100, Rate), -0.2, 0.2);
    }

    // ---------------------------------------------------------------- Biquad: processamento

    [Fact]
    public void Peaking_boost_multiplies_a_centered_sine_by_its_gain()
    {
        // 10^(6/20) ≈ 1,995
        Assert.InRange(GainRatio(Biquad.Peaking(1000, 6, 1, Rate), 1000), 1.97, 2.02);
    }

    [Fact]
    public void Low_pass_attenuates_a_tone_well_above_its_corner_and_passes_a_low_one()
    {
        Assert.True(GainRatio(Biquad.LowPass(500, 0.7071, Rate), 8000) < 0.02);
        Assert.InRange(GainRatio(Biquad.LowPass(500, 0.7071, Rate), 100), 0.97, 1.03);
    }

    [Fact]
    public void Reset_clears_the_filter_memory()
    {
        var f = Biquad.LowPass(1000, 0.7071, Rate);
        f.Process(StereoSine(1000, 1, 1000));
        f.Reset();

        // Sem reset, a memória do filtro ainda geraria saída diferente de zero no silêncio.
        var silence = new float[2000];
        f.Process(silence);
        Assert.True(silence.All(v => v == 0f));
    }

    [Fact]
    public void Identity_coefficients_pass_the_signal_unchanged()
    {
        var f = Biquad.FromCoefficients(1, 0, 0, 0, 0);
        var rng = new Random(5);
        var input = new float[2000];
        for (var i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() * 2 - 1);

        var output = (float[])input.Clone();
        f.Process(output);

        Assert.Equal(input, output);
    }

    // ---------------------------------------------------------------- Fft

    [Fact]
    public void Forward_then_inverse_transform_returns_the_original_signal()
    {
        var rng = new Random(11);
        var re = Enumerable.Range(0, 64).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var im = Enumerable.Range(0, 64).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var re0 = (double[])re.Clone();
        var im0 = (double[])im.Clone();

        Fft.Transform(re, im);
        Fft.Transform(re, im, inverse: true);

        for (var i = 0; i < re.Length; i++)
        {
            Assert.InRange(re[i], re0[i] - 1e-9, re0[i] + 1e-9);
            Assert.InRange(im[i], im0[i] - 1e-9, im0[i] + 1e-9);
        }
    }

    [Fact]
    public void An_impulse_has_a_flat_spectrum()
    {
        var re = new double[32];
        var im = new double[32];
        re[0] = 1;

        Fft.Transform(re, im);

        for (var k = 0; k < re.Length; k++)
        {
            Assert.InRange(re[k], 1 - 1e-12, 1 + 1e-12);
            Assert.InRange(im[k], -1e-12, 1e-12);
        }
    }

    [Fact]
    public void Transform_rejects_sizes_that_are_not_powers_of_two()
    {
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[6], new double[6]));
    }

    [Fact]
    public void NextPowerOfTwo_rounds_up_to_the_next_power()
    {
        Assert.Equal(1, Fft.NextPowerOfTwo(1));
        Assert.Equal(4, Fft.NextPowerOfTwo(3));
        Assert.Equal(2048, Fft.NextPowerOfTwo(1025));
    }
}
