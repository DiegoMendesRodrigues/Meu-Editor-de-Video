using System.Globalization;

namespace MeuEditorDeVideo.Domain.Time;

/// <summary>
/// Instante ou duração na linha do tempo, representado em "flicks" (1/705.600.000 s).
/// A base divide exatamente 23,976 / 24 / 25 / 29,97 / 30 / 50 / 59,94 / 60 FPS e 44,1 / 48 / 96 kHz,
/// evitando acúmulo de erro de ponto flutuante (§6.4 da especificação).
/// </summary>
public readonly record struct MediaTime(long Ticks) : IComparable<MediaTime>
{
    public const long TicksPerSecond = 705_600_000L;

    public static readonly MediaTime Zero = new(0);
    public static readonly MediaTime MaxValue = new(long.MaxValue);
    public static readonly MediaTime MinValue = new(long.MinValue);

    public static MediaTime FromSeconds(long seconds) => new(checked(seconds * TicksPerSecond));

    /// <summary>Conversão a partir de segundos fracionários. Use apenas para entrada do usuário/metadados externos.</summary>
    public static MediaTime FromSecondsApprox(double seconds) => new((long)Math.Round(seconds * TicksPerSecond));

    /// <summary>Converte uma quantidade de amostras de áudio, arredondando para baixo quando a taxa não é exata.</summary>
    public static MediaTime FromSamples(long samples, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return new((long)((Int128)samples * TicksPerSecond / sampleRate));
    }

    public long ToSamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return (long)((Int128)Ticks * sampleRate / TicksPerSecond);
    }

    public double TotalSeconds => (double)Ticks / TicksPerSecond;

    public static MediaTime operator +(MediaTime a, MediaTime b) => new(checked(a.Ticks + b.Ticks));
    public static MediaTime operator -(MediaTime a, MediaTime b) => new(checked(a.Ticks - b.Ticks));
    public static MediaTime operator -(MediaTime a) => new(checked(-a.Ticks));
    public static bool operator <(MediaTime a, MediaTime b) => a.Ticks < b.Ticks;
    public static bool operator >(MediaTime a, MediaTime b) => a.Ticks > b.Ticks;
    public static bool operator <=(MediaTime a, MediaTime b) => a.Ticks <= b.Ticks;
    public static bool operator >=(MediaTime a, MediaTime b) => a.Ticks >= b.Ticks;

    public static MediaTime Min(MediaTime a, MediaTime b) => a <= b ? a : b;
    public static MediaTime Max(MediaTime a, MediaTime b) => a >= b ? a : b;

    public int CompareTo(MediaTime other) => Ticks.CompareTo(other.Ticks);

    public override string ToString() => TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture) + "s";
}
