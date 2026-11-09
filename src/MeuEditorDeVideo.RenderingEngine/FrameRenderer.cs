using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Effects;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>
/// Renderiza um quadro da sequência: avalia a timeline, decodifica as camadas, mistura quadros (câmera lenta suavizada),
/// aplica efeitos/cor/chroma key e compõe. Compartilhado pela pré-visualização e pela exportação, garantindo que
/// o resultado exportado seja o mesmo que se vê na tela.
/// </summary>
public sealed class FrameRenderer(ICompositor compositor)
{
    /// <param name="bypassEffects">Ignora efeitos e correção de cor (lado "antes" da comparação, RF101).</param>
    public BgraImage Render(IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        MediaTime t, int width, int height, bool transparentBackground = false, bool bypassEffects = false)
    {
        var graph = TimelineEvaluator.Evaluate(project, sequence, t);
        var layers = new List<LayerImage>(graph.Layers.Count);
        var ctx = new EffectContext(graph.Width > 0 ? (double)width / graph.Width : 1, project.Settings.FrameRate.TimeToFrame(t));
        foreach (var layer in graph.Layers)
        {
            if (!paths.TryGetValue(layer.Asset.Id, out var path)) continue;
            var image = video.GetFrame(layer.Asset, path, layer.SourceTime, width, height);
            if (image is null) continue;

            if (layer.BlendSourceTime is { } next && video.GetFrame(layer.Asset, path, next, width, height) is { } second)
                image = EffectProcessor.Mix(image, second, layer.BlendWeight);

            var hasAlpha = layer.Asset.Info?.HasAlpha == true || layer.Asset.Kind == MediaKind.Image;
            if (!bypassEffects && layer.Effects.Count > 0)
            {
                var (processed, alphaChanged) = EffectProcessor.Apply(image, layer.Effects, ctx);
                image = processed;
                hasAlpha |= alphaChanged;
            }
            layers.Add(new LayerImage(layer, image, hasAlpha));
        }
        return compositor.Compose(width, height, graph.Width, layers, transparentBackground);
    }

    /// <summary>Comparação antes/depois lado a lado: metade esquerda sem efeitos, metade direita com efeitos (RF101).</summary>
    public BgraImage RenderCompare(IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        MediaTime t, int width, int height, double split = 0.5)
    {
        var after = Render(video, project, sequence, paths, t, width, height);
        var before = Render(video, project, sequence, paths, t, width, height, bypassEffects: true);
        var px = after.Pixels;
        var cut = Math.Clamp((int)Math.Round(width * split), 0, width);
        Parallel.For(0, height, y =>
        {
            var row = y * width * 4;
            Buffer.BlockCopy(before.Pixels, row, px, row, cut * 4);
            // Linha divisória branca de 2 px.
            for (var x = Math.Max(0, cut - 1); x < Math.Min(width, cut + 1); x++)
            {
                var i = row + x * 4;
                px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 255;
            }
        });
        return after;
    }
}
