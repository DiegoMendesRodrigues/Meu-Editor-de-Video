// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using System.Globalization;
using MeuEditorDeVideo.AudioEngine.Speech;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class SpeechSynthesisParsingTests
{
    private const string SayOutput =
        "Alex                en_US    # Most people recognize me by my voice.\n" +
        "Eddy (Português (Brasil)) pt_BR    # Olá, meu nome é Eddy.\n" +
        "Luciana             pt_BR    # Olá, meu nome é Luciana.\n" +
        "Eddy (English (US)) en_US    # Hello, my name is Eddy.\n" +
        "Reed (Español (España)) es_ES    # Hola # com cerquilha\n" +
        "Linha sem idioma\n" +
        "\n";

    private const string EspeakOutput =
        "Pty Language       Age/Gender VoiceName          File                 Other Languages\n" +
        " 5  af             --/M      Afrikaans          gmw/af\n" +
        " 5  en-gb          --/M      English_(Great_Britain) gmw/en               (en 2)\n" +
        " 5  en-gb-x-rp     --/M      English_(Received_Pronunciation) gmw/en-GB-x-rp  (en-gb 2)(en 5)\n" +
        " 5  pt-br          --/M      Portuguese_(Brazil) roa/pt-BR            (pt 5)\n" +
        " 5  pt             --/M      Portuguese_(Portugal) roa/pt               (pt-pt 5)\n" +
        " 5  pt-br          --/F      mbrola_pt          mb/mb-br1\n";

    [Fact]
    public void ParseSayVoices_reads_names_with_spaces_and_parentheses()
    {
        var voices = SpeechParsing.ParseSayVoices(SayOutput);
        Assert.Equal(5, voices.Count);
        Assert.Equal("Alex", voices[0].Name);
        Assert.Equal("en_US", voices[0].Language);
        Assert.Equal("Alex (en_US)", voices[0].Display);
        Assert.Equal("Eddy (Português (Brasil))", voices[1].Name);
        Assert.Equal("pt_BR", voices[1].Language);
        Assert.Equal("Eddy (Português (Brasil)) (pt_BR)", voices[1].Display);
        Assert.Equal("Reed (Español (España))", voices[4].Name);
    }

    [Fact]
    public void ParseEspeakVoices_reads_table_and_skips_header_and_mbrola()
    {
        var voices = SpeechParsing.ParseEspeakVoices(EspeakOutput);
        Assert.Equal(["af", "en-gb", "en-gb-x-rp", "pt-br", "pt"], voices.Select(v => v.Name));
        Assert.Equal("Portuguese (Brazil) (pt-br)", voices.Single(v => v.Name == "pt-br").Display);
        Assert.Equal("English (Received Pronunciation) (en-gb-x-rp)", voices.Single(v => v.Name == "en-gb-x-rp").Display);
    }

    [Fact]
    public void ResolveVoice_accepts_display_text_or_plain_name_and_rejects_unknown()
    {
        var voices = SpeechParsing.ParseSayVoices(SayOutput);
        Assert.Equal("Eddy (Português (Brasil))", SpeechParsing.ResolveVoice(voices, "Eddy (Português (Brasil)) (pt_BR)"));
        Assert.Equal("Luciana", SpeechParsing.ResolveVoice(voices, "Luciana"));
        Assert.Null(SpeechParsing.ResolveVoice(voices, "Inexistente (xx_XX)"));
        Assert.Null(SpeechParsing.ResolveVoice(voices, null));
        Assert.Null(SpeechParsing.ResolveVoice(voices, "  "));
    }

    [Fact]
    public void OrderForCulture_puts_same_language_first_and_same_region_before_other_regions()
    {
        var voices = SpeechParsing.ParseEspeakVoices(EspeakOutput);
        var ordered = SpeechParsing.OrderForCulture(voices, new CultureInfo("pt-BR"));
        Assert.Equal("pt-br", ordered[0].Name);
        Assert.Equal("pt", ordered[1].Name);
        Assert.DoesNotContain(ordered.Take(2), v => v.Name.StartsWith("en", StringComparison.Ordinal));
        Assert.Equal(voices.Count, ordered.Count);
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(10, 360)]
    [InlineData(-10, 90)]
    [InlineData(5, 270)]
    [InlineData(99, 360)]
    [InlineData(-99, 90)]
    public void RateToSayWpm_maps_and_clamps(int rate, int expected) => Assert.Equal(expected, SpeechParsing.RateToSayWpm(rate));

    [Theory]
    [InlineData(0, 175)]
    [InlineData(10, 350)]
    [InlineData(-10, 88)]
    public void RateToEspeakWpm_maps_and_clamps(int rate, int expected) => Assert.Equal(expected, SpeechParsing.RateToEspeakWpm(rate));

    [Fact]
    public void Wav_roundtrip_keeps_format_and_samples()
    {
        short[] samples = [0, 1000, -1000, short.MaxValue, short.MinValue, 42];
        var bytes = new WavAudio(22050, 1, samples).ToBytes();
        Assert.Equal(44 + samples.Length * 2, bytes.Length);
        var back = WavAudio.Parse(bytes);
        Assert.Equal(22050, back.SampleRate);
        Assert.Equal(1, back.Channels);
        Assert.Equal(samples, back.Samples);
    }

    [Fact]
    public void Wav_parse_tolerates_unknown_chunks_and_oversized_data_length()
    {
        var bytes = new WavAudio(8000, 1, [1, 2, 3, 4]).ToBytes().ToList();
        // Insere um bloco "LIST" (3 bytes + 1 de preenchimento) antes de "data" e declara o tamanho dos dados como 0xFFFFFFFF.
        byte[] list = [(byte)'L', (byte)'I', (byte)'S', (byte)'T', 3, 0, 0, 0, 1, 2, 3, 0];
        bytes.InsertRange(36, list);
        var dataSizeAt = 36 + list.Length + 4;
        for (var i = 0; i < 4; i++) bytes[dataSizeAt + i] = 0xFF;
        var parsed = WavAudio.Parse(bytes.ToArray());
        Assert.Equal([1, 2, 3, 4], parsed.Samples);
    }

    [Fact]
    public void Wav_parse_rejects_non_wav_and_non_pcm16()
    {
        Assert.Throws<InvalidDataException>(() => WavAudio.Parse(new byte[] { 1, 2, 3 }));
        var bytes = new WavAudio(8000, 1, [1, 2]).ToBytes();
        bytes[34] = 8; // 8 bits por amostra
        Assert.Throws<InvalidDataException>(() => WavAudio.Parse(bytes));
    }

    [Fact]
    public void ToMono_averages_channels()
    {
        var stereo = new WavAudio(48000, 2, [100, 300, -200, -400]);
        Assert.Equal([200, -300], stereo.ToMono().Samples);
    }

    [Theory]
    [InlineData(22050, 48000)]
    [InlineData(16000, 48000)]
    [InlineData(48000, 22050)]
    public void Resample_keeps_length_and_preserves_a_tone(int inRate, int outRate)
    {
        const double freq = 1000;
        var input = Enumerable.Range(0, inRate).Select(i => (short)(12000 * Math.Sin(2 * Math.PI * freq * i / inRate))).ToArray();
        var output = AudioResampler.Resample(input, inRate, outRate);
        Assert.Equal(outRate, output.Length);
        // Compara com o tom ideal na nova taxa, longe das bordas (onde o filtro não tem vizinhança completa).
        double err = 0, power = 0;
        for (var i = 100; i < output.Length - 100; i++)
        {
            var ideal = 12000 * Math.Sin(2 * Math.PI * freq * i / outRate);
            err += (output[i] - ideal) * (output[i] - ideal);
            power += ideal * ideal;
        }
        Assert.True(err / power < 1e-3, $"erro relativo {err / power}");
    }

    [Fact]
    public void Resample_downsampling_removes_content_above_the_new_nyquist()
    {
        // 20 kHz a 48 kHz reduzido para 22,05 kHz não pode virar um tom alias audível.
        var input = Enumerable.Range(0, 48000).Select(i => (short)(12000 * Math.Sin(2 * Math.PI * 20000 * i / 48000))).ToArray();
        var output = AudioResampler.Resample(input, 48000, 22050);
        var rms = Math.Sqrt(output.Skip(100).Take(output.Length - 200).Average(s => (double)s * s));
        Assert.True(rms < 500, $"rms {rms}");
    }

    [Fact]
    public void Resample_same_rate_and_empty_input()
    {
        short[] x = [1, 2, 3];
        Assert.Equal(x, AudioResampler.Resample(x, 48000, 48000));
        Assert.Empty(AudioResampler.Resample([], 22050, 48000));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioResampler.Resample(x, 0, 48000));
    }

    [Fact]
    public void ToMono_with_target_rate_outputs_48k_mono()
    {
        var source = new WavAudio(22050, 2, new short[22050 * 2]);
        var result = source.ToMono(48000);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.Equal(48000, result.Samples.Length);
    }

    [Fact]
    public void Linux_and_mac_services_report_missing_tool_with_clear_message()
    {
        Assert.Equal("Instale o pacote espeak-ng para usar o texto para fala.", new LinuxSpeechSynthesisService().UnavailableReason);
        Assert.Contains("say", new MacSpeechSynthesisService().UnavailableReason);
    }
}
