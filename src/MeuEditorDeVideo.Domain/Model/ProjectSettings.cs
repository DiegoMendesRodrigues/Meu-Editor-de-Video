using System.Globalization;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Model;

public enum ColorProfile
{
    Rec709,
    SRgb,
    Rec2020,
}

/// <summary>Proporção reduzida (ex.: 1920x1080 → 16:9).</summary>
public readonly record struct AspectRatio(int Width, int Height)
{
    public static AspectRatio FromSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var g = Gcd(width, height);
        return new AspectRatio(width / g, height / g);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}:{Height}");

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }
}

/// <summary>Configurações técnicas do projeto (RF005–RF007, §5.2).</summary>
public sealed record ProjectSettings
{
    public const int MinDimension = 16;
    public const int MaxDimension = 8192;

    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public FrameRate FrameRate { get; init; } = FrameRate.Fps30;
    public ColorProfile ColorProfile { get; init; } = ColorProfile.Rec709;
    public int SampleRate { get; init; } = 48_000;
    public int AudioChannels { get; init; } = 2;

    public AspectRatio AspectRatio => AspectRatio.FromSize(Width, Height);

    public IEnumerable<string> Validate()
    {
        if (Width is < MinDimension or > MaxDimension || Height is < MinDimension or > MaxDimension)
            yield return $"A resolução deve estar entre {MinDimension} e {MaxDimension} pixels em cada dimensão.";
        if (Width % 2 != 0 || Height % 2 != 0)
            yield return "Largura e altura devem ser números pares (exigência dos codificadores de vídeo).";
        if (FrameRate.Value is < 1 or > 240)
            yield return "A taxa de quadros deve estar entre 1 e 240 FPS.";
        if (SampleRate is not (44_100 or 48_000 or 96_000))
            yield return "A taxa de amostragem de áudio deve ser 44,1 kHz, 48 kHz ou 96 kHz.";
        if (AudioChannels is not (1 or 2 or 6))
            yield return "O áudio do projeto deve ser mono, estéreo ou 5.1.";
    }
}
