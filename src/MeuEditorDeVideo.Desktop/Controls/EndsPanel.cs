// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Controls;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Dois grupos nas pontas da mesma linha. Quando não cabem lado a lado (janela estreita, idioma com textos longos),
/// o segundo desce para a linha de baixo.
/// </summary>
public sealed class EndsPanel : Panel
{
    private const double Gap = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, widest = 0, tallest = 0, stacked = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width += child.DesiredSize.Width;
            widest = Math.Max(widest, child.DesiredSize.Width);
            tallest = Math.Max(tallest, child.DesiredSize.Height);
            stacked += child.DesiredSize.Height;
        }
        var gaps = Math.Max(0, Children.Count - 1) * Gap;
        return width + gaps <= availableSize.Width ? new Size(width + gaps, tallest) : new Size(widest, stacked + gaps);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Children.Sum(c => c.DesiredSize.Width) + Math.Max(0, Children.Count - 1) * Gap;
        var oneLine = width <= finalSize.Width + 0.5;
        double top = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var size = Children[i].DesiredSize;
            var x = i == 0 ? 0 : Math.Max(0, finalSize.Width - size.Width);
            Children[i].Arrange(new Rect(x, top, size.Width, oneLine ? finalSize.Height : size.Height));
            if (!oneLine) top += size.Height + Gap;
        }
        return finalSize;
    }
}
