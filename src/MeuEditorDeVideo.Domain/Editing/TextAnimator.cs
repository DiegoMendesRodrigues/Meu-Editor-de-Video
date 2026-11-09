using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Efeito das animações de entrada/saída de um texto num instante (RF139).</summary>
/// <param name="Opacity">Multiplicador de opacidade.</param>
/// <param name="OffsetX">Deslocamento horizontal em pixels do projeto.</param>
/// <param name="OffsetY">Deslocamento vertical em pixels do projeto.</param>
/// <param name="Scale">Multiplicador de escala.</param>
/// <param name="Visible">Fração de caracteres visíveis (máquina de escrever).</param>
public readonly record struct TextAnimationState(double Opacity, double OffsetX, double OffsetY, double Scale, double Visible)
{
    public static readonly TextAnimationState Identity = new(1, 0, 0, 1, 1);
}

/// <summary>
/// Calcula animações de texto. A entrada vai de "oculto" a "visível" em <see cref="TextFormat.AnimationInDuration"/>;
/// a saída faz o caminho inverso no fim do clipe, continuando o movimento (ex.: "subir" entra por baixo e sai por cima).
/// Se as duas durações não couberem no clipe, são reduzidas proporcionalmente.
/// </summary>
public static class TextAnimator
{
    /// <summary>Distância dos deslizamentos: fração da largura (horizontal) ou altura (vertical) do quadro.</summary>
    public const double SlideFraction = 0.08;

    public static TextAnimationState At(Clip clip, MediaTime offset, int frameWidth, int frameHeight)
    {
        var format = clip.TextFormat ?? TextFormat.Default;
        if (format.AnimationIn == TextAnimationKind.None && format.AnimationOut == TextAnimationKind.None) return TextAnimationState.Identity;

        double total = clip.Duration.Ticks;
        double inTicks = format.AnimationIn == TextAnimationKind.None ? 0 : Math.Max(0, format.AnimationInDuration.Ticks);
        double outTicks = format.AnimationOut == TextAnimationKind.None ? 0 : Math.Max(0, format.AnimationOutDuration.Ticks);
        if (inTicks + outTicks > total && inTicks + outTicks > 0)
        {
            var k = total / (inTicks + outTicks);
            inTicks *= k;
            outTicks *= k;
        }

        var state = TextAnimationState.Identity;
        var t = Math.Clamp((double)offset.Ticks, 0, total);
        if (inTicks > 0 && t < inTicks)
            state = Apply(state, format.AnimationIn, t / inTicks, entering: true, frameWidth, frameHeight);
        var remaining = total - t;
        if (outTicks > 0 && remaining < outTicks)
            state = Apply(state, format.AnimationOut, remaining / outTicks, entering: false, frameWidth, frameHeight);
        return state;
    }

    /// <param name="p">0 = oculto, 1 = totalmente visível.</param>
    private static TextAnimationState Apply(TextAnimationState s, TextAnimationKind kind, double p, bool entering, int w, int h)
    {
        p = Math.Clamp(p, 0, 1);
        var e = 1 - Math.Pow(1 - p, 3); // desacelera ao chegar
        var sign = entering ? 1 : -1;
        switch (kind)
        {
            case TextAnimationKind.Fade:
                return s with { Opacity = s.Opacity * p };
            case TextAnimationKind.SlideUp:
                return s with { Opacity = s.Opacity * p, OffsetY = s.OffsetY + sign * (1 - e) * h * SlideFraction };
            case TextAnimationKind.SlideDown:
                return s with { Opacity = s.Opacity * p, OffsetY = s.OffsetY - sign * (1 - e) * h * SlideFraction };
            case TextAnimationKind.SlideLeft:
                return s with { Opacity = s.Opacity * p, OffsetX = s.OffsetX + sign * (1 - e) * w * SlideFraction };
            case TextAnimationKind.SlideRight:
                return s with { Opacity = s.Opacity * p, OffsetX = s.OffsetX - sign * (1 - e) * w * SlideFraction };
            case TextAnimationKind.Zoom:
                return s with { Opacity = s.Opacity * p, Scale = s.Scale * (0.5 + 0.5 * e) };
            case TextAnimationKind.Pop:
                return s with { Opacity = s.Opacity * Math.Min(1, p * 3), Scale = s.Scale * Math.Max(0.01, EaseOutBack(p)) };
            case TextAnimationKind.Typewriter:
                return s with { Visible = Math.Min(s.Visible, p) };
            default:
                return s;
        }
    }

    /// <summary>Passa um pouco do tamanho final e volta (efeito "pop").</summary>
    public static double EaseOutBack(double p)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(p - 1, 3) + c1 * Math.Pow(p - 1, 2);
    }

    /// <summary>Quantidade de caracteres visíveis para a fração, sem cortar pares substitutos (emoji).</summary>
    public static int VisibleCharacters(string text, double fraction)
    {
        if (fraction >= 1) return text.Length;
        var n = (int)Math.Round(text.Length * Math.Clamp(fraction, 0, 1));
        if (n > 0 && n < text.Length && char.IsHighSurrogate(text[n - 1])) n--;
        return n;
    }
}
