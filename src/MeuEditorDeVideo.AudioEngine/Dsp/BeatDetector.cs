// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
namespace MeuEditorDeVideo.AudioEngine.Dsp;

/// <summary>
/// Detecção de batidas (onsets): fluxo espectral em bandas (FFT de 1024 amostras, passo de ~10,7 ms) com limiar adaptativo
/// (mediana local + fração do desvio) e intervalo mínimo entre batidas. Recebe áudio estéreo intercalado a 48 kHz.
/// </summary>
public sealed class BeatDetector
{
    public const int Rate = 48000;
    public const int Size = 1024;
    public const int Hop = 512;

    private readonly float[] _mono = new float[Size];
    private readonly double[] _prev = new double[Size / 2];
    private readonly List<double> _flux = [];
    private int _filled;
    private int _sinceHop;
    private int _write;

    public void Add(ReadOnlySpan<float> stereo)
    {
        for (var i = 0; i + 1 < stereo.Length; i += 2)
        {
            // Buffer circular: _write aponta para a amostra mais antiga.
            _mono[_write] = (stereo[i] + stereo[i + 1]) * 0.5f;
            _write = (_write + 1) % Size;
            if (_filled < Size) _filled++;
            if (_filled == Size && ++_sinceHop >= Hop)
            {
                _sinceHop = 0;
                Frame();
            }
        }
    }

    private void Frame()
    {
        var re = new double[Size];
        var im = new double[Size];
        for (var i = 0; i < Size; i++)
            re[i] = _mono[(_write + i) % Size] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1)));
        Fft.Transform(re, im);
        var flux = 0.0;
        for (var k = 1; k < Size / 2; k++)
        {
            var mag = Math.Log(1 + 100 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
            var d = mag - _prev[k];
            if (d > 0) flux += k < Size / 16 ? d * 2 : d; // graves (bumbo) pesam mais
            _prev[k] = mag;
        }
        _flux.Add(flux);
    }

    /// <summary>
    /// Instantes das batidas, em quadros de áudio desde o início. <paramref name="sensitivity"/> (0…1): maior = mais batidas.
    /// </summary>
    public IReadOnlyList<long> Detect(double sensitivity = 0.5, double minIntervalSeconds = 0.25)
    {
        var result = new List<long>();
        if (_flux.Count < 3) return result;
        var window = (int)(Rate / (double)Hop * 0.5); // 0,5 s de cada lado
        var k = 1.6 - 1.4 * Math.Clamp(sensitivity, 0, 1);
        var minGap = (long)(minIntervalSeconds * Rate);
        var max = _flux.Skip(1).Max(); // o primeiro quadro compara com silêncio
        if (max <= 0) return result;
        long last = long.MinValue / 2;
        for (var i = 1; i < _flux.Count - 1; i++)
        {
            var v = _flux[i];
            if (v < _flux[i - 1] || v < _flux[i + 1] || v < max * 0.05) continue;
            var from = Math.Max(0, i - window);
            var to = Math.Min(_flux.Count, i + window + 1);
            var local = _flux.GetRange(from, to - from);
            var mean = local.Average();
            var std = Math.Sqrt(local.Average(x => (x - mean) * (x - mean)));
            if (v < mean + k * std) continue;
            // O pico do quadro está no centro da janela de análise.
            var frame = (long)(i + 1) * Hop + Size / 2;
            if (frame - last < minGap) continue;
            result.Add(frame);
            last = frame;
        }
        return result;
    }

    public static IReadOnlyList<long> Detect(ReadOnlySpan<float> stereo, double sensitivity = 0.5)
    {
        var d = new BeatDetector();
        d.Add(stereo);
        return d.Detect(sensitivity);
    }
}
