using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.AudioEngine.Dsp;

/// <summary>
/// Processador de áudio com estado, aplicado em blocos sequenciais de amostras estéreo intercaladas (48 kHz).
/// Após uma descontinuidade (busca), a cadeia é reiniciada e alimentada com <see cref="PreRollFrames"/> quadros
/// anteriores, descartando a saída, para que filtros, envelopes e caudas cheguem ao estado correto.
/// </summary>
public interface IAudioProcessor
{
    /// <summary>Atraso (em quadros) entre entrada e saída.</summary>
    int LatencyFrames { get; }

    /// <summary>Quadros de aquecimento recomendados após reiniciar.</summary>
    int PreRollFrames { get; }

    /// <summary>Atualiza os parâmetros sem perder o estado (arrastar um controle durante a reprodução).</summary>
    void Configure(EffectInstance effect);

    void Process(Span<float> samples);

    void Reset();
}

public static class AudioProcessorFactory
{
    public const int Rate = 48000;

    public static IAudioProcessor? Create(EffectInstance effect)
    {
        IAudioProcessor? p = effect.Type switch
        {
            EffectCatalog.Equalizer => new EqualizerProcessor(),
            EffectCatalog.Compressor => new CompressorProcessor(),
            EffectCatalog.Denoise => new SpectralDenoiser(),
            EffectCatalog.Echo => new EchoProcessor(),
            EffectCatalog.Reverb => new ReverbProcessor(),
            EffectCatalog.Telephone => new TelephoneProcessor(),
            EffectCatalog.Distortion => new DistortionProcessor(),
            _ => null,
        };
        p?.Configure(effect);
        return p;
    }
}

/// <summary>Equalizador paramétrico: passa-altas (corte de graves), prateleira de graves, pico de médios e prateleira de agudos (RF123).</summary>
public sealed class EqualizerProcessor : IAudioProcessor
{
    private readonly Biquad _lowCut1 = new(), _lowCut2 = new(), _low = new(), _mid = new(), _high = new();
    private bool _cut, _hasLow, _hasMid, _hasHigh;

    public int LatencyFrames => 0;
    public int PreRollFrames => AudioProcessorFactory.Rate / 20;

    public void Configure(EffectInstance e)
    {
        const int r = AudioProcessorFactory.Rate;
        var cut = EffectCatalog.Value(e, "lowcut");
        _cut = cut >= 10;
        if (_cut)
        {
            // Butterworth de 4ª ordem (dois estágios, Q de Butterworth).
            _lowCut1.SetPass(cut, 0.5412, r, high: true);
            _lowCut2.SetPass(cut, 1.3066, r, high: true);
        }
        var lg = EffectCatalog.Value(e, "lowGain");
        var mg = EffectCatalog.Value(e, "midGain");
        var hg = EffectCatalog.Value(e, "highGain");
        _hasLow = Math.Abs(lg) > 0.01;
        _hasMid = Math.Abs(mg) > 0.01;
        _hasHigh = Math.Abs(hg) > 0.01;
        _low.SetShelf(EffectCatalog.Value(e, "lowFreq"), lg, r, high: false);
        _mid.SetPeaking(EffectCatalog.Value(e, "midFreq"), mg, EffectCatalog.Value(e, "midQ"), r);
        _high.SetShelf(EffectCatalog.Value(e, "highFreq"), hg, r, high: true);
    }

    public void Process(Span<float> s)
    {
        if (_cut)
        {
            _lowCut1.Process(s);
            _lowCut2.Process(s);
        }
        if (_hasLow) _low.Process(s);
        if (_hasMid) _mid.Process(s);
        if (_hasHigh) _high.Process(s);
    }

    public void Reset()
    {
        _lowCut1.Reset();
        _lowCut2.Reset();
        _low.Reset();
        _mid.Reset();
        _high.Reset();
    }

    /// <summary>Resposta total (dB) do equalizador na frequência, para desenhar a curva.</summary>
    public double MagnitudeDb(double freq)
    {
        const int r = AudioProcessorFactory.Rate;
        var db = 0.0;
        if (_cut) db += _lowCut1.MagnitudeDb(freq, r) + _lowCut2.MagnitudeDb(freq, r);
        if (_hasLow) db += _low.MagnitudeDb(freq, r);
        if (_hasMid) db += _mid.MagnitudeDb(freq, r);
        if (_hasHigh) db += _high.MagnitudeDb(freq, r);
        return db;
    }
}

/// <summary>Compressor com detector de pico estéreo vinculado, joelho suave de 6 dB e ganho de compensação (RF125).</summary>
public sealed class CompressorProcessor : IAudioProcessor
{
    private const double Knee = 6;
    private double _threshold, _ratio, _attack, _release, _makeup;
    private double _envDb;

    public int LatencyFrames => 0;
    public int PreRollFrames => AudioProcessorFactory.Rate / 2;

    /// <summary>Redução de ganho atual (dB, ≥ 0), para medidores.</summary>
    public double GainReductionDb { get; private set; }

    public void Configure(EffectInstance e)
    {
        const double r = AudioProcessorFactory.Rate;
        _threshold = EffectCatalog.Value(e, "threshold");
        _ratio = Math.Max(1, EffectCatalog.Value(e, "ratio"));
        _attack = Math.Exp(-1 / (Math.Max(0.1, EffectCatalog.Value(e, "attack")) / 1000 * r));
        _release = Math.Exp(-1 / (Math.Max(1, EffectCatalog.Value(e, "release")) / 1000 * r));
        _makeup = EffectCatalog.Value(e, "makeup");
    }

    /// <summary>Curva estática: redução de ganho (dB) para um nível de entrada (dB).</summary>
    public static double StaticReduction(double levelDb, double threshold, double ratio)
    {
        var over = levelDb - threshold;
        if (over <= -Knee / 2) return 0;
        if (over >= Knee / 2) return over * (1 - 1 / ratio);
        var x = over + Knee / 2;
        return (1 - 1 / ratio) * x * x / (2 * Knee);
    }

    public void Process(Span<float> s)
    {
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var peak = Math.Max(Math.Abs(s[i]), Math.Abs(s[i + 1]));
            var levelDb = peak > 1e-6 ? 20 * Math.Log10(peak) : -120;
            var target = StaticReduction(levelDb, _threshold, _ratio);
            // Envelope sobre a redução: sobe com o ataque, desce com a liberação.
            var coef = target > _envDb ? _attack : _release;
            _envDb = target + coef * (_envDb - target);
            var g = (float)Math.Pow(10, (_makeup - _envDb) / 20);
            s[i] *= g;
            s[i + 1] *= g;
        }
        GainReductionDb = _envDb;
    }

    public void Reset() => _envDb = 0;
}

/// <summary>
/// Redução de ruído espectral (RF124): STFT (1024, salto 256, janela raiz de Hann), estimativa contínua do piso de ruído
/// por estatística de mínimos com subida lenta, subtração espectral com piso de atenuação e suavização temporal do ganho.
/// L e R são transformados juntos (parte real/imaginária) e recebem o mesmo ganho, preservando a imagem estéreo.
/// </summary>
public sealed class SpectralDenoiser : IAudioProcessor
{
    private const int N = 1024;
    private const int Hop = N / 4;
    private const int Bins = N / 2 + 1;
    private const double NoiseBias = 2.0;
    private static readonly double[] Window = BuildWindow();

    private readonly float[] _inL = new float[N], _inR = new float[N];
    private readonly double[] _re = new double[N], _im = new double[N];
    private readonly double[] _outL = new double[N], _outR = new double[N];
    private readonly double[] _noise = new double[Bins], _gain = new double[Bins], _smoothPower = new double[Bins];
    private readonly Queue<(float L, float R)> _fifo = new();
    private int _filled;
    private long _frames;
    private double _floor = 0.1, _overSubtract = 2;

    public int LatencyFrames => N;
    public int PreRollFrames => AudioProcessorFactory.Rate; // 1 s para aprender o piso de ruído

    public SpectralDenoiser() => Reset();

    private static double[] BuildWindow()
    {
        var w = new double[N];
        for (var i = 0; i < N; i++) w[i] = Math.Sqrt(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
        return w;
    }

    public void Configure(EffectInstance e)
    {
        var amount = Math.Clamp(EffectCatalog.Value(e, "amount"), 0, 100) / 100;
        var sensitivity = Math.Clamp(EffectCatalog.Value(e, "sensitivity"), 0, 100) / 100;
        _floor = Math.Pow(10, -amount * 30 / 20); // até −30 dB
        _overSubtract = 1 + sensitivity * 3;
    }

    public void Reset()
    {
        Array.Clear(_inL);
        Array.Clear(_inR);
        Array.Clear(_outL);
        Array.Clear(_outR);
        Array.Fill(_noise, -1);
        Array.Fill(_gain, 1);
        Array.Clear(_smoothPower);
        _fifo.Clear();
        for (var i = 0; i < N; i++) _fifo.Enqueue((0, 0));
        _filled = 0;
        _frames = 0;
    }

    public void Process(Span<float> s)
    {
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            // Janela deslizante: as últimas N amostras de entrada (as mais antigas no início).
            _inL[N - Hop + _filled] = s[i];
            _inR[N - Hop + _filled] = s[i + 1];
            if (++_filled == Hop)
            {
                ProcessFrame();
                _filled = 0;
            }
            var (l, r) = _fifo.Dequeue();
            s[i] = l;
            s[i + 1] = r;
        }
    }

    private void ProcessFrame()
    {
        for (var i = 0; i < N; i++)
        {
            _re[i] = _inL[i] * Window[i];
            _im[i] = _inR[i] * Window[i];
        }
        Fft.Transform(_re, _im);

        // Taxa de subida do piso de ruído: ~3 dB/s (para acompanhar ruídos que mudam devagar).
        var rise = Math.Pow(10, 3.0 / 10 * Hop / AudioProcessorFactory.Rate);
        for (var k = 0; k < Bins; k++)
        {
            var nk = (N - k) % N;
            // Separa os espectros de L e R a partir da FFT complexa conjunta.
            var lr = (_re[k] + _re[nk]) / 2;
            var li = (_im[k] - _im[nk]) / 2;
            var rr = (_im[k] + _im[nk]) / 2;
            var ri = (_re[nk] - _re[k]) / 2;
            var power = (lr * lr + li * li + rr * rr + ri * ri) / 2;

            _smoothPower[k] = _frames == 0 ? power : 0.8 * _smoothPower[k] + 0.2 * power;
            var p = _smoothPower[k];
            if (_noise[k] < 0) _noise[k] = p;
            else _noise[k] = Math.Min(p, _noise[k] * rise);

            // O mínimo do espectro suavizado subestima a média do ruído: compensação de viés (estatística de mínimos).
            var g = p <= 1e-20 ? _floor : Math.Max(_floor, 1 - _overSubtract * NoiseBias * _noise[k] / p);
            // Suavização: ataque rápido (abre), liberação mais lenta (fecha) — reduz o "ruído musical".
            _gain[k] = g > _gain[k] ? 0.7 * g + 0.3 * _gain[k] : 0.35 * g + 0.65 * _gain[k];
        }
        _frames++;

        for (var k = 0; k < N; k++)
        {
            var g = _gain[k <= N / 2 ? k : N - k];
            _re[k] *= g;
            _im[k] *= g;
        }
        Fft.Transform(_re, _im, inverse: true);

        // Sobreposição-soma (janela de síntese também raiz de Hann; soma das janelas² com 75% = 2).
        for (var i = 0; i < N; i++)
        {
            _outL[i] += _re[i] * Window[i] * 0.5;
            _outR[i] += _im[i] * Window[i] * 0.5;
        }
        for (var i = 0; i < Hop; i++) _fifo.Enqueue(((float)_outL[i], (float)_outR[i]));
        Array.Copy(_outL, Hop, _outL, 0, N - Hop);
        Array.Copy(_outR, Hop, _outR, 0, N - Hop);
        Array.Clear(_outL, N - Hop, Hop);
        Array.Clear(_outR, N - Hop, Hop);
        Array.Copy(_inL, Hop, _inL, 0, N - Hop);
        Array.Copy(_inR, Hop, _inR, 0, N - Hop);
    }
}

/// <summary>Eco: linha de atraso com realimentação (RF131 — efeitos sonoros).</summary>
public sealed class EchoProcessor : IAudioProcessor
{
    private readonly float[] _buffer = new float[AudioProcessorFactory.Rate * 2 * 2 + 4];
    private int _delay = 1, _write;
    private float _feedback, _mix;

    public int LatencyFrames => 0;
    public int PreRollFrames { get; private set; }

    public void Configure(EffectInstance e)
    {
        _delay = Math.Clamp((int)(EffectCatalog.Value(e, "delay") / 1000 * AudioProcessorFactory.Rate), 1, AudioProcessorFactory.Rate * 2);
        _feedback = (float)Math.Clamp(EffectCatalog.Value(e, "feedback") / 100, 0, 0.95);
        _mix = (float)Math.Clamp(EffectCatalog.Value(e, "mix") / 100, 0, 1);
        PreRollFrames = Math.Min(AudioProcessorFactory.Rate * 3, _delay * 6);
    }

    public void Process(Span<float> s)
    {
        var size = _buffer.Length / 2;
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var read = (_write - _delay + size) % size;
            var dl = _buffer[read * 2];
            var dr = _buffer[read * 2 + 1];
            _buffer[_write * 2] = s[i] + dl * _feedback;
            _buffer[_write * 2 + 1] = s[i + 1] + dr * _feedback;
            s[i] += dl * _mix;
            s[i + 1] += dr * _mix;
            _write = (_write + 1) % size;
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _write = 0;
    }
}

/// <summary>Reverberação no estilo Freeverb (8 filtros pente com amortecimento e 4 passa-tudo por canal).</summary>
public sealed class ReverbProcessor : IAudioProcessor
{
    private static readonly int[] CombTuning = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllpassTuning = [556, 441, 341, 225];
    private const int Spread = 23;

    private readonly Comb[] _combL, _combR;
    private readonly Allpass[] _apL, _apR;
    private float _wet, _dry = 1;

    public int LatencyFrames => 0;
    public int PreRollFrames => AudioProcessorFactory.Rate * 3 / 2;

    public ReverbProcessor()
    {
        static int Scale(int n) => n * AudioProcessorFactory.Rate / 44100;
        _combL = CombTuning.Select(n => new Comb(Scale(n))).ToArray();
        _combR = CombTuning.Select(n => new Comb(Scale(n + Spread))).ToArray();
        _apL = AllpassTuning.Select(n => new Allpass(Scale(n))).ToArray();
        _apR = AllpassTuning.Select(n => new Allpass(Scale(n + Spread))).ToArray();
    }

    public void Configure(EffectInstance e)
    {
        var room = (float)(0.7 + 0.28 * Math.Clamp(EffectCatalog.Value(e, "room"), 0, 100) / 100);
        var damp = (float)(0.4 * Math.Clamp(EffectCatalog.Value(e, "damping"), 0, 100) / 100);
        var mix = (float)Math.Clamp(EffectCatalog.Value(e, "mix") / 100, 0, 1);
        _wet = mix * 3;
        _dry = 1 - mix * 0.5f;
        foreach (var c in _combL.Concat(_combR))
        {
            c.Feedback = room;
            c.Damp = damp;
        }
    }

    public void Process(Span<float> s)
    {
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var input = (s[i] + s[i + 1]) * 0.015f;
            float ol = 0, or = 0;
            for (var c = 0; c < _combL.Length; c++)
            {
                ol += _combL[c].Process(input);
                or += _combR[c].Process(input);
            }
            for (var a = 0; a < _apL.Length; a++)
            {
                ol = _apL[a].Process(ol);
                or = _apR[a].Process(or);
            }
            s[i] = s[i] * _dry + ol * _wet;
            s[i + 1] = s[i + 1] * _dry + or * _wet;
        }
    }

    public void Reset()
    {
        foreach (var c in _combL.Concat(_combR)) c.Clear();
        foreach (var a in _apL.Concat(_apR)) a.Clear();
    }

    private sealed class Comb(int size)
    {
        private readonly float[] _buf = new float[size];
        private int _i;
        private float _store;
        public float Feedback, Damp;

        public float Process(float x)
        {
            var y = _buf[_i];
            _store = y * (1 - Damp) + _store * Damp;
            _buf[_i] = x + _store * Feedback;
            if (++_i >= _buf.Length) _i = 0;
            return y;
        }

        public void Clear()
        {
            Array.Clear(_buf);
            _store = 0;
        }
    }

    private sealed class Allpass(int size)
    {
        private readonly float[] _buf = new float[size];
        private int _i;

        public float Process(float x)
        {
            var b = _buf[_i];
            var y = -x + b;
            _buf[_i] = x + b * 0.5f;
            if (++_i >= _buf.Length) _i = 0;
            return y;
        }

        public void Clear() => Array.Clear(_buf);
    }
}

/// <summary>Telefone/rádio: banda de 300–3400 Hz com leve saturação.</summary>
public sealed class TelephoneProcessor : IAudioProcessor
{
    private readonly Biquad _hp1 = Biquad.HighPass(300, 0.5412, AudioProcessorFactory.Rate);
    private readonly Biquad _hp2 = Biquad.HighPass(300, 1.3066, AudioProcessorFactory.Rate);
    private readonly Biquad _lp1 = Biquad.LowPass(3400, 0.5412, AudioProcessorFactory.Rate);
    private readonly Biquad _lp2 = Biquad.LowPass(3400, 1.3066, AudioProcessorFactory.Rate);
    private readonly Biquad _presence = Biquad.Peaking(1800, 6, 1.2, AudioProcessorFactory.Rate);
    private float[] _dry = [];
    private float _mix = 1;

    public int LatencyFrames => 0;
    public int PreRollFrames => AudioProcessorFactory.Rate / 20;

    public void Configure(EffectInstance e) => _mix = (float)Math.Clamp(EffectCatalog.Value(e, "intensity") / 100, 0, 1);

    public void Process(Span<float> s)
    {
        if (_dry.Length < s.Length) _dry = new float[s.Length];
        s.CopyTo(_dry);
        _hp1.Process(s);
        _hp2.Process(s);
        _lp1.Process(s);
        _lp2.Process(s);
        _presence.Process(s);
        for (var i = 0; i < s.Length; i++)
        {
            // Saturação suave com ganho unitário para sinais baixos (não aumenta o volume percebido).
            var wet = MathF.Tanh(s[i] * 2) / 2;
            s[i] = _dry[i] + (wet - _dry[i]) * _mix;
        }
    }

    public void Reset()
    {
        _hp1.Reset();
        _hp2.Reset();
        _lp1.Reset();
        _lp2.Reset();
        _presence.Reset();
    }
}

/// <summary>Distorção por saturação (tangente hiperbólica) com nível compensado.</summary>
public sealed class DistortionProcessor : IAudioProcessor
{
    private float _drive = 1, _norm = 1, _mix = 1;

    public int LatencyFrames => 0;
    public int PreRollFrames => 0;

    public void Configure(EffectInstance e)
    {
        _drive = (float)(1 + Math.Clamp(EffectCatalog.Value(e, "drive"), 0, 100) * 0.3);
        _norm = 1 / MathF.Tanh(_drive);
        _mix = (float)Math.Clamp(EffectCatalog.Value(e, "mix") / 100, 0, 1);
    }

    public void Process(Span<float> s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var wet = MathF.Tanh(s[i] * _drive) * _norm;
            s[i] += (wet - s[i]) * _mix;
        }
    }

    public void Reset()
    {
    }
}

/// <summary>Cadeia de processadores na ordem dos efeitos do clipe; reconfigura sem perder estado quando só os parâmetros mudam.</summary>
public sealed class AudioEffectChain
{
    private readonly List<(string Type, Guid Id, IAudioProcessor Processor)> _items = [];

    public int LatencyFrames => _items.Sum(i => i.Processor.LatencyFrames);
    public int PreRollFrames => _items.Count == 0 ? 0 : _items.Max(i => i.Processor.PreRollFrames);
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Sincroniza com a lista de efeitos; retorna verdadeiro se a estrutura mudou (exige reinício).</summary>
    public bool Update(IReadOnlyList<EffectInstance> effects)
    {
        var same = effects.Count == _items.Count;
        for (var i = 0; same && i < effects.Count; i++)
            same = effects[i].Id == _items[i].Id && effects[i].Type == _items[i].Type;
        if (same)
        {
            for (var i = 0; i < effects.Count; i++) _items[i].Processor.Configure(effects[i]);
            return false;
        }
        _items.Clear();
        foreach (var e in effects)
            if (AudioProcessorFactory.Create(e) is { } p)
                _items.Add((e.Type, e.Id, p));
        return true;
    }

    public void Process(Span<float> s)
    {
        foreach (var item in _items) item.Processor.Process(s);
    }

    public void Reset()
    {
        foreach (var item in _items) item.Processor.Reset();
    }
}
