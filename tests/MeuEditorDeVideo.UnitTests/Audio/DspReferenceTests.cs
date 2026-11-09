using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Domain.Effects;

namespace MeuEditorDeVideo.UnitTests.Audio;

/// <summary>Valores de referência dos blocos de DSP (normas e propriedades físicas).</summary>
public sealed class DspReferenceTests
{
    private const int Rate = 48000;

    private static float[] Sine(double freq, double amplitude, double seconds, double phase = 0)
    {
        var n = (int)(Rate * seconds);
        var s = new float[n * 2];
        for (var i = 0; i < n; i++)
            s[i * 2] = s[i * 2 + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / Rate + phase));
        return s;
    }

    private static double Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;
        foreach (var v in s) sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, s.Length));
    }

    private static double DominantFrequency(float[] stereo, int from, int frames)
    {
        // Contagem de cruzamentos por zero no canal esquerdo.
        var crossings = 0;
        for (var i = from + 1; i < from + frames; i++)
            if (stereo[(i - 1) * 2] < 0 && stereo[i * 2] >= 0) crossings++;
        return crossings * (double)Rate / frames;
    }

    [Fact]
    public void Stereo_sine_at_minus_20_dbfs_measures_minus_20_lufs()
    {
        // BS.1770: seno de 1 kHz a 0 dBFS em um canal = −3,01 LKFS; nos dois canais = 0 LUFS.
        var meter = new LoudnessMeter();
        meter.Add(Sine(1000, 0.1, 5));
        Assert.InRange(meter.IntegratedLufs, -20.2, -19.8);
        Assert.InRange(meter.PeakDbfs, -20.1, -19.9);
    }

    [Fact]
    public void Silence_has_no_integrated_loudness()
    {
        var meter = new LoudnessMeter();
        meter.Add(new float[Rate * 2 * 2]);
        Assert.True(double.IsNegativeInfinity(meter.IntegratedLufs));
    }

    [Fact]
    public void Normalization_gain_is_limited_by_peak()
    {
        Assert.Equal(6, LoudnessMeter.NormalizationGainDb(-20, -10, -14), 6);
        Assert.Equal(3, LoudnessMeter.NormalizationGainDb(-20, -4, -14), 6); // pico iria a +2 dBFS → limita em −1
    }

    [Fact]
    public void Time_stretch_keeps_pitch_at_double_speed()
    {
        var input = Sine(440, 0.5, 4);
        var producer = new TimeStretchProducer((pos, dst) =>
        {
            for (var i = 0; i < dst.Length / 2; i++)
            {
                var p = pos + i;
                dst[i * 2] = dst[i * 2 + 1] = p >= 0 && p * 2 < input.Length ? input[p * 2] : 0;
            }
        }, 0, 2.0);
        var output = new float[Rate * 2];
        producer.Read(output); // 1 s de saída = 2 s de entrada
        Assert.InRange(DominantFrequency(output, 4800, 38400), 430, 450);
        Assert.InRange(Rms(output.AsSpan(9600, 76800)), 0.30, 0.40); // seno de 0,5 → RMS ≈ 0,354, sem cancelamentos
    }

    [Fact]
    public void Resample_raises_pitch_at_double_speed()
    {
        var input = Sine(440, 0.5, 4);
        var producer = new ResampleProducer((pos, dst) =>
        {
            for (var i = 0; i < dst.Length / 2; i++)
            {
                var p = pos + i;
                dst[i * 2] = dst[i * 2 + 1] = p >= 0 && p * 2 < input.Length ? input[p * 2] : 0;
            }
        }, 0, 2.0);
        var output = new float[Rate * 2];
        producer.Read(output);
        Assert.InRange(DominantFrequency(output, 0, 38400), 870, 890);
    }

    [Fact]
    public void Denoiser_reduces_steady_noise_and_keeps_tone()
    {
        var rng = new Random(1);
        var seconds = 4;
        var noise = new float[Rate * seconds * 2];
        for (var i = 0; i < noise.Length; i++) noise[i] = (float)((rng.NextDouble() * 2 - 1) * 0.05);
        var dn = new SpectralDenoiser();
        dn.Configure(EffectCatalog.Create(EffectCatalog.Denoise));
        var processed = (float[])noise.Clone();
        dn.Process(processed);
        // Após aprender o ruído (2 s), o nível cai bem.
        var tail = processed.AsSpan(Rate * 2 * 2);
        Assert.True(Rms(tail) < Rms(noise.AsSpan(Rate * 2 * 2)) * 0.4, $"{Rms(tail)}");

        // Um som que surge sobre o ruído (como uma fala) passa praticamente intacto.
        // (Um tom perfeitamente constante seria tratado como ruído — é o caso de zumbidos.)
        dn.Reset();
        var mixed = (float[])noise.Clone();
        var burst = Sine(1000, 0.5, 0.4);
        var at = Rate * 2 * 2; // aos 2 s
        for (var i = 0; i < burst.Length; i++) mixed[at + i] += burst[i];
        var input = (float[])mixed.Clone();
        dn.Process(mixed);
        Assert.Equal(1024, dn.LatencyFrames);
        var lat = dn.LatencyFrames * 2;
        var outRms = Rms(mixed.AsSpan(at + lat + 9600, burst.Length - 19200));
        var inRms = Rms(input.AsSpan(at + 9600, burst.Length - 19200));
        Assert.InRange(outRms / inRms, 0.85, 1.1);
    }

    [Fact]
    public void Equalizer_boosts_low_shelf()
    {
        var eq = new EqualizerProcessor();
        eq.Configure(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Equalizer), "lowGain", 6));
        Assert.InRange(eq.MagnitudeDb(30), 5.5, 6.2);
        Assert.InRange(eq.MagnitudeDb(5000), -0.2, 0.2);
    }

    [Fact]
    public void Compressor_reduces_loud_signal()
    {
        var c = new CompressorProcessor();
        c.Configure(EffectCatalog.Create(EffectCatalog.Compressor)); // −18 dB, 4:1
        var s = Sine(200, 1.0, 1);
        c.Process(s);
        // Pico 0 dB, 18 dB acima do limiar → ~13,5 dB de redução.
        Assert.InRange(c.GainReductionDb, 12, 15);
    }

    [Fact]
    public void Correlator_finds_known_offset()
    {
        var rng = new Random(3);
        var reference = new double[16000];
        for (var i = 0; i < reference.Length; i++) reference[i] = rng.NextDouble() * 2 - 1;
        // other começa 3000 amostras depois do início da referência.
        var other = reference.Skip(3000).Take(8000).ToArray();
        var (lag, confidence) = AudioCorrelator.FindLag(reference, other, 16000);
        Assert.Equal(3000, lag);
        Assert.True(confidence > 0.9);
    }

    [Fact]
    public void Silence_detector_finds_gap_with_padding()
    {
        var s = new List<float>();
        s.AddRange(Sine(300, 0.5, 1));
        s.AddRange(new float[Rate * 2]); // 1 s de silêncio
        s.AddRange(Sine(300, 0.5, 1));
        var ranges = SilenceDetector.Detect(s.ToArray(), -40, Rate / 2, Rate / 10);
        var r = Assert.Single(ranges);
        Assert.InRange(r.Start, Rate + 4800 - 480, Rate + 4800 + 480);
        Assert.InRange(r.End, 2 * Rate - 4800 - 480, 2 * Rate - 4800 + 480);
    }
}
