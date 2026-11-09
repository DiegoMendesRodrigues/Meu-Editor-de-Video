using MeuEditorDeVideo.AudioEngine.Dsp;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class LoudnessAnalysisTests
{
    private const int Rate = 48000;

    /// <summary>Seno estéreo com nível em dBFS (pico); <paramref name="left"/>/<paramref name="right"/> ligam cada canal.</summary>
    private static float[] Tone(double freq, double dbfs, double seconds, bool left = true, bool right = true)
    {
        var amp = Math.Pow(10, dbfs / 20);
        var n = (int)(Rate * seconds);
        var s = new float[n * 2];
        for (var i = 0; i < n; i++)
        {
            var v = (float)(amp * Math.Sin(2 * Math.PI * freq * i / Rate));
            s[i * 2] = left ? v : 0;
            s[i * 2 + 1] = right ? v : 0;
        }
        return s;
    }

    private static float[] Silence(double seconds) => new float[(int)(Rate * seconds) * 2];

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    // ---------------------------------------------------------------- LoudnessMeter

    [Fact]
    public void Stereo_tone_at_minus_23_dbfs_measures_about_minus_23_lufs()
    {
        var m = new LoudnessMeter();
        m.Add(Tone(1000, -23, 10));
        Assert.InRange(m.IntegratedLufs, -23.3, -22.7);
    }

    [Fact]
    public void Tone_on_one_channel_only_measures_about_three_db_lower()
    {
        var stereo = new LoudnessMeter();
        stereo.Add(Tone(1000, -23, 10));
        var mono = new LoudnessMeter();
        mono.Add(Tone(1000, -23, 10, right: false));

        Assert.InRange(stereo.IntegratedLufs - mono.IntegratedLufs, 2.8, 3.2);
    }

    [Fact]
    public void Appending_long_silence_barely_moves_the_integrated_value()
    {
        // Os blocos de silêncio ficam abaixo da comporta absoluta; só as bordas parciais do tom mudam um pouco a média.
        var a = new LoudnessMeter();
        a.Add(Tone(1000, -23, 5));
        var b = new LoudnessMeter();
        b.Add(Tone(1000, -23, 5));
        b.Add(Silence(10));

        Assert.InRange(b.IntegratedLufs - a.IntegratedLufs, -0.3, 0.3);
    }

    [Fact]
    public void Relative_gate_drops_passages_far_below_the_programme_level()
    {
        var m = new LoudnessMeter();
        m.Add(Tone(1000, -23, 5));
        m.Add(Tone(1000, -40, 5)); // acima da comporta absoluta, mas 17 LU abaixo: fora da relativa

        Assert.InRange(m.IntegratedLufs, -23.6, -22.6);
    }

    [Fact]
    public void Signal_below_the_absolute_gate_has_no_integrated_loudness()
    {
        var m = new LoudnessMeter();
        m.Add(Tone(1000, -75, 5));

        Assert.True(double.IsNegativeInfinity(m.IntegratedLufs));
    }

    [Theory]
    [InlineData(-23.0, -20.0, -14.0, -1.0, 9.0)]  // alvo manda
    [InlineData(-23.0, -3.0, -14.0, -1.0, 2.0)]   // o pico limita
    [InlineData(-20.0, -6.0, -14.0, -2.0, 4.0)]   // teto personalizado
    public void Normalization_gain_is_the_smaller_of_target_and_peak_headroom(double lufs, double peak, double target, double ceiling, double expected)
    {
        Assert.InRange(LoudnessMeter.NormalizationGainDb(lufs, peak, target, ceiling), expected - 1e-9, expected + 1e-9);
    }

    [Fact]
    public void Normalization_gain_is_zero_for_a_silent_programme()
    {
        Assert.Equal(0.0, LoudnessMeter.NormalizationGainDb(double.NegativeInfinity, -6, -14));
    }

    // ---------------------------------------------------------------- SilenceDetector

    [Fact]
    public void Leading_silence_is_not_padded_on_the_start_side()
    {
        var s = Concat(Silence(1), Tone(300, -20, 1));

        var r = Assert.Single(SilenceDetector.Detect(s, -40, Rate / 2, Rate / 10));

        Assert.Equal(0L, r.Start);
        Assert.Equal(43200L, r.End); // 1 s − 0,1 s de respiro
    }

    [Fact]
    public void Trailing_silence_is_not_padded_on_the_end_side()
    {
        var s = Concat(Tone(300, -20, 1), Silence(1));

        var r = Assert.Single(SilenceDetector.Detect(s, -40, Rate / 2, Rate / 10));

        Assert.Equal(52800L, r.Start); // 1 s + 0,1 s
        Assert.Equal(96000L, r.End);
    }

    [Fact]
    public void Inner_silence_is_shrunk_by_the_padding_on_both_sides()
    {
        var s = Concat(Tone(300, -20, 1), Silence(1), Tone(300, -20, 1));

        var r = Assert.Single(SilenceDetector.Detect(s, -40, Rate / 2, Rate / 10));

        Assert.Equal(52800L, r.Start);
        Assert.Equal(91200L, r.End);
    }

    [Fact]
    public void Silence_shorter_than_the_minimum_is_ignored()
    {
        var s = Concat(Tone(300, -20, 1), Silence(0.2), Tone(300, -20, 1));

        Assert.Empty(SilenceDetector.Detect(s, -40, Rate / 2, Rate / 10));
    }

    [Fact]
    public void Streaming_in_small_chunks_gives_the_same_silences_as_one_call()
    {
        var s = Concat(Tone(300, -20, 1), Silence(0.5), Tone(300, -20, 1));
        var whole = SilenceDetector.Detect(s, -40, Rate / 4, Rate / 20);

        var d = new SilenceDetector();
        const int chunk = 2 * 1001; // quantidade de amostras por bloco, sem alinhar com a janela de 10 ms
        for (var i = 0; i < s.Length; i += chunk)
            d.Add(s.AsSpan(i, Math.Min(chunk, s.Length - i)));

        Assert.NotEmpty(whole);
        Assert.Equal((long)s.Length / 2, d.Frames);
        Assert.Equal(whole, d.Detect(-40, Rate / 4, Rate / 20));
    }

    // ---------------------------------------------------------------- AudioCorrelator

    [Fact]
    public void Analysis_signal_has_its_dc_offset_removed()
    {
        var stereo = Enumerable.Repeat(0.3f, 12000).ToArray(); // 6000 quadros → 1000 amostras
        var a = AudioCorrelator.ToAnalysis(stereo);

        Assert.Equal(1000, a.Length);
        Assert.True(a.All(v => Math.Abs(v) < 1e-9));
    }

    [Fact]
    public void Lag_is_negative_when_the_other_recording_starts_earlier()
    {
        var rng = new Random(21);
        var signal = Enumerable.Range(0, 20000).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var reference = signal[2000..18000]; // começa 2000 amostras depois
        var other = signal[..8000];

        var (lag, confidence) = AudioCorrelator.FindLag(reference, other, 16000);

        Assert.Equal(-2000, lag);
        Assert.True(confidence > 0.9, $"confiança baixa: {confidence}");
    }

    [Fact]
    public void Unrelated_noise_gives_low_correlation_confidence()
    {
        var rng = new Random(33);
        var a = Enumerable.Range(0, 8000).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var b = Enumerable.Range(0, 8000).Select(_ => rng.NextDouble() * 2 - 1).ToArray();

        var (_, confidence) = AudioCorrelator.FindLag(a, b, 4000);

        Assert.True(confidence < 0.3, $"confiança alta demais para ruídos independentes: {confidence}");
    }
}
