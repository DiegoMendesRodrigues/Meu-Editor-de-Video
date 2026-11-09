namespace MeuEditorDeVideo.AudioEngine.Dsp;

/// <summary>
/// Fonte sequencial de quadros estéreo de entrada (posição absoluta em quadros da mídia; posições negativas são silêncio).
/// </summary>
public delegate void FrameReader(long position, Span<float> destination);

/// <summary>Leitura sequencial de uma fonte com velocidade alterada, produzindo quadros de saída em sequência.</summary>
public interface ISpeedProducer
{
    void Read(Span<float> destination);
}

/// <summary>Velocidade normal: copia a entrada.</summary>
public sealed class DirectProducer(FrameReader reader, long start) : ISpeedProducer
{
    private long _pos = start;

    public void Read(Span<float> destination)
    {
        reader(_pos, destination);
        _pos += destination.Length / 2;
    }
}

/// <summary>Reamostragem linear (o tom acompanha a velocidade).</summary>
public sealed class ResampleProducer : ISpeedProducer
{
    private readonly FrameReader _reader;
    private readonly double _speed;
    private float[] _buf = new float[8192];
    private long _bufStart;
    private int _bufFrames;
    private double _pos;

    public ResampleProducer(FrameReader reader, double start, double speed)
    {
        _reader = reader;
        _speed = speed;
        _pos = start;
        _bufStart = (long)Math.Floor(start);
    }

    public void Read(Span<float> destination)
    {
        var frames = destination.Length / 2;
        var needEnd = (long)Math.Floor(_pos + frames * _speed) + 2;
        Ensure(needEnd);
        for (var i = 0; i < frames; i++)
        {
            var p = _pos + i * _speed;
            var i0 = (long)Math.Floor(p);
            var frac = (float)(p - i0);
            var a = (int)(i0 - _bufStart) * 2;
            destination[i * 2] = _buf[a] + (_buf[a + 2] - _buf[a]) * frac;
            destination[i * 2 + 1] = _buf[a + 1] + (_buf[a + 3] - _buf[a + 1]) * frac;
        }
        _pos += frames * _speed;
        // Descarta o que já foi consumido (mantém um quadro para a interpolação).
        var keepFrom = (long)Math.Floor(_pos);
        var drop = (int)Math.Clamp(keepFrom - _bufStart, 0, _bufFrames);
        if (drop > 0)
        {
            Array.Copy(_buf, drop * 2, _buf, 0, (_bufFrames - drop) * 2);
            _bufFrames -= drop;
            _bufStart += drop;
        }
    }

    private void Ensure(long endExclusive)
    {
        var have = _bufStart + _bufFrames;
        if (have >= endExclusive) return;
        var add = (int)(endExclusive - have);
        if (_buf.Length < (_bufFrames + add) * 2) Array.Resize(ref _buf, (_bufFrames + add) * 2 * 2);
        _reader(have, _buf.AsSpan(_bufFrames * 2, add * 2));
        _bufFrames += add;
    }
}

/// <summary>
/// Mudança de velocidade preservando o tom (WSOLA — sobreposição-soma com similaridade de forma de onda).
/// Janela de Hann de 1024 quadros, salto de síntese 512; o segmento de análise é deslocado em até ±256 quadros
/// para maximizar a correlação com a continuação natural do segmento anterior, evitando cancelamentos de fase.
/// </summary>
public sealed class TimeStretchProducer : ISpeedProducer
{
    private const int N = 1024;
    private const int Hs = N / 2;
    private const int Tol = 256;
    private static readonly float[] Window = BuildWindow();

    private readonly FrameReader _reader;
    private readonly double _speed;
    private float[] _in = new float[16384];
    private long _inStart;
    private int _inFrames;
    private double _anaPos;
    private long _prevSel;
    private readonly float[] _ola = new float[N * 2];
    private readonly Queue<float> _out = new();

    public TimeStretchProducer(FrameReader reader, double start, double speed)
    {
        _reader = reader;
        _speed = speed;
        // Começa meia janela antes, para que a primeira saída útil já tenha sobreposição completa; descarta Hs quadros.
        _anaPos = start - Hs * speed;
        _prevSel = (long)Math.Round(_anaPos) - Hs;
        _inStart = (long)Math.Floor(Math.Min(_anaPos, _prevSel)) - Tol - 4;
        Step();
        _out.Clear();
    }

    private static float[] BuildWindow()
    {
        var w = new float[N];
        for (var i = 0; i < N; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
        return w;
    }

    public void Read(Span<float> destination)
    {
        while (_out.Count < destination.Length) Step();
        for (var i = 0; i < destination.Length; i++) destination[i] = _out.Dequeue();
    }

    private float At(long frame, int ch) => _in[(int)(frame - _inStart) * 2 + ch];

    private void Step()
    {
        var target = (long)Math.Round(_anaPos);
        var natural = _prevSel + Hs;
        Ensure(Math.Max(target + Tol, natural) + N + 1);

        // Busca grossa (decimada por 4) e refinamento fino em torno do melhor deslocamento.
        var best = 0;
        var bestScore = double.NegativeInfinity;
        for (var d = -Tol; d <= Tol; d += 4)
        {
            var score = Correlate(target + d, natural, 4);
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        var coarse = best;
        bestScore = double.NegativeInfinity;
        for (var d = Math.Max(-Tol, coarse - 3); d <= Math.Min(Tol, coarse + 3); d++)
        {
            var score = Correlate(target + d, natural, 1);
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        var sel = target + best;

        for (var i = 0; i < N; i++)
        {
            var w = Window[i];
            _ola[i * 2] += At(sel + i, 0) * w;
            _ola[i * 2 + 1] += At(sel + i, 1) * w;
        }
        for (var i = 0; i < Hs * 2; i++) _out.Enqueue(_ola[i]);
        Array.Copy(_ola, Hs * 2, _ola, 0, (N - Hs) * 2);
        Array.Clear(_ola, (N - Hs) * 2, Hs * 2);

        _prevSel = sel;
        _anaPos += Hs * _speed;

        // Descarta a entrada que não será mais usada.
        var keepFrom = Math.Min((long)Math.Round(_anaPos) - Tol, _prevSel + Hs) - 4;
        var drop = (int)Math.Clamp(keepFrom - _inStart, 0, _inFrames);
        if (drop > 0)
        {
            Array.Copy(_in, drop * 2, _in, 0, (_inFrames - drop) * 2);
            _inFrames -= drop;
            _inStart += drop;
        }
    }

    private double Correlate(long a, long b, int step)
    {
        double sum = 0, ea = 0, eb = 0;
        for (var i = 0; i < N; i += step)
        {
            var x = At(a + i, 0) + At(a + i, 1);
            var y = At(b + i, 0) + At(b + i, 1);
            sum += x * y;
            ea += x * x;
            eb += y * y;
        }
        var norm = Math.Sqrt(ea * eb);
        return norm < 1e-12 ? 0 : sum / norm;
    }

    private void Ensure(long endExclusive)
    {
        var have = _inStart + _inFrames;
        if (have >= endExclusive) return;
        var add = (int)(endExclusive - have);
        if (_in.Length < (_inFrames + add) * 2) Array.Resize(ref _in, (_inFrames + add) * 2 * 2);
        _reader(have, _in.AsSpan(_inFrames * 2, add * 2));
        _inFrames += add;
    }
}
