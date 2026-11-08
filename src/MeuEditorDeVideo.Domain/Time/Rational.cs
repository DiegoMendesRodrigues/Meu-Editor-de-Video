using System.Globalization;

namespace MeuEditorDeVideo.Domain.Time;

/// <summary>Fração positiva normalizada, usada para velocidade de clipes (ex.: 1/2 = câmera lenta 50%).</summary>
public readonly record struct Rational
{
    public long Numerator { get; }
    public long Denominator { get; }

    public Rational(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var g = Gcd(numerator, denominator);
        Numerator = numerator / g;
        Denominator = denominator / g;
    }

    public static readonly Rational One = new(1, 1);

    public double Value => (double)Numerator / Denominator;

    /// <summary>Duração na timeline de um trecho de mídia reproduzido nesta velocidade.</summary>
    public MediaTime TimelineDurationOf(MediaTime sourceDuration) =>
        new((long)((Int128)sourceDuration.Ticks * Denominator / Numerator));

    /// <summary>Duração consumida da mídia de origem para uma duração na timeline.</summary>
    public MediaTime SourceDurationOf(MediaTime timelineDuration) =>
        new((long)((Int128)timelineDuration.Ticks * Numerator / Denominator));

    public override string ToString() => Denominator == 1
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : $"{Numerator.ToString(CultureInfo.InvariantCulture)}/{Denominator.ToString(CultureInfo.InvariantCulture)}";

    public static Rational Parse(string text)
    {
        var parts = text.Split('/');
        return parts.Length switch
        {
            1 => new Rational(long.Parse(parts[0], CultureInfo.InvariantCulture), 1),
            2 => new Rational(long.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"Fração inválida: '{text}'."),
        };
    }

    private static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }
}
