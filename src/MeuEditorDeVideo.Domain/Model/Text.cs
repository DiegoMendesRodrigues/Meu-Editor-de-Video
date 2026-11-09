using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Model;

/// <summary>Finalidade de um clipe de texto (RF132–RF135, RF143): define o comportamento e o modelo inicial.</summary>
public enum TextKind
{
    /// <summary>Texto livre.</summary>
    Text,

    /// <summary>Título grande.</summary>
    Title,

    /// <summary>Créditos rolando de baixo para cima durante todo o clipe.</summary>
    Credits,

    /// <summary>Tarja (lower third): nome e cargo no canto inferior.</summary>
    LowerThird,

    /// <summary>Legenda (trilha de legendas; exportável em arquivo .srt/.vtt).</summary>
    Subtitle,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Ponto vertical do bloco de texto que fica na posição Y do clipe.</summary>
public enum TextAnchor
{
    Top,
    Center,
    Bottom,
}

/// <summary>Animações de entrada e saída de texto (RF139).</summary>
public enum TextAnimationKind
{
    None,
    Fade,
    SlideUp,
    SlideDown,
    SlideLeft,
    SlideRight,
    Zoom,
    Pop,
    Typewriter,
}

/// <summary>
/// Aparência do texto (RF136–RF138). Tamanhos e distâncias em pixels do projeto; cores em "#AARRGGBB" ou "#RRGGBB".
/// </summary>
public sealed record TextStyle
{
    public string FontFamily { get; init; } = "Segoe UI";
    public double FontSize { get; init; } = 72;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public string Color { get; init; } = "#FFFFFFFF";
    public TextAlign Align { get; init; } = TextAlign.Center;

    /// <summary>Altura da linha em relação ao tamanho da fonte (1 = padrão da fonte).</summary>
    public double LineSpacing { get; init; } = 1;

    /// <summary>Largura máxima do bloco (fração da largura do quadro); o texto quebra linhas além dela.</summary>
    public double MaxWidth { get; init; } = 0.9;

    public string OutlineColor { get; init; } = "#FF000000";

    /// <summary>Espessura do contorno (0 = sem contorno).</summary>
    public double OutlineWidth { get; init; }

    public string ShadowColor { get; init; } = "#B4000000";

    /// <summary>Sombra ligada (deslocamento e desfoque abaixo).</summary>
    public bool Shadow { get; init; }

    public double ShadowOffsetX { get; init; } = 4;
    public double ShadowOffsetY { get; init; } = 4;
    public double ShadowBlur { get; init; } = 6;

    /// <summary>Caixa de fundo atrás do texto (transparente = sem caixa).</summary>
    public string BackgroundColor { get; init; } = "#00000000";

    public double BackgroundPadding { get; init; } = 16;
    public double BackgroundRadius { get; init; } = 6;

    /// <summary>Barra de destaque à esquerda da caixa (tarjas); transparente = sem barra.</summary>
    public string AccentColor { get; init; } = "#00000000";
}

/// <summary>Formatação de um clipe de texto: tipo, posição do bloco, estilo e animações.</summary>
public sealed record TextFormat
{
    public static readonly TextFormat Default = new();

    public TextKind Kind { get; init; } = TextKind.Text;
    public TextStyle Style { get; init; } = new();
    public TextAnchor Anchor { get; init; } = TextAnchor.Center;
    public TextAnimationKind AnimationIn { get; init; } = TextAnimationKind.None;
    public TextAnimationKind AnimationOut { get; init; } = TextAnimationKind.None;
    public MediaTime AnimationInDuration { get; init; } = MediaTime.FromSecondsApprox(0.5);
    public MediaTime AnimationOutDuration { get; init; } = MediaTime.FromSecondsApprox(0.5);
}
