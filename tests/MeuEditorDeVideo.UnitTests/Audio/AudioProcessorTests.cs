using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class AudioProcessorTests
{
    private const int Rate = 48000;

    private static EffectInstance Fx(string type, params (string Key, double Value)[] values)
    {
        var e = EffectCatalog.Create(type);
        foreach (var (key, value) in values) e = EffectCatalog.WithValue(e, key, value);
        return e;
    }

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

    // ---------------------------------------------------------------- Eco

    [Fact]
    public void Echo_repeats_an_impulse_once_at_the_configured_delay()
    {
        var echo = new EchoProcessor();
        echo.Configure(Fx(EffectCatalog.Echo, ("delay", 100), ("feedback", 0), ("mix", 50)));
        var s = new float[2 * 12000];
        s[0] = s[1] = 1;

        echo.Process(s);

        Assert.Equal(1f, s[0]);                       // seco
        Assert.Equal(0f, s[2 * 4799]);                // antes do atraso
        Assert.InRange(s[2 * 4800], 0.4999, 0.5001);  // 100 ms = 4800 quadros, mistura 50 %
        Assert.InRange(s[2 * 4800 + 1], 0.4999, 0.5001);
        Assert.Equal(0f, s[2 * 9600]);                // sem realimentação, não há segunda repetição
    }

    [Fact]
    public void Echo_with_zero_mix_leaves_the_signal_unchanged()
    {
        var echo = new EchoProcessor();
        echo.Configure(Fx(EffectCatalog.Echo, ("mix", 0)));
        var input = StereoSine(440, 0.5, 4800);
        var output = (float[])input.Clone();

        echo.Process(output);

        Assert.Equal(input, output);
    }

    // ---------------------------------------------------------------- Reverberação

    [Fact]
    public void Reverb_with_zero_mix_passes_the_dry_signal()
    {
        var rev = new ReverbProcessor();
        rev.Configure(Fx(EffectCatalog.Reverb, ("mix", 0)));
        var input = StereoSine(440, 0.5, 4800);
        var output = (float[])input.Clone();

        rev.Process(output);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Reverb_keeps_ringing_after_an_impulse()
    {
        var rev = new ReverbProcessor();
        rev.Configure(Fx(EffectCatalog.Reverb, ("mix", 100)));
        var s = new float[2 * Rate * 2];
        s[0] = s[1] = 1;

        rev.Process(s);

        var peak = 0f;
        for (var i = 2 * Rate / 5; i < 2 * Rate * 3 / 2; i++) peak = Math.Max(peak, Math.Abs(s[i]));
        Assert.True(peak > 1e-3f, $"cauda muito fraca: {peak}");
    }

    // ---------------------------------------------------------------- Distorção

    [Fact]
    public void Distortion_with_zero_mix_leaves_the_signal_unchanged()
    {
        var d = new DistortionProcessor();
        d.Configure(Fx(EffectCatalog.Distortion, ("mix", 0)));
        var input = StereoSine(440, 0.8, 4800);
        var output = (float[])input.Clone();

        d.Process(output);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Distortion_output_stays_within_full_scale_for_large_inputs()
    {
        var d = new DistortionProcessor();
        d.Configure(Fx(EffectCatalog.Distortion, ("drive", 100), ("mix", 100)));
        var s = new float[] { 0.1f, -0.5f, 2f, -10f, 100f, -100f, 0.001f, 0f };

        d.Process(s);

        Assert.True(s.All(v => Math.Abs(v) <= 1.001f));
    }

    // ---------------------------------------------------------------- Telefone

    [Fact]
    public void Telephone_with_zero_intensity_leaves_the_signal_unchanged()
    {
        var t = new TelephoneProcessor();
        t.Configure(Fx(EffectCatalog.Telephone, ("intensity", 0)));
        var input = StereoSine(1000, 0.5, 4800);
        var output = (float[])input.Clone();

        t.Process(output);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Telephone_band_keeps_voice_range_and_cuts_bass_and_treble()
    {
        double Level(double freq)
        {
            var t = new TelephoneProcessor();
            t.Configure(Fx(EffectCatalog.Telephone, ("intensity", 100)));
            var s = StereoSine(freq, 0.5, Rate);
            t.Process(s);
            return Rms(s.AsSpan(Rate, Rate)); // último 0,5 s
        }

        var voice = Level(1000);
        Assert.True(Level(100) < 0.1 * voice);
        Assert.True(Level(10000) < 0.1 * voice);
    }

    // ---------------------------------------------------------------- Compressor

    [Theory]
    [InlineData(-18.0, 0.5625)] // meio do joelho
    [InlineData(-15.0, 2.25)]   // fim do joelho: continua com a curva de razão
    [InlineData(-8.0, 7.5)]     // acima: 10 dB × (1 − 1/4)
    [InlineData(0.0, 13.5)]
    public void Compressor_static_curve_has_a_soft_knee_and_then_follows_the_ratio(double level, double expected)
    {
        Assert.InRange(CompressorProcessor.StaticReduction(level, -18, 4), expected - 1e-9, expected + 1e-9);
    }

    [Fact]
    public void Compressor_leaves_quiet_passages_alone_and_applies_makeup_gain()
    {
        var c = new CompressorProcessor();
        c.Configure(Fx(EffectCatalog.Compressor, ("makeup", 6)));
        var input = StereoSine(200, 0.05, Rate / 10); // -26 dBFS, abaixo do limiar de -18 dB
        var output = (float[])input.Clone();

        c.Process(output);

        var ratio = output.Max(v => Math.Abs(v)) / input.Max(v => Math.Abs(v));
        Assert.InRange(ratio, 1.98, 2.01); // 10^(6/20) ≈ 1,995
    }

    // ---------------------------------------------------------------- Equalizador

    // ---------------------------------------------------------------- Cadeia e fábrica

    [Fact]
    public void Chain_update_reports_structure_changes_but_not_parameter_changes()
    {
        var chain = new AudioEffectChain();
        var echo = Fx(EffectCatalog.Echo);

        Assert.True(chain.Update([echo]));                                           // primeira vez
        Assert.False(chain.Update([EffectCatalog.WithValue(echo, "mix", 80)]));     // só parâmetro mudou
        Assert.True(chain.Update([echo, Fx(EffectCatalog.Compressor)]));            // efeito novo
        Assert.True(chain.Update([Fx(EffectCatalog.Echo)]));                        // mesmo tipo, outro Id
    }

    [Fact]
    public void Chain_sums_latency_and_takes_the_longest_pre_roll()
    {
        var chain = new AudioEffectChain();
        chain.Update([EffectCatalog.Create(EffectCatalog.Denoise), Fx(EffectCatalog.Equalizer, ("lowGain", 3))]);

        Assert.Equal(1024, chain.LatencyFrames);  // STFT do redutor de ruído
        Assert.Equal(Rate, chain.PreRollFrames);   // 1 s para aprender o piso de ruído
        Assert.False(chain.IsEmpty);
    }

    [Fact]
    public void Factory_returns_no_processor_for_visual_effects_and_skips_them_in_the_chain()
    {
        Assert.Null(AudioProcessorFactory.Create(EffectCatalog.Create(EffectCatalog.Blur)));
        Assert.IsType<EqualizerProcessor>(AudioProcessorFactory.Create(EffectCatalog.Create(EffectCatalog.Equalizer)));
        Assert.IsType<SpectralDenoiser>(AudioProcessorFactory.Create(EffectCatalog.Create(EffectCatalog.Denoise)));

        var chain = new AudioEffectChain();
        chain.Update([EffectCatalog.Create(EffectCatalog.Blur)]);
        Assert.True(chain.IsEmpty);
    }
}
