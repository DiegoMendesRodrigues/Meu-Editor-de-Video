using System.Globalization;

namespace MeuEditorDeVideo.Domain.Time;

/// <summary>Taxa de quadros racional (numerador/denominador), ex.: 30000/1001 = 29,97 FPS.</summary>
public readonly record struct FrameRate
{
    public int Numerator { get; }
    public int Denominator { get; }

    public FrameRate(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var g = Gcd(numerator, denominator);
        Numerator = numerator / g;
        Denominator = denominator / g;
    }

    public static readonly FrameRate Fps23_976 = new(24000, 1001);
    public static readonly FrameRate Fps24 = new(24, 1);
    public static readonly FrameRate Fps25 = new(25, 1);
    public static readonly FrameRate Fps29_97 = new(30000, 1001);
    public static readonly FrameRate Fps30 = new(30, 1);
    public static readonly FrameRate Fps50 = new(50, 1);
    public static readonly FrameRate Fps59_94 = new(60000, 1001);
    public static readonly FrameRate Fps60 = new(60, 1);

    public static IReadOnlyList<FrameRate> Standard { get; } =
        [Fps23_976, Fps24, Fps25, Fps29_97, Fps30, Fps50, Fps59_94, Fps60];

    public double Value => (double)Numerator / Denominator;

    /// <summary>True quando a duração de um quadro é um número inteiro de flicks.</summary>
    public bool IsExactInFlicks => (Int128)MediaTime.TicksPerSecond * Denominator % Numerator == 0;

    /// <summary>Duração de um quadro (truncada se a taxa não for exata em flicks).</summary>
    public MediaTime FrameDuration => FrameToTime(1);

    /// <summary>Início do quadro <paramref name="frame"/>.</summary>
    public MediaTime FrameToTime(long frame) =>
        new((long)((Int128)frame * MediaTime.TicksPerSecond * Denominator / Numerator));

    /// <summary>Quadro que contém o instante (arredondamento para baixo, também para tempos negativos).</summary>
    public long TimeToFrame(MediaTime time)
    {
        Int128 num = (Int128)time.Ticks * Numerator;
        Int128 den = (Int128)MediaTime.TicksPerSecond * Denominator;
        var q = num / den;
        if (num % den != 0 && num < 0) q--;
        return (long)q;
    }

    /// <summary>Ajusta um instante ao início do quadro mais próximo.</summary>
    public MediaTime SnapToFrame(MediaTime time)
    {
        var f = TimeToFrame(time);
        var a = FrameToTime(f);
        var b = FrameToTime(f + 1);
        return (time - a) <= (b - time) ? a : b;
    }

    public override string ToString() => Denominator == 1
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : Value.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}
