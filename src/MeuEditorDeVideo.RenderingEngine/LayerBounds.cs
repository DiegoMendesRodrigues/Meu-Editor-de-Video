// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Composition;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>
/// Caixa de um clipe no quadro, em pixels do projeto relativos ao centro: centro, largura, altura (já com a escala do
/// clipe) e giro no plano, em graus. Usada pelas alças de mover, girar e redimensionar no monitor.
/// </summary>
public readonly record struct LayerBox(double CenterX, double CenterY, double Width, double Height, double RotationDegrees);

public static class LayerBounds
{
    /// <summary>Caixa do clipe no instante, ou nulo se ele não estiver visível (ou não tiver tamanho conhecido).</summary>
    public static LayerBox? Of(Project project, Sequence sequence, Guid clipId, MediaTime t)
    {
        var graph = TimelineEvaluator.Evaluate(project, sequence, t);
        var layer = graph.Layers.FirstOrDefault(l => l.ClipId == clipId);
        if (layer is null) return null;

        double w, h;
        var tr = layer.Transform;
        if (layer.Text is { } text)
        {
            // Mesmo posicionamento do renderizador (alinhamento e âncora), rasterizado na escala do projeto.
            if (FrameRenderer.TextImage(layer, text, graph, graph.Width) is not { } positioned) return null;
            tr = positioned.Layer.Transform;
            (w, h) = (positioned.Image.Width, positioned.Image.Height);
        }
        else if (layer.Shape is { } shape)
        {
            var img = ShapeRasterizer.Render(shape, 1);
            (w, h) = (img.Width, img.Height);
        }
        else if (layer.Asset is { Width: > 0, Height: > 0 } asset)
        {
            var fit = CompositionMath.FitScale(asset.Width, asset.Height, graph.Width, graph.Height);
            (w, h) = (asset.Width * fit, asset.Height * fit);
        }
        else
        {
            return null;
        }
        return new LayerBox(tr.X, tr.Y, w * Math.Abs(tr.ScaleX), h * Math.Abs(tr.ScaleY), tr.RotationDegrees);
    }
}
