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
public sealed class FrameRenderer(ICompositor compositor, IAiFrameProcessor? ai = null, IWaveformProvider? waveforms = null)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, WaveformData?> _waveforms = new();

    /// <param name="bypassEffects">Ignora efeitos e correção de cor (lado "antes" da comparação, RF101).</param>
    public BgraImage Render(IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        MediaTime t, int width, int height, bool transparentBackground = false, bool bypassEffects = false, bool includeSubtitles = true) =>
        RenderCore(video, project, sequence, paths, t, width, height, transparentBackground, bypassEffects, includeSubtitles, depth: 0);

    /// <summary>Profundidade máxima de clipes compostos dentro de clipes compostos.</summary>
    private const int MaxNesting = 4;

    private BgraImage RenderCore(IVideoFrameSource video, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths,
        MediaTime t, int width, int height, bool transparentBackground, bool bypassEffects, bool includeSubtitles, int depth)
    {
        var graph = TimelineEvaluator.Evaluate(project, sequence, t, includeSubtitles);
        var layers = new List<LayerImage>(graph.Layers.Count);
        var ctx = new EffectContext(graph.Width > 0 ? (double)width / graph.Width : 1, project.Settings.FrameRate.TimeToFrame(t));
        foreach (var visual in graph.Layers)
        {
            var layer = visual;
            if (layer.Text is { } text)
            {
                if (TextImage(layer, text, graph, width) is { } textLayer)
                {
                    var (img, positioned) = textLayer;
                    if (layer.Masks.Count > 0) img = MaskRenderer.Apply(img, layer.Masks);
                    if (!bypassEffects && positioned.Effects.Count > 0) img = EffectProcessor.Apply(PadForEffects(img, positioned.Effects, ctx), positioned.Effects, ctx).Image;
                    layers.Add(new LayerImage(positioned, img, HasAlpha: true, NativeSize: true));
                }
                continue;
            }
            if (layer.Shape is { } shape)
            {
                var img = ShapeRasterizer.Render(shape, graph.Width > 0 ? (double)width / graph.Width : 1);
                if (layer.Masks.Count > 0) img = MaskRenderer.Apply(img, layer.Masks);
                if (!bypassEffects && layer.Effects.Count > 0) img = EffectProcessor.Apply(PadForEffects(img, layer.Effects, ctx), layer.Effects, ctx).Image;
                AddWithTrail(layers, new LayerImage(layer, img, HasAlpha: true, NativeSize: true));
                continue;
            }
            if (layer.IsAdjustment)
            {
                if (!bypassEffects && layer.Effects.Count > 0)
                    ApplyAdjustment(layers, layer, ctx, video, project, sequence, paths, t, width, height, graph.Width, transparentBackground);
                continue;
            }

            BgraImage? image;
            bool hasAlpha;
            if (layer.NestedSequenceId is { } nestedId)
            {
                // Clipe composto: a sequência aninhada é renderizada inteira (fundo transparente) e vira uma camada.
                if (depth >= MaxNesting || nestedId == sequence.Id || project.Sequences.Find(s => s.Id == nestedId) is not { } nested) continue;
                image = RenderCore(video, project, nested, paths, layer.SourceTime, width, height, true, bypassEffects, includeSubtitles, depth + 1);
                hasAlpha = true;
            }
            else
            {
                if (layer.Asset is null || !paths.TryGetValue(layer.Asset.Id, out var path)) continue;
                image = video.GetFrame(layer.Asset, path, layer.SourceTime, width, height);
                if (image is null) continue;

                if (layer.BlendSourceTime is { } next && video.GetFrame(layer.Asset, path, next, width, height) is { } second)
                    image = (layer.OpticalFlow ? ai?.Interpolate(image, second, layer.BlendWeight) : null)
                            ?? EffectProcessor.Mix(image, second, layer.BlendWeight);

                hasAlpha = layer.Asset.Info?.HasAlpha == true || layer.Asset.Kind == MediaKind.Image;
            }
            var cut = layer.Masks.Where(m => !m.EffectsOnly).ToList();
            var colorMasks = layer.Masks.Where(m => m.EffectsOnly).ToList();
            if (cut.Count > 0)
            {
                // Máscaras antes dos efeitos: "Refinar bordas" e desfoques atuam sobre o recorte.
                image = MaskRenderer.Apply(image, cut);
                hasAlpha = true;
            }
            if (!bypassEffects && layer.Effects.Count > 0)
            {
                var original = image;
                var (processed, alphaChanged) = ApplyEffects(image, layer, ctx, video, project, sequence, paths, t);
                image = processed;
                hasAlpha |= alphaChanged;
                foreach (var viz in layer.Effects.Where(e => e.Type == EffectCatalog.AudioVisualizer && !EffectCatalog.IsNeutral(e)))
                    image = EffectProcessor.DrawVisualizer(image, VisualizerLevels(viz, project, sequence, paths, t), viz);
                // Máscara de cor: os efeitos só valem dentro da área (com a borda suavizada da máscara).
                if (colorMasks.Count > 0 && original.Width == image.Width && original.Height == image.Height
                    && MaskRenderer.Coverage(image.Width, image.Height, colorMasks) is { } cov)
                    image = MaskedMix(original, image, cov);
            }
            AddWithTrail(layers, new LayerImage(layer, image, hasAlpha));
        }
        return compositor.Compose(width, height, graph.Width, layers, transparentBackground);
    }

    /// <summary>
    /// Camada de ajuste: compõe o que está abaixo, aplica os efeitos ao quadro (limitados às máscaras, se houver, e misturados
    /// pela opacidade) e troca as camadas anteriores por esse quadro já pronto.
    /// </summary>
    private void ApplyAdjustment(List<LayerImage> layers, VisualLayer layer, EffectContext ctx, IVideoFrameSource video, Project project,
        Sequence sequence, IReadOnlyDictionary<Guid, string> paths, MediaTime t, int width, int height, int projectWidth, bool transparentBackground)
    {
        var below = compositor.Compose(width, height, projectWidth, layers, transparentBackground);
        var image = ApplyEffects(below, layer, ctx, video, project, sequence, paths, t).Image;
        if (image.Width != width || image.Height != height) return;
        if (layer.Masks.Count > 0 && MaskRenderer.Coverage(width, height, layer.Masks) is { } cov)
            image = MaskedMix(below, image, cov);
        if (layer.Opacity < 1) image = EffectProcessor.Mix(below, image, layer.Opacity);
        layers.Clear();
        layers.Add(new LayerImage(layer with { Opacity = 1, Transform = Transform.Identity, MotionTrail = [], Mask = null }, image,
            HasAlpha: transparentBackground, NativeSize: true));
    }

    /// <summary>
    /// Níveis das barras do visualizador: a música (<see cref="EffectInstance.Resource"/> = Id da mídia) é lida no instante
    /// em que toca na linha do tempo (o clipe dela que cobre <paramref name="t"/>); fora dela, barras zeradas.
    /// </summary>
    private IReadOnlyList<float> VisualizerLevels(EffectInstance e, Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths, MediaTime t)
    {
        var bars = Math.Clamp((int)Math.Round(EffectCatalog.Value(e, "bars")), 8, 128);
        var levels = new float[bars];
        if (waveforms is null || !Guid.TryParse(e.Resource, out var id) || project.FindMedia(id) is not { } asset || !paths.TryGetValue(id, out var path))
            return levels;
        var clip = sequence.Tracks.SelectMany(tr => tr.Clips).FirstOrDefault(c => c.MediaAssetId == id && c.Start <= t && t < c.End);
        if (clip is null) return levels;
        var data = _waveforms.GetOrAdd(id, _ =>
        {
            try { return waveforms.GetWaveformAsync(asset, path).GetAwaiter().GetResult(); }
            catch (Exception) { return null; }
        });
        if (data is null || data.Count == 0) return levels;
        return AudioVisualizerLevels(data, clip.SourceTimeAt(t), bars);
    }

    /// <summary>
    /// Níveis (0…1) de cada barra: picos da forma de onda numa janela de ~0,4 s ao redor do instante, com as barras
    /// espalhadas na janela (passado à esquerda, futuro à direita) e reforço nos sons fracos.
    /// </summary>
    public static float[] AudioVisualizerLevels(WaveformData data, MediaTime source, int bars)
    {
        var levels = new float[bars];
        var center = source.TotalSeconds * data.PeaksPerSecond;
        var window = Math.Max(bars, 0.4 * data.PeaksPerSecond);
        for (var i = 0; i < bars; i++)
        {
            var a = (int)Math.Floor(center - window / 2 + window * i / bars);
            var b = Math.Max(a + 1, (int)Math.Floor(center - window / 2 + window * (i + 1) / bars));
            var peak = 0f;
            for (var j = Math.Max(0, a); j < Math.Min(data.Count, b); j++)
                peak = Math.Max(peak, Math.Max(Math.Abs(data.Min[j]), Math.Abs(data.Max[j])));
            levels[i] = (float)Math.Sqrt(Math.Clamp(peak, 0, 1));
        }
        return levels;
    }

    /// <summary>
    /// Desfoque de movimento: desenha a mesma imagem nas posições anteriores e na atual com opacidade 1/k (k = 1…N), o que dá
    /// peso igual a cada amostra onde elas se sobrepõem.
    /// </summary>
    private static void AddWithTrail(List<LayerImage> layers, LayerImage item)
    {
        var trail = item.Layer.MotionTrail;
        if (trail.Count == 0)
        {
            layers.Add(item);
            return;
        }
        var n = trail.Count + 1;
        for (var k = 1; k <= n; k++)
        {
            var transform = k < n ? trail[k - 1] : item.Layer.Transform;
            layers.Add(item with { Layer = item.Layer with { Transform = transform, Opacity = item.Layer.Opacity / k }, HasAlpha = true });
        }
    }

    /// <summary>Mistura por pixel: <paramref name="processed"/> onde a cobertura é 255 e <paramref name="original"/> onde é 0.</summary>
    public static BgraImage MaskedMix(BgraImage original, BgraImage processed, byte[] coverage)
    {
        var a = original.Pixels;
        var b = processed.Pixels;
        var px = new byte[b.Length];
        Parallel.For(0, processed.Height, y =>
        {
            var row = y * processed.Width;
            for (var x = 0; x < processed.Width; x++)
            {
                var i = row + x;
                var w = coverage[i];
                for (var c = 0; c < 4; c++)
                {
                    var j = i * 4 + c;
                    px[j] = (byte)((b[j] * w + a[j] * (255 - w) + 127) / 255);
                }
            }
        });
        return new BgraImage(processed.Width, processed.Height, px);
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
            var (processed, changed) = ai.Apply(e, image, layer.Asset, paths.GetValueOrDefault(layer.Asset.Id), layer.SourceTime,
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
    /// Imagens em tamanho nativo (textos, formas) são justas ao conteúdo: desfoque e brilho seriam cortados na borda.
    /// Acrescenta margem transparente simétrica (o centro, e portanto a posição, não muda).
    /// </summary>
    private static BgraImage PadForEffects(BgraImage img, IReadOnlyList<EffectInstance> effects, EffectContext ctx)
    {
        double radius = 0;
        foreach (var e in effects)
            if (e.Type is EffectCatalog.Blur or EffectCatalog.Glow && !EffectCatalog.IsNeutral(e))
                radius = Math.Max(radius, EffectCatalog.Value(e, "radius") * ctx.PixelScale);
        var pad = (int)Math.Ceiling(radius * 2.5);
        if (pad <= 0) return img;
        var w = img.Width + pad * 2;
        var h = img.Height + pad * 2;
        var px = new byte[w * h * 4];
        for (var y = 0; y < img.Height; y++)
            Buffer.BlockCopy(img.Pixels, y * img.Width * 4, px, ((y + pad) * w + pad) * 4, img.Width * 4);
        return new BgraImage(w, h, px);
    }

    /// <summary>
    /// Rasteriza o texto na escala de saída e converte a posição do clipe (ponto de ancoragem do bloco, conforme
    /// alinhamento e âncora vertical) no centro da imagem; nos créditos, calcula a rolagem de baixo para cima.
    /// </summary>
    internal static (BgraImage Image, VisualLayer Layer)? TextImage(VisualLayer layer, TextLayer text, CompositionGraph graph, int width)
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
