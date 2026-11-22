using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Em telas pequenas (ou com escala alta do sistema), reduz o conteúdo inteiro em vez de cortá-lo: abaixo da área
/// necessária, o filho recebe essa área e é desenhado menor. Nunca amplia.
/// </summary>
public sealed class AjusteDeEscala : Decorator
{
    /// <summary>Menor largura em que os três modos do layout cabem (763 + 3 + 400, com folga).</summary>
    public const double LarguraBase = 1180;

    public const double AlturaBase = 680;

    private double _larguraExtra;

    public AjusteDeEscala() => ClipToBounds = true;

    /// <summary>Largura além da base que o conteúdo pede (coluna do assistente).</summary>
    public double LarguraExtra
    {
        get => _larguraExtra;
        set
        {
            if (_larguraExtra == value) return;
            _larguraExtra = value;
            InvalidateMeasure();
        }
    }

    public double Escala { get; private set; } = 1;

    public event EventHandler? EscalaMudou;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;
        if (!double.IsFinite(availableSize.Width) || !double.IsFinite(availableSize.Height)) return base.MeasureOverride(availableSize);
        Atualizar(child, availableSize);
        child.Measure(availableSize / Escala);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize / Escala));
        return finalSize;
    }

    private void Atualizar(Control child, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        var escala = Math.Min(1, Math.Min(area.Width / (LarguraBase + LarguraExtra), area.Height / AlturaBase));
        if (escala == Escala) return;
        Escala = escala;
        child.RenderTransformOrigin = RelativePoint.TopLeft;
        child.RenderTransform = escala < 1 ? new ScaleTransform(escala, escala) : null;
        EscalaMudou?.Invoke(this, EventArgs.Empty);
    }
}
