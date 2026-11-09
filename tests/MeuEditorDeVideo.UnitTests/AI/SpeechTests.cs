using System.IO;
using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.AIEngine.Speech;

namespace MeuEditorDeVideo.UnitTests.AI;

public sealed class SpeechTests
{
    private static float[] Sine(int n, double freq, double rate, double amp = 0.5) =>
        Enumerable.Range(0, n).Select(i => (float)(amp * Math.Sin(2 * Math.PI * freq * i / rate))).ToArray();

    private static double Rms(ReadOnlySpan<float> x)
    {
        double acc = 0;
        foreach (var v in x) acc += (double)v * v;
        return Math.Sqrt(acc / Math.Max(1, x.Length));
    }

    private static double Correlation(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var n = Math.Min(a.Length, b.Length);
        double ma = 0, mb = 0;
        for (var i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;
        double num = 0, da = 0, db = 0;
        for (var i = 0; i < n; i++)
        {
            var x = a[i] - ma;
            var y = b[i] - mb;
            num += x * y; da += x * x; db += y * y;
        }
        return num / Math.Sqrt(da * db);
    }

    /// <summary>Gera probabilidades a partir de trechos (quantidade de janelas, valor).</summary>
    private static List<float> Probs(params (int Count, float Value)[] runs)
    {
        var list = new List<float>();
        foreach (var (count, value) in runs)
            for (var i = 0; i < count; i++) list.Add(value);
        return list;
    }

    [Fact]
    public void Resampler_round_trip_preserves_440_Hz_sine()
    {
        var original = Sine(48000, 440, 48000);
        var down = Resampler.Downsample3(original);
        var back = Resampler.Upsample3(down, original.Length);

        Assert.Equal(16000, down.Length);
        var corr = Correlation(original.AsSpan(100, original.Length - 200), back.AsSpan(100, back.Length - 200));
        Assert.True(corr > 0.99, $"correlação {corr:F4}");
    }

    [Fact]
    public void Resampler_attenuates_12_kHz_above_new_nyquist_cutoff()
    {
        var input = Sine(48000, 12000, 48000);
        var down = Resampler.Downsample3(input);

        Assert.True(Rms(down) < 0.05 * Rms(input), $"RMS {Rms(down):E3} vs entrada {Rms(input):E3}");
    }

    [Fact]
    public void WavFile_writes_valid_header_and_clamps_samples()
    {
        var path = Path.Combine(Path.GetTempPath(), $"med-wav-{Guid.NewGuid():N}.wav");
        try
        {
            WavFile.Write(path, new float[] { 0.5f, -0.5f, 2f, -2f }, 48000, 2);

            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);
            Assert.Equal(52, fs.Length);
            Assert.Equal("RIFF", new string(r.ReadChars(4)));
            Assert.Equal(52 - 8, r.ReadInt32()); // tamanho do RIFF = total − 8
            Assert.Equal("WAVE", new string(r.ReadChars(4)));
            Assert.Equal("fmt ", new string(r.ReadChars(4)));
            Assert.Equal(16, r.ReadInt32());
            Assert.Equal(1, r.ReadInt16());      // PCM
            Assert.Equal(2, r.ReadInt16());      // canais
            Assert.Equal(48000, r.ReadInt32());  // taxa
            Assert.Equal(192000, r.ReadInt32()); // bytes/s
            Assert.Equal(4, r.ReadInt16());      // bloco
            Assert.Equal(16, r.ReadInt16());     // bits
            Assert.Equal("data", new string(r.ReadChars(4)));
            Assert.Equal(8, r.ReadInt32());
            Assert.Equal(16384, r.ReadInt16());
            Assert.Equal(-16384, r.ReadInt16());
            Assert.Equal(short.MaxValue, r.ReadInt16());
            Assert.Equal(short.MinValue, r.ReadInt16());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(5000)]
    public void Stft_identity_reconstruction(int length)
    {
        var rnd = new Random(7);
        var signal = new float[length];
        for (var i = 0; i < length; i++) signal[i] = (float)(rnd.NextDouble() * 2 - 1);

        var output = Stft.Process(signal, (_, _, _) => { });

        Assert.Equal(length, output.Length);
        var maxErr = 0f;
        for (var i = 0; i < length; i++) maxErr = Math.Max(maxErr, Math.Abs(output[i] - signal[i]));
        Assert.True(maxErr < 1e-4f, $"erro máximo {maxErr}");
    }

    [Fact]
    public void SpeechSegments_hysteresis_keeps_speech_through_intermediate_probabilities()
    {
        // Entre limiar − 0,15 (0,35) e limiar (0,5) a probabilidade não encerra a fala nem inicia outra.
        // Fala: janelas 5..24 e 45..64; trecho 0,4 nas janelas 25..44; silêncio a partir da janela 65.
        var probs = Probs((5, 0.1f), (20, 0.9f), (20, 0.4f), (20, 0.9f), (15, 0.1f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, probs.Count * 512L, padMs: 0);

        Assert.Single(segs);
        Assert.Equal(5 * 512L, segs[0].Start);
        Assert.Equal(65 * 512L, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_ends_speech_after_min_silence()
    {
        // 20 janelas de fala; silêncio a partir da janela 25: fim só após 300 ms (≥ 9,4 janelas).
        var probs = Probs((5, 0.1f), (20, 0.9f), (20, 0.1f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, probs.Count * 512L, padMs: 0);

        Assert.Single(segs);
        Assert.Equal(5 * 512L, segs[0].Start);
        Assert.Equal(25 * 512L, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_short_silence_gap_is_merged()
    {
        // Pausa de 5 janelas (160 ms) < 300 ms: uma única fala.
        var probs = Probs((10, 0.9f), (5, 0.1f), (10, 0.9f), (25, 0.1f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, probs.Count * 512L, padMs: 0);

        Assert.Single(segs);
        Assert.Equal(0, segs[0].Start);
        Assert.Equal(25 * 512L, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_long_silence_gap_splits_speech()
    {
        // Pausa de 15 janelas (480 ms) ≥ 300 ms: duas falas.
        var probs = Probs((10, 0.9f), (15, 0.1f), (10, 0.9f), (15, 0.1f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, probs.Count * 512L, padMs: 0);

        Assert.Equal(2, segs.Count);
        Assert.Equal((0L, 10 * 512L), segs[0]);
        Assert.Equal((25 * 512L, 35 * 512L), segs[1]);
    }

    [Fact]
    public void SpeechSegments_drops_speech_shorter_than_min_speech()
    {
        // 5 janelas = 2560 amostras < 250 ms (4000); 10 janelas = 5120 ≥ 4000.
        var probs = Probs((5, 0.9f), (20, 0.1f), (10, 0.9f), (20, 0.1f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, probs.Count * 512L, padMs: 0);

        Assert.Single(segs);
        Assert.Equal(25 * 512L, segs[0].Start);
        Assert.Equal(35 * 512L, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_pads_both_sides_and_clamps_to_audio()
    {
        // Fala de 10 a 30 janelas; padding de 60 ms = 960 amostras.
        var probs = Probs((10, 0.1f), (20, 0.9f), (40, 0.1f));
        var total = probs.Count * 512L;
        var segs = SpeechSegments.FromProbabilities(probs, 512, total, padMs: 60);

        Assert.Single(segs);
        Assert.Equal(10 * 512L - 960, segs[0].Start);
        Assert.Equal(30 * 512L + 960, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_padding_clamps_at_start_and_end()
    {
        var probs = Probs((20, 0.9f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, 20 * 512L - 100, padMs: 60);

        Assert.Single(segs);
        Assert.Equal(0, segs[0].Start);
        Assert.Equal(20 * 512L - 100, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_overlapping_padding_is_merged()
    {
        // Com padding de 300 ms (4800 amostras) as duas falas separadas por 4800 amostras se sobrepõem.
        var probs = Probs((10, 0.9f), (15, 0.1f), (10, 0.9f), (15, 0.1f));
        var segs = SpeechSegments.FromProbabilities(probs, 512, probs.Count * 512L, padMs: 300);

        // Fala 2 termina na janela 35 (17920); com 300 ms de padding: 17920 + 4800.
        Assert.Single(segs);
        Assert.Equal(0, segs[0].Start);
        Assert.Equal(17920 + 4800, segs[0].End);
    }

    [Fact]
    public void SpeechSegments_empty_or_below_threshold_gives_nothing()
    {
        Assert.Empty(SpeechSegments.FromProbabilities(Array.Empty<float>(), 512, 0));
        var below = Probs((40, 0.6f));
        Assert.Empty(SpeechSegments.FromProbabilities(below, 512, below.Count * 512L, threshold: 0.7f));
    }

    [Theory]
    [InlineData("  olá   mundo \n", "olá mundo")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData("[Música]", "")]
    [InlineData("(risos)", "")]
    [InlineData("*music*", "")]
    [InlineData("[ Silence ]", "")]
    [InlineData("(risos) [aplausos]", "")]
    [InlineData("[Música] Olá", "[Música] Olá")]
    [InlineData("Oi (risos) tudo bem", "Oi (risos) tudo bem")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void TranscriptCleanup_cases(string input, string expected) =>
        Assert.Equal(expected, TranscriptCleanup.Clean(input));

    [Fact]
    public void TranscriptCleanup_null_gives_empty() =>
        Assert.Equal("", TranscriptCleanup.Clean(null));

    [ModelFact("silero_vad.onnx")]
    public void SileroVad_silence_has_low_probabilities_and_expected_length()
    {
        using var session = OnnxSessionFactory.CreateCpu(TestModels.Require("silero_vad.onnx"));
        var vad = new SileroVad(session.Session);

        var probs = vad.Probabilities(new float[16000], null, default);

        Assert.Equal((16000 + 511) / 512, probs.Length);
        Assert.All(probs, p => Assert.True(p < 0.3f, $"probabilidade {p}"));
    }

    [ModelFact("silero_vad.onnx")]
    public void SileroVad_runs_on_synthetic_harmonic_signal()
    {
        using var session = OnnxSessionFactory.CreateCpu(TestModels.Require("silero_vad.onnx"));
        var vad = new SileroVad(session.Session);
        var x = new float[32000 + 100];
        for (var i = 0; i < x.Length; i++)
        {
            double t = i / 16000.0, sum = 0;
            for (var h = 1; h <= 10; h++) sum += Math.Sin(2 * Math.PI * 120 * h * t) / h;
            x[i] = (float)(0.3 * sum * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * t)));
        }

        var probs = vad.Probabilities(x, null, default);

        Assert.Equal((x.Length + 511) / 512, probs.Length);
        Assert.All(probs, p => Assert.True(p >= 0 && p <= 1 && !float.IsNaN(p)));
    }

    [ModelFact("gtcrn_simple.onnx")]
    public void GtcrnEnhancer_reduces_white_noise_and_keeps_length()
    {
        using var session = OnnxSessionFactory.CreateCpu(TestModels.Require("gtcrn_simple.onnx"));
        var rnd = new Random(42);
        var noise = new float[32000];
        for (var i = 0; i < noise.Length; i++) noise[i] = (float)((rnd.NextDouble() * 2 - 1) * 0.1);

        var output = new GtcrnEnhancer(session.Session).Enhance(noise, null, default);

        Assert.Equal(noise.Length, output.Length);
        Assert.True(Rms(output) < 0.5 * Rms(noise), $"RMS saída {Rms(output):F5} vs entrada {Rms(noise):F5}");
    }

    // ---------------------------------------------------------------- preparação para o Whisper

    [Fact]
    public void Silencio_nao_gera_blocos_para_o_Whisper()
    {
        var silence = new float[5 * 16000];
        var probs = SpeechChunks.EnergyProbabilities(silence, SileroVad.WindowSamples);
        var regions = SpeechSegments.FromProbabilities(probs, SileroVad.WindowSamples, silence.Length, 0.5f, 16000, 200, 500, 250);
        Assert.Empty(SpeechChunks.Group(regions, 16000));
    }

    [Fact]
    public void Detector_por_energia_mantem_fala_com_ruido_e_descarta_o_silencio()
    {
        // 2 s de silêncio, 3 s de "fala" (tom) com ruído, 2 s de silêncio.
        var rng = new Random(3);
        var audio = new float[7 * 16000];
        for (var i = 2 * 16000; i < 5 * 16000; i++)
            audio[i] = 0.3f * MathF.Sin(2 * MathF.PI * 220 * i / 16000) + 0.05f * (float)(rng.NextDouble() - 0.5);
        var probs = SpeechChunks.EnergyProbabilities(audio, SileroVad.WindowSamples);
        var regions = SpeechSegments.FromProbabilities(probs, SileroVad.WindowSamples, audio.Length, 0.5f, 16000, 200, 500, 250);
        var chunk = Assert.Single(SpeechChunks.Group(regions, 16000));
        Assert.InRange(chunk.Start / 16000.0, 1.6, 2.0);
        Assert.InRange(chunk.End / 16000.0, 5.0, 5.4);
    }

    [Fact]
    public void Blocos_juntam_pausas_curtas_e_respeitam_o_tamanho_maximo()
    {
        const int r = 16000;
        var regions = new List<(long, long)> { (0, 2 * r), (3 * r, 5 * r), (20 * r, 22 * r), (24 * r, 50 * r), (60 * r, 61 * r) };
        var chunks = SpeechChunks.Group(regions, r);
        Assert.Equal([(0L, 5L * r), (20L * r, 22L * r), (24L * r, 50L * r), (60L * r, 61L * r)], chunks);
    }
}
