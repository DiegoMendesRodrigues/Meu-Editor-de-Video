using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Model;

/// <summary>Modos de mistura da composição de camadas (RF151).</summary>
public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    SoftLight,
    Add,
    Darken,
    Lighten,
    Difference,
}

/// <summary>Curva de interpolação entre um keyframe e o seguinte (RF144).</summary>
public enum Easing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,

    /// <summary>Mantém o valor até o próximo keyframe (salto).</summary>
    Hold,
}

/// <summary>Valor de uma propriedade num instante relativo ao início do clipe.</summary>
public sealed record Keyframe(MediaTime Offset, double Value, Easing Easing = Easing.EaseInOut);

/// <summary>Propriedades animáveis por keyframes (RF076, RF145–RF148).</summary>
public static class AnimatedProperty
{
    public const string X = "x";
    public const string Y = "y";
    public const string Scale = "scale";
    public const string Rotation = "rotation";
    public const string Opacity = "opacity";

    /// <summary>Volume do clipe de áudio (RF121).</summary>
    public const string Volume = "volume";

    /// <summary>Propriedades visuais.</summary>
    public static IReadOnlyList<string> All { get; } = [X, Y, Scale, Rotation, Opacity];
}

/// <summary>
/// Efeito aplicado a um clipe (RF077, RF086–RF089), incluindo correção de cor e chroma key.
/// O tipo referencia o catálogo de efeitos; parâmetros ausentes usam o valor padrão do catálogo.
/// </summary>
public sealed record EffectInstance
{
    public required Guid Id { get; init; }
    public required string Type { get; init; }
    public bool Enabled { get; init; } = true;
    public ImmutableDictionary<string, double> Parameters { get; init; } = ImmutableDictionary<string, double>.Empty;

    /// <summary>Recurso externo do efeito (ex.: caminho do arquivo .cube de uma LUT).</summary>
    public string? Resource { get; init; }
}

/// <summary>Tipos de transição (RF078–RF083). O corte seco é a ausência de transição.</summary>
public enum TransitionKind
{
    Dissolve,
    FadeBlack,
    Zoom,
    SlideLeft,
    SlideRight,
    SlideUp,
    SlideDown,
    PushLeft,
    PushRight,
    WipeLeft,
    WipeRight,
    WipeUp,
    WipeDown,
    Circle,
    Diamond,
    Clock,
    Blinds,
}

/// <summary>
/// Transição entre dois clipes adjacentes da mesma trilha, centrada no ponto de corte (RF084).
/// Fora do clipe são usados os quadros além dos pontos de entrada/saída quando a mídia tem sobra;
/// caso contrário o primeiro/último quadro é mantido.
/// </summary>
public sealed record Transition
{
    public required Guid Id { get; init; }
    public required Guid FromClipId { get; init; }
    public required Guid ToClipId { get; init; }
    public TransitionKind Kind { get; init; } = TransitionKind.Dissolve;
    public required MediaTime Duration { get; init; }
}
