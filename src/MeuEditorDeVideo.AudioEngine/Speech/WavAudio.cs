// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Runtime.InteropServices;
using System.Text;

namespace MeuEditorDeVideo.AudioEngine.Speech;

/// <summary>Áudio PCM de 16 bits intercalado lido de um WAV.</summary>
public sealed record WavAudio(int SampleRate, int Channels, short[] Samples)
{
    /// <summary>Lê um WAV PCM de 16 bits (inclusive WAVE_FORMAT_EXTENSIBLE), ignorando blocos desconhecidos e tolerando tamanho de dados inválido.</summary>
    public static WavAudio Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("O arquivo de áudio gerado não é um WAV válido.");
        int pos = 12, rate = 0, channels = 0, bits = 0, tag = 0;
        var haveFormat = false;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = BitConverter.ToUInt32(data.Slice(pos + 4, 4));
            var body = pos + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > data.Length) throw new InvalidDataException("Bloco 'fmt' do WAV inválido.");
                tag = BitConverter.ToUInt16(data.Slice(body, 2));
                channels = BitConverter.ToUInt16(data.Slice(body + 2, 2));
                rate = (int)BitConverter.ToUInt32(data.Slice(body + 4, 4));
                bits = BitConverter.ToUInt16(data.Slice(body + 14, 2));
                if (tag == 0xFFFE && size >= 26 && body + 26 <= data.Length) tag = BitConverter.ToUInt16(data.Slice(body + 24, 2));
                haveFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (!haveFormat) throw new InvalidDataException("O bloco 'data' do WAV veio antes do 'fmt'.");
                if (tag != 1 || bits != 16 || channels < 1 || rate < 1) throw new InvalidDataException("Só há suporte a WAV PCM de 16 bits.");
                var length = (int)Math.Min(size, (uint)(data.Length - body));
                length -= length % (2 * channels);
                var samples = new short[length / 2];
                MemoryMarshal.Cast<byte, short>(data.Slice(body, length)).CopyTo(samples);
                return new WavAudio(rate, channels, samples);
            }
            var next = (long)body + size + (size & 1);
            if (next > data.Length) break;
            pos = (int)next;
        }
        throw new InvalidDataException("O WAV não tem dados de áudio.");
    }

    /// <summary>Serializa como WAV PCM de 16 bits com o cabeçalho canônico de 44 bytes.</summary>
    public byte[] ToBytes()
    {
        var dataLength = Samples.Length * 2;
        using var ms = new MemoryStream(44 + dataLength);
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + dataLength);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)Channels);
            w.Write(SampleRate);
            w.Write(SampleRate * Channels * 2);
            w.Write((short)(Channels * 2));
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(dataLength);
            foreach (var s in Samples) w.Write(s);
        }
        return ms.ToArray();
    }

    /// <summary>Mistura os canais em mono (média).</summary>
    public WavAudio ToMono()
    {
        if (Channels == 1) return this;
        var frames = Samples.Length / Channels;
        var mono = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            for (var c = 0; c < Channels; c++) sum += Samples[i * Channels + c];
            mono[i] = (short)(sum / Channels);
        }
        return new WavAudio(SampleRate, 1, mono);
    }

    /// <summary>Converte para mono 16 bits na taxa pedida.</summary>
    public WavAudio ToMono(int targetRate)
    {
        var mono = ToMono();
        return mono.SampleRate == targetRate ? mono : new WavAudio(targetRate, 1, AudioResampler.Resample(mono.Samples, mono.SampleRate, targetRate));
    }
}

/// <summary>Reamostragem de boa qualidade: sinc com janela de Blackman, com passa-baixas quando a taxa diminui.</summary>
public static class AudioResampler
{
    private const int HalfTaps = 16;

    public static short[] Resample(ReadOnlySpan<short> input, int inRate, int outRate)
    {
        if (inRate <= 0 || outRate <= 0) throw new ArgumentOutOfRangeException(nameof(inRate), "Taxa de amostragem inválida.");
        if (inRate == outRate) return input.ToArray();
        if (input.Length == 0) return [];

        var outLength = (int)((long)input.Length * outRate / inRate);
        var output = new short[outLength];
        // Fração do espectro de entrada que se mantém: 1 ao aumentar a taxa; outRate/inRate ao reduzir (evita aliasing).
        var cutoff = Math.Min(1.0, (double)outRate / inRate);
        var support = HalfTaps / cutoff;
        var gcd = Gcd(inRate, outRate);
        var phases = outRate / gcd;
        var step = inRate / gcd;
        var firstOffset = -(int)Math.Ceiling(support) + 1;
        var width = (int)Math.Ceiling(support) * 2;
        // Uma fase repete a cada 'phases' amostras de saída, então os núcleos são calculados uma vez por fase.
        var kernels = new float[Math.Min(phases, outLength)][];

        for (var n = 0; n < outLength; n++)
        {
            var pos = (long)n * step;
            var baseIndex = (int)(pos / phases);
            var phase = (int)(pos % phases);
            var slot = n % kernels.Length;
            var kernel = kernels[slot];
            kernel ??= kernels[slot] = BuildKernel((double)phase / phases, width, firstOffset, cutoff, support);

            double acc = 0;
            for (var j = 0; j < width; j++)
            {
                var idx = baseIndex + firstOffset + j;
                if ((uint)idx < (uint)input.Length) acc += input[idx] * kernel[j];
            }
            output[n] = (short)Math.Clamp((int)Math.Round(acc), short.MinValue, short.MaxValue);
        }
        return output;
    }

    private static float[] BuildKernel(double frac, int width, int firstOffset, double cutoff, double support)
    {
        var k = new double[width];
        double sum = 0;
        for (var j = 0; j < width; j++)
        {
            var d = firstOffset + j - frac;
            k[j] = Sinc(d * cutoff) * Blackman(d / support);
            sum += k[j];
        }
        var result = new float[width];
        for (var j = 0; j < width; j++) result[j] = (float)(sum != 0 ? k[j] / sum : k[j]);
        return result;
    }

    private static double Sinc(double x) => Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    private static double Blackman(double x) =>
        Math.Abs(x) >= 1 ? 0 : 0.42 + 0.5 * Math.Cos(Math.PI * x) + 0.08 * Math.Cos(2 * Math.PI * x);

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}
