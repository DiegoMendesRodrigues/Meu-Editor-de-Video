using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Composition;
using MeuEditorDeVideo.ImageEngine.Effects;
using MeuEditorDeVideo.ImageEngine.Text;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>
/// Renderiza um quadro da sequência: avalia a timeline, decodifica as camadas, mistura quadros (câmera lenta suavizada),
/// aplica efeitos/cor/chroma key e compõe. Compartilhado pela pré-visualização e pela exportação, garantindo que
/// o resultado exportado seja o mesmo que se vê na tela. Efeitos de IA (<c>ai.*</c>) são executados por
/// <see cref="IAiFrameProcessor"/> na posição em que estão na pilha de efeitos.
/// </summary>
public sealed class FrameRenderer(ICompositor compositor, IAiFrameProcessor? ai = null)
{
    /// <param name="bypassEffects">Ignora efeitos e correção de cor (lado "antes" da comparação, RF101).</param>
    public BgraImage Render(IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        MediaTime t, int width, int height, bool transparentBackground = false, bool bypassEffects = false, bool includeSubtitles = true)
    {
        var graph = TimelineEvaluator.Evaluate(project, sequence, t, includeSubtitles);
        var layers = new List<LayerImage>(graph.Layers.Count);
        var ctx = new EffectContext(graph.Width > 0 ? (double)width / graph.Width : 1, project.Settings.FrameRate.TimeToFrame(t));
        foreach (var visual in graph.Layers)
        {
            var layer = visual;
            if (layer.Text is { } text)
            {
                if (TextImage(layer, text, graph, width, height) is { } textLayer)
                {
                    var (img, positioned) = textLayer;
                    if (layer.Masks.Count > 0) img = MaskRenderer.Apply(img, layer.Masks);
                    if (!bypassEffects && positioned.Effects.Count > 0) img = EffectProcessor.Apply(img, positioned.Effects, ctx).Image;
                    layers.Add(new LayerImage(positioned, img, HasAlpha: true, NativeSize: true));
                }
                continue;
            }
            if (layer.Shape is { } shape)
            {
                var img = ShapeRasterizer.Render(shape, graph.Width > 0 ? (double)width / graph.Width : 1);
                if (layer.Masks.Count > 0) img = MaskRenderer.Apply(img, layer.Masks);
                if (!bypassEffects && layer.Effects.Count > 0) img = EffectProcessor.Apply(img, layer.Effects, ctx).Image;
                layers.Add(new LayerImage(layer, img, HasAlpha: true, NativeSize: true));
                continue;
            }
            if (layer.Asset is null || !paths.TryGetValue(layer.Asset.Id, out var path)) continue;
            var image = video.GetFrame(layer.Asset, path, layer.SourceTime, width, height);
            if (image is null) continue;

            if (layer.BlendSourceTime is { } next && video.GetFrame(layer.Asset, path, next, width, height) is { } second)
                image = EffectProcessor.Mix(image, second, layer.BlendWeight);

            var hasAlpha = layer.Asset.Info?.HasAlpha == true || layer.Asset.Kind == MediaKind.Image;
            if (layer.Masks.Count > 0)
            {
                // Máscaras antes dos efeitos: "Refinar bordas" e desfoques atuam sobre o recorte.
                image = MaskRenderer.Apply(image, layer.Masks);
                hasAlpha = true;
            }
            if (!bypassEffects && layer.Effects.Count > 0)
            {
                var (processed, alphaChanged) = ApplyEffects(image, layer, ctx, video, project, sequence, paths, t);
                image = processed;
                hasAlpha |= alphaChanged;
            }
            layers.Add(new LayerImage(layer, image, hasAlpha));
        }
        return compositor.Compose(width, height, graph.Width, layers, transparentBackground);
    }

    /// <summary>Efeitos comuns em blocos, intercalados com os efeitos de IA na ordem da pilha.</summary>
    private (BgraImage Image, bool AlphaChanged) ApplyEffects(BgraImage image, VisualLayer layer, EffectContext ctx,
        IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths, MediaTime t)
    {
        var effects = layer.Effects;
        if (!effects.Any(e => e.Enabled && EffectCatalog.Find(e.Type)?.Category == EffectCategory.Ai))
            return EffectProcessor.Apply(image, effects, ctx);

        var alpha = false;
        var pending = new List<EffectInstance>();
        void Flush()
        {
            if (pending.Count == 0) return;
            var (img, changed) = EffectProcessor.Apply(image, pending, ctx);
            image = img;
            alpha |= changed;
            pending.Clear();
        }
        foreach (var e in effects)
        {
            if (EffectCatalog.Find(e.Type)?.Category != EffectCategory.Ai)
            {
                pending.Add(e);
                continue;
            }
            Flush();
            if (!e.Enabled || ai is null || layer.Asset is null) continue;
            var (processed, changed) = ai.Apply(e, image, layer.Asset, layer.SourceTime,
                (w, h) => BackgroundFrame(e, video, project, sequence, paths, t, layer, w, h), ctx.PixelScale);
            image = processed;
            alpha |= changed;
        }
        Flush();
        return (image, alpha);
    }

    /// <summary>
    /// Quadro de fundo do modo "imagem ou vídeo" (<see cref="EffectInstance.Resource"/> = Id da mídia): preenche o tamanho
    /// pedido (recorte central). Vídeos acompanham o tempo do clipe e repetem quando acabam.
    /// </summary>
    private static BgraImage? BackgroundFrame(EffectInstance e, IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        MediaTime t, VisualLayer layer, int width, int height)
    {
        if (!Guid.TryParse(e.Resource, out var id) || project.FindMedia(id) is not { } asset || !paths.TryGetValue(id, out var path)) return null;
        var local = MediaTime.Zero;
        if (asset.Kind == MediaKind.Video && asset.Duration.Ticks > 0 
            && sequence.Tracks.SelectMany(tr => tr.Clips).FirstOrDefault(c => c.Id == layer.ClipId) is { } clip)
            local = new MediaTime(Math.Max(0, (t - clip.Start).Ticks) % asset.Duration.Ticks);
        // Decodifica maior que o alvo para recortar sem bordas.
        var (sw, sh) = (asset.Width, asset.Height);
        var cover = sw > 0 && sh > 0 ? Math.Max((double)width / sw, (double)height / sh) : 1;
        var frame = video.GetFrame(asset, path, local, (int)Math.Ceiling(sw * cover), (int)Math.Ceiling(sh * cover));
        return frame is null ? null : CoverCrop(frame, width, height);
    }

    private static BgraImage CoverCrop(BgraImage src, int width, int height)
    {
        if (src.Width == width && src.Height == height) return src;
        var scale = Math.Max((double)width / src.Width, (double)height / src.Height);
        var px = new byte[width * height * 4];
        var ox = (src.Width * scale - width) / 2;
        var oy = (src.Height * scale - height) / 2;
        Parallel.For(0, height, y =>
        {
            var sy = Math.Clamp((int)((y + oy) / scale), 0, src.Height - 1);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((int)((x + ox) / scale), 0, src.Width - 1);
                Buffer.BlockCopy(src.Pixels, (sy * src.Width + sx) * 4, px, (y * width + x) * 4, 4);
            }
        });
        return new BgraImage(width, height, px);
    }

    /// <summary>
    /// Rasteriza o texto na escala de saída e converte a posição do clipe (ponto de ancoragem do bloco, conforme
    /// alinhamento e âncora vertical) no centro da imagem; nos créditos, calcula a rolagem de baixo para cima.
    /// </summary>
    private static (BgraImage Image, VisualLayer Layer)? TextImage(VisualLayer layer, TextLayer text, CompositionGraph graph, int width, int height)
    {
        var scale = graph.Width > 0 ? (double)width / graph.Width : 1;
        var raster = TextRasterizer.Render(text.Text, text.VisibleCharacters, text.Format.Style, scale, width);
        var boxW = raster.BoxWidth / scale;
        var boxH = raster.BoxHeight / scale;
        var t = layer.Transform;
        var x = text.Format.Style.Align switch
        {
            TextAlign.Left => t.X + boxW / 2,
            TextAlign.Right => t.X - boxW / 2,
            _ => t.X,
        };
        var y = text.Format.Anchor switch
        {
            TextAnchor.Top => t.Y + boxH / 2,
            TextAnchor.Bottom => t.Y - boxH / 2,
            _ => t.Y,
        };
        if (text.Roll is { } roll)
        {
            // Entra pela base (bloco todo abaixo do quadro) e sai pelo topo.
            var h = graph.Height;
            y = t.Y + (h / 2.0 + boxH / 2) - roll * (h + boxH);
            if (Math.Abs(y - t.Y) > h / 2.0 + boxH / 2 + 1) return null;
        }
        return (raster.Image, layer with { Transform = t with { X = x, Y = y } });
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
