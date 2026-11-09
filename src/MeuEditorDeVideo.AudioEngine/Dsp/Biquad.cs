namespace MeuEditorDeVideo.AudioEngine.Dsp;

/// <summary>Filtro biquad estéreo (fórmulas do "Audio EQ Cookbook", R. Bristow-Johnson), forma direta transposta II.</summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _l1, _l2, _r1, _r2;

    public static Biquad LowShelf(double freq, double gainDb, int rate) => new Biquad().SetShelf(freq, gainDb, rate, high: false);
    public static Biquad HighShelf(double freq, double gainDb, int rate) => new Biquad().SetShelf(freq, gainDb, rate, high: true);
    public static Biquad Peaking(double freq, double gainDb, double q, int rate) => new Biquad().SetPeaking(freq, gainDb, q, rate);
    public static Biquad HighPass(double freq, double q, int rate) => new Biquad().SetPass(freq, q, rate, high: true);
    public static Biquad LowPass(double freq, double q, int rate) => new Biquad().SetPass(freq, q, rate, high: false);

    public Biquad SetPeaking(double freq, double gainDb, double q, int rate)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * Clamp(freq, rate) / rate;
        var alpha = Math.Sin(w) / (2 * Math.Max(0.05, q));
        var cos = Math.Cos(w);
        return Set(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    public Biquad SetShelf(double freq, double gainDb, int rate, bool high)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * Clamp(freq, rate) / rate;
        var cos = Math.Cos(w);
        var alpha = Math.Sin(w) / 2 * Math.Sqrt(2); // S = 1
        var sq = 2 * Math.Sqrt(a) * alpha;
        return high
            ? Set(a * ((a + 1) + (a - 1) * cos + sq), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - sq),
                (a + 1) - (a - 1) * cos + sq, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - sq)
            : Set(a * ((a + 1) - (a - 1) * cos + sq), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - sq),
                (a + 1) + (a - 1) * cos + sq, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - sq);
    }

    public Biquad SetPass(double freq, double q, int rate, bool high)
    {
        var w = 2 * Math.PI * Clamp(freq, rate) / rate;
        var cos = Math.Cos(w);
        var alpha = Math.Sin(w) / (2 * Math.Max(0.05, q));
        return high
            ? Set((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha)
            : Set((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>Biquad com coeficientes diretos (a0 = 1).</summary>
    public static Biquad FromCoefficients(double b0, double b1, double b2, double a1, double a2) => new Biquad().Set(b0, b1, b2, 1, a1, a2);

    private static double Clamp(double freq, int rate) => Math.Clamp(freq, 10, rate * 0.45);

    private Biquad Set(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
        return this;
    }

    public void Reset() => _l1 = _l2 = _r1 = _r2 = 0;

    /// <summary>Processa amostras estéreo intercaladas.</summary>
    public void Process(Span<float> s)
    {
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            double x = s[i];
            var y = _b0 * x + _l1;
            _l1 = _b1 * x - _a1 * y + _l2;
            _l2 = _b2 * x - _a2 * y;
            s[i] = (float)y;

            x = s[i + 1];
            y = _b0 * x + _r1;
            _r1 = _b1 * x - _a1 * y + _r2;
            _r2 = _b2 * x - _a2 * y;
            s[i + 1] = (float)y;
        }
        // Evita números desnormalizados (lentíssimos) no silêncio.
        if (Math.Abs(_l1) < 1e-25) _l1 = 0;
        if (Math.Abs(_l2) < 1e-25) _l2 = 0;
        if (Math.Abs(_r1) < 1e-25) _r1 = 0;
        if (Math.Abs(_r2) < 1e-25) _r2 = 0;
    }

    /// <summary>Processa amostras mono (usado na medição de loudness).</summary>
    public double ProcessMono(double x)
    {
        var y = _b0 * x + _l1;
        _l1 = _b1 * x - _a1 * y + _l2;
        _l2 = _b2 * x - _a2 * y;
        return y;
    }

    /// <summary>Processa o canal direito com estado próprio (uso mono duplo, ex.: medição de loudness).</summary>
    public double ProcessMonoRight(double x)
    {
        var y = _b0 * x + _r1;
        _r1 = _b1 * x - _a1 * y + _r2;
        _r2 = _b2 * x - _a2 * y;
        return y;
    }

    /// <summary>Resposta em magnitude (dB) na frequência — para testes e para desenhar a curva do equalizador.</summary>
    public double MagnitudeDb(double freq, int rate)
    {
        var w = 2 * Math.PI * freq / rate;
        var (c1, s1, c2, s2) = (Math.Cos(w), Math.Sin(w), Math.Cos(2 * w), Math.Sin(2 * w));
        var nr = _b0 + _b1 * c1 + _b2 * c2;
        var ni = -(_b1 * s1 + _b2 * s2);
        var dr = 1 + _a1 * c1 + _a2 * c2;
        var di = -(_a1 * s1 + _a2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }
}
