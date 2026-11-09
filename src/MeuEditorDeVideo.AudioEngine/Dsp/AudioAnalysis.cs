namespace MeuEditorDeVideo.AudioEngine.Dsp;

/// <summary>
/// Medidor de loudness integrado (ITU-R BS.1770-4 / EBU R128) para 48 kHz estéreo: ponderação K, blocos de 400 ms
/// com 75% de sobreposição, comporta absoluta de −70 LUFS e relativa de −10 LU. Também registra o pico amostral.
/// </summary>
public sealed class LoudnessMeter
{
    private const int Rate = 48000;
    private const int Step = Rate / 10;    // 100 ms
    private const int BlockSteps = 4;      // 400 ms

    private readonly Biquad _shelf = Biquad.FromCoefficients(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
    private readonly Biquad _highPass = Biquad.FromCoefficients(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
    private readonly List<double> _stepPower = [];
    private readonly List<double> _blocks = [];
    private double _acc;
    private int _inStep;
    private float _peak;

    public void Add(ReadOnlySpan<float> stereo)
    {
        for (var i = 0; i + 1 < stereo.Length; i += 2)
        {
            _peak = Math.Max(_peak, Math.Max(Math.Abs(stereo[i]), Math.Abs(stereo[i + 1])));
            var l = _highPass.ProcessMono(_shelf.ProcessMono(stereo[i]));
            var r = _highPass.ProcessMonoRight(_shelf.ProcessMonoRight(stereo[i + 1]));
            _acc += l * l + r * r;
            if (++_inStep == Step)
            {
                _stepPower.Add(_acc / Step);
                _acc = 0;
                _inStep = 0;
                if (_stepPower.Count >= BlockSteps)
                {
                    var sum = 0.0;
                    for (var k = _stepPower.Count - BlockSteps; k < _stepPower.Count; k++) sum += _stepPower[k];
                    _blocks.Add(sum / BlockSteps);
                }
            }
        }
    }

    private static double Lufs(double power) => power <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(power);

    /// <summary>Loudness integrado (LUFS); −∞ se não houver blocos acima da comporta absoluta.</summary>
    public double IntegratedLufs
    {
        get
        {
            var gated = _blocks.Where(p => Lufs(p) > -70).ToList();
            if (gated.Count == 0) return double.NegativeInfinity;
            var relative = Lufs(gated.Average()) - 10;
            var final = gated.Where(p => Lufs(p) > relative).ToList();
            return final.Count == 0 ? double.NegativeInfinity : Lufs(final.Average());
        }
    }

    /// <summary>Pico amostral (dBFS).</summary>
    public double PeakDbfs => _peak <= 1e-9 ? double.NegativeInfinity : 20 * Math.Log10(_peak);

    /// <summary>Ganho (dB) para levar ao alvo sem que o pico passe de <paramref name="peakCeilingDb"/>.</summary>
    public static double NormalizationGainDb(double integratedLufs, double peakDbfs, double targetLufs, double peakCeilingDb = -1)
    {
        if (double.IsNegativeInfinity(integratedLufs)) return 0;
        var gain = targetLufs - integratedLufs;
        if (!double.IsNegativeInfinity(peakDbfs)) gain = Math.Min(gain, peakCeilingDb - peakDbfs);
        return gain;
    }
}

/// <summary>
/// Detecção de silêncios (RF128) por RMS em janelas de 10 ms (canal mais alto). Acumula os níveis por janela,
/// permitindo analisar mídias longas em blocos sem guardar as amostras.
/// </summary>
public sealed class SilenceDetector
{
    public const int Rate = 48000;
    public const int Window = Rate / 100;

    private readonly List<double> _levels = [];
    private double _sl, _sr;
    private int _inWindow;
    private long _frames;

    public long Frames => _frames;

    public void Add(ReadOnlySpan<float> stereo)
    {
        for (var i = 0; i + 1 < stereo.Length; i += 2)
        {
            _sl += stereo[i] * stereo[i];
            _sr += stereo[i + 1] * stereo[i + 1];
            _frames++;
            if (++_inWindow == Window) Flush();
        }
    }

    private void Flush()
    {
        if (_inWindow == 0) return;
        _levels.Add(Math.Sqrt(Math.Max(_sl, _sr) / _inWindow));
        _sl = _sr = 0;
        _inWindow = 0;
    }

    /// <summary>
    /// Intervalos silenciosos (em quadros desde o início) com duração mínima <paramref name="minFrames"/>; cada intervalo
    /// é encolhido por <paramref name="paddingFrames"/> de cada lado, preservando respirações e o início/fim das falas.
    /// Silêncios nas pontas não são encolhidos do lado da ponta.
    /// </summary>
    public IReadOnlyList<(long Start, long End)> Detect(double thresholdDb, long minFrames, long paddingFrames)
    {
        Flush();
        var threshold = Math.Pow(10, thresholdDb / 20);
        var result = new List<(long, long)>();
        long runStart = -1;
        for (var w = 0; w < _levels.Count; w++)
        {
            long from = (long)w * Window;
            if (_levels[w] < threshold)
            {
                if (runStart < 0) runStart = from;
            }
            else if (runStart >= 0)
            {
                Add(runStart, from);
                runStart = -1;
            }
        }
        if (runStart >= 0) Add(runStart, _frames);
        return result;

        void Add(long s, long e)
        {
            if (e - s < minFrames) return;
            var ps = s == 0 ? s : s + paddingFrames;
            var pe = e >= _frames ? _frames : e - paddingFrames;
            if (pe > ps) result.Add((ps, pe));
        }
    }

    public static IReadOnlyList<(long Start, long End)> Detect(ReadOnlySpan<float> stereo, double thresholdDb, long minFrames, long paddingFrames)
    {
        var d = new SilenceDetector();
        d.Add(stereo);
        return d.Detect(thresholdDb, minFrames, paddingFrames);
    }
}

/// <summary>Sincronização por áudio (RF129): correlação cruzada via FFT de versões mono a 8 kHz.</summary>
public static class AudioCorrelator
{
    public const int AnalysisRate = 8000;
    private const int Decimation = 48000 / AnalysisRate;

    /// <summary>Converte estéreo 48 kHz em mono 8 kHz (média em caixa como passa-baixas antes da dizimação).</summary>
    public static double[] ToAnalysis(ReadOnlySpan<float> stereo)
    {
        var frames = stereo.Length / 2;
        var result = new double[frames / Decimation];
        for (var i = 0; i < result.Length; i++)
        {
            double sum = 0;
            for (var k = 0; k < Decimation; k++)
            {
                var f = (i * Decimation + k) * 2;
                sum += stereo[f] + stereo[f + 1];
            }
            result[i] = sum / (2 * Decimation);
        }
        // Remove o nível médio (offset DC atrapalha a correlação).
        if (result.Length > 0)
        {
            var mean = result.Average();
            for (var i = 0; i < result.Length; i++) result[i] -= mean;
        }
        return result;
    }

    /// <summary>
    /// Deslocamento (em amostras de análise) que alinha <paramref name="other"/> a <paramref name="reference"/>:
    /// o conteúdo de other[m] corresponde a reference[m + lag]. Confiança = correlação normalizada no pico (0…1).
    /// </summary>
    public static (int Lag, double Confidence) FindLag(double[] reference, double[] other, int maxLag)
    {
        if (reference.Length == 0 || other.Length == 0) return (0, 0);
        var n = Fft.NextPowerOfTwo(reference.Length + other.Length);
        var ar = new double[n];
        var ai = new double[n];
        var br = new double[n];
        var bi = new double[n];
        Array.Copy(reference, ar, reference.Length);
        Array.Copy(other, br, other.Length);
        Fft.Transform(ar, ai);
        Fft.Transform(br, bi);
        // R(lag) = Σ ref[m + lag]·other[m] → IFFT(A · conj(B)).
        for (var k = 0; k < n; k++)
        {
            var re = ar[k] * br[k] + ai[k] * bi[k];
            var im = ai[k] * br[k] - ar[k] * bi[k];
            ar[k] = re;
            ai[k] = im;
        }
        Fft.Transform(ar, ai, inverse: true);

        var best = 0;
        var bestValue = double.NegativeInfinity;
        for (var lag = -Math.Min(maxLag, other.Length - 1); lag <= Math.Min(maxLag, reference.Length - 1); lag++)
        {
            var v = ar[(lag + n) % n];
            if (v > bestValue)
            {
                bestValue = v;
                best = lag;
            }
        }

        // Normaliza pela energia da região sobreposta.
        double er = 0, eo = 0;
        for (var m = 0; m < other.Length; m++)
        {
            var r = m + best;
            if (r < 0 || r >= reference.Length) continue;
            er += reference[r] * reference[r];
            eo += other[m] * other[m];
        }
        var norm = Math.Sqrt(er * eo);
        return (best, norm < 1e-12 ? 0 : Math.Clamp(bestValue / norm, 0, 1));
    }
}
