using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Em telas pequenas (ou com escala alta do sistema), reduz o conteúdo inteiro em vez de cortá-lo: abaixo da área
/// necessária, o filho recebe essa área e é desenhado menor. Nunca amplia. A área padrão é a do editor; os diálogos
/// informam a deles (<c>JanelasNaTela</c>). O filho direto não pode ter margem, que ficaria fora da escala.
/// </summary>
public sealed class AjusteDeEscala : Decorator
{
    /// <summary>Menor largura em que os três modos do layout cabem (763 + 3 + 400, com folga).</summary>
    public const double LarguraBase = 1180;

    public const double AlturaBase = 680;

    private double _larguraExtra;

    /// <summary>Largura abaixo da qual o conteúdo é reduzido.</summary>
    public double LarguraMinima { get; init; } = LarguraBase;

    /// <summary>Altura abaixo da qual o conteúdo é reduzido; NaN: a que o próprio conteúdo pedir.</summary>
    public double AlturaMinima { get; init; } = AlturaBase;

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
        if (double.IsNaN(AlturaMinima)) return MedirPeloConteudo(child, availableSize);
        if (!double.IsFinite(availableSize.Width) || !double.IsFinite(availableSize.Height)) return base.MeasureOverride(availableSize);
        if (availableSize.Width > 0 && availableSize.Height > 0)
            Aplicar(child, Math.Min(1, Math.Min(availableSize.Width / (LarguraMinima + LarguraExtra), availableSize.Height / AlturaMinima)));
        child.Measure(availableSize / Escala);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize / Escala));
        return finalSize;
    }

    /// <summary>Janela com a altura do conteúdo: mede sem limite de altura e reduz se não couber.</summary>
    private Size MedirPeloConteudo(Control child, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0) return default;
        child.Measure(new Size(Math.Max(area.Width, LarguraMinima), double.PositiveInfinity));
        var natural = child.DesiredSize;
        var escala = Math.Min(1, area.Width / LarguraMinima);
        if (natural.Height > 0) escala = Math.Min(escala, area.Height / natural.Height);
        Aplicar(child, escala);
        // Mais largo quando reduzido, o texto quebra menos: a altura só diminui.
        child.Measure(new Size(area.Width / escala, double.PositiveInfinity));
        return new Size(area.Width, Math.Min(area.Height, child.DesiredSize.Height * escala));
    }

    private void Aplicar(Control child, double escala)
    {
        if (escala == Escala) return;
        Escala = escala;
        child.RenderTransformOrigin = RelativePoint.TopLeft;
        child.RenderTransform = escala < 1 ? new ScaleTransform(escala, escala) : null;
        EscalaMudou?.Invoke(this, EventArgs.Empty);
    }
}
