using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.TimelineEngine;

/// <summary>Máscara de transição avaliada em coordenadas do quadro de saída (cortinas e formas geométricas).</summary>
public readonly record struct TransitionMask(TransitionKind Kind, double Progress);

/// <summary>Camada visual de um quadro: mídia, instante de origem, transformação, opacidade e efeitos ativos.</summary>
public sealed record VisualLayer(
    Guid ClipId,
    Guid TrackId,
    MediaAsset Asset,
    MediaTime SourceTime,
    Transform Transform,
    double Opacity,
    IReadOnlyList<EffectInstance> Effects)
{
    public BlendMode BlendMode { get; init; }

    /// <summary>Câmera lenta suavizada: segundo quadro de origem misturado com peso <see cref="BlendWeight"/>.</summary>
    public MediaTime? BlendSourceTime { get; init; }

    public double BlendWeight { get; init; }

    public TransitionMask? Mask { get; init; }
}

/// <summary>
/// Grafo de composição de um instante: camadas de baixo para cima (a primeira é desenhada primeiro).
/// É a entrada comum da pré-visualização (F4) e da exportação (F5).
/// </summary>
public sealed record CompositionGraph(MediaTime Time, int Width, int Height, IReadOnlyList<VisualLayer> Layers)
{
    public bool IsEmpty => Layers.Count == 0;
}

/// <summary>Trecho de áudio que soa em parte de uma janela de tempo da timeline.</summary>
public sealed record AudioSegment(
    Guid ClipId,
    MediaAsset Asset,
    MediaTime TimelineStart,
    MediaTime TimelineEnd,
    MediaTime SourceStart,
    Rational Speed,
    double Volume);

/// <summary>
/// Traduz a sequência em "o que aparece/soa em cada instante" (§6.5, §7.2).
/// Regra de empilhamento: trilhas mais acima na lista ficam na frente (como exibidas na linha do tempo).
/// </summary>
public static class TimelineEvaluator
{
    /// <summary>Instante dentro da mídia de origem correspondente ao tempo <paramref name="t"/> da timeline.</summary>
    public static MediaTime SourceTimeAt(Clip clip, MediaTime t) =>
        clip.SourceIn + clip.Speed.SourceDurationOf(t - clip.Start);

    public static CompositionGraph Evaluate(Project project, Sequence sequence, MediaTime t)
    {
        var width = project.Settings.Width;
        var height = project.Settings.Height;
        var layers = new List<VisualLayer>();
        // De baixo para cima: percorre as trilhas da última para a primeira.
        for (var i = sequence.Tracks.Count - 1; i >= 0; i--)
        {
            var track = sequence.Tracks[i];
            if (!track.IsVisual || track.IsHidden) continue;

            if (ActiveTransition(track, t) is { } active)
            {
                var (transition, from, to, progress) = active;
                AddTransitionLayers(project, track, transition.Kind, from, to, progress, t, width, height, layers);
                continue;
            }

            if (ClipAt(track, t) is { } clip && Layer(project, track, clip, t) is { } layer)
                layers.Add(layer);
        }
        return new CompositionGraph(t, width, height, layers);
    }

    /// <summary>Transição cuja janela (centrada no corte) contém o instante, com o progresso 0…1.</summary>
    public static (Transition Transition, Clip From, Clip To, double Progress)? ActiveTransition(Track track, MediaTime t)
    {
        foreach (var tr in track.Transitions)
        {
            if (tr.Duration <= MediaTime.Zero) continue;
            var to = track.Clips.Find(c => c.Id == tr.ToClipId);
            var from = track.Clips.Find(c => c.Id == tr.FromClipId);
            if (to is null || from is null || from.End != to.Start) continue;
            var start = to.Start - new MediaTime(tr.Duration.Ticks / 2);
            var end = start + tr.Duration;
            if (t < start || t >= end) continue;
            return (tr, from, to, Math.Clamp((double)(t - start).Ticks / tr.Duration.Ticks, 0, 1));
        }
        return null;
    }

    private static void AddTransitionLayers(Project project, Track track, TransitionKind kind, Clip from, Clip to, double p,
        MediaTime t, int width, int height, List<VisualLayer> layers)
    {
        var a = Layer(project, track, from, t);
        var b = Layer(project, track, to, t);
        var eased = ClipAnimation.Ease(Easing.EaseInOut, p);

        static VisualLayer? Move(VisualLayer? l, double dx, double dy) =>
            l is null ? null : l with { Transform = l.Transform with { X = l.Transform.X + dx, Y = l.Transform.Y + dy } };

        static VisualLayer? Zoom(VisualLayer? l, double factor) =>
            l is null ? null : l with { Transform = l.Transform with { ScaleX = l.Transform.ScaleX * factor, ScaleY = l.Transform.ScaleY * factor } };

        static VisualLayer? Fade(VisualLayer? l, double factor) => l is null ? null : l with { Opacity = l.Opacity * Math.Clamp(factor, 0, 1) };

        switch (kind)
        {
            case TransitionKind.Dissolve:
                b = Fade(b, p);
                break;
            case TransitionKind.FadeBlack:
                // Primeira metade: o clipe de saída some; segunda metade: o de entrada aparece.
                a = p < 0.5 ? Fade(a, 1 - p * 2) : null;
                b = p >= 0.5 ? Fade(b, p * 2 - 1) : null;
                break;
            case TransitionKind.Zoom:
                a = Fade(Zoom(a, 1 + 0.6 * eased), 1 - p);
                b = Fade(Zoom(b, 0.6 + 0.4 * eased), p);
                break;
            case TransitionKind.SlideLeft: b = Move(b, width * (1 - eased), 0); break;
            case TransitionKind.SlideRight: b = Move(b, -width * (1 - eased), 0); break;
            case TransitionKind.SlideUp: b = Move(b, 0, height * (1 - eased)); break;
            case TransitionKind.SlideDown: b = Move(b, 0, -height * (1 - eased)); break;
            case TransitionKind.PushLeft:
                a = Move(a, -width * eased, 0);
                b = Move(b, width * (1 - eased), 0);
                break;
            case TransitionKind.PushRight:
                a = Move(a, width * eased, 0);
                b = Move(b, -width * (1 - eased), 0);
                break;
            default:
                // Cortinas e formas: o clipe de entrada é revelado por uma máscara.
                if (b is not null) b = b with { Mask = new TransitionMask(kind, p) };
                break;
        }

        if (a is { Opacity: > 0 }) layers.Add(a);
        if (b is { Opacity: > 0 }) layers.Add(b);
    }

    /// <summary>Camada de um clipe no instante (aceita instantes fora do clipe, usados pelas transições).</summary>
    public static VisualLayer? Layer(Project project, Track track, Clip clip, MediaTime t)
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { HasVideo: true } asset) return null;

        var offset = MediaTime.Max(MediaTime.Zero, MediaTime.Min(clip.Duration, t - clip.Start));
        var opacity = ClipAnimation.OpacityAt(clip, offset);
        if (opacity <= 0) return null;

        var isMoving = asset.Kind is MediaKind.Video or MediaKind.ImageSequence or MediaKind.AnimatedGif;
        var source = clip.FreezeAt ?? SourceTimeAt(clip, t);
        source = ClampSource(asset, source);

        MediaTime? blendSource = null;
        var blendWeight = 0.0;
        if (clip.FrameBlending && clip.FreezeAt is null && isMoving && clip.Speed.Value < 1 && asset.FrameRate is { } fr && fr.Value > 0)
        {
            // Mistura o quadro de origem atual com o seguinte, pela fração de tempo entre eles.
            var frame = fr.TimeToFrame(source);
            var frameStart = fr.FrameToTime(frame);
            var next = ClampSource(asset, fr.FrameToTime(frame + 1));
            var weight = (double)(source - frameStart).Ticks / fr.FrameDuration.Ticks;
            if (next > frameStart && weight > 0.02)
            {
                source = frameStart;
                blendSource = next;
                blendWeight = Math.Clamp(weight, 0, 1);
            }
        }

        return new VisualLayer(clip.Id, track.Id, asset, source, ClipAnimation.TransformAt(clip, offset), opacity,
            EffectCatalog.ActiveEffects(clip))
        {
            BlendMode = clip.BlendMode,
            BlendSourceTime = blendSource,
            BlendWeight = blendWeight,
        };
    }

    private static MediaTime ClampSource(MediaAsset asset, MediaTime source)
    {
        if (source < MediaTime.Zero) return MediaTime.Zero;
        if (asset.Kind is MediaKind.Video or MediaKind.ImageSequence && asset.Duration > MediaTime.Zero && source >= asset.Duration)
            return asset.Duration - new MediaTime(1);
        return source;
    }

    /// <summary>Clipe da trilha que cobre o instante (início inclusivo, fim exclusivo).</summary>
    public static Clip? ClipAt(Track track, MediaTime t)
    {
        // Clipes ordenados por início e sem sobreposição: busca binária.
        var clips = track.Clips;
        int lo = 0, hi = clips.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var c = clips[mid];
            if (t < c.Start) hi = mid - 1;
            else if (t >= c.End) lo = mid + 1;
            else return c;
        }
        return null;
    }

    /// <summary>Trechos de áudio audíveis em [<paramref name="from"/>, <paramref name="to"/>).</summary>
    public static IReadOnlyList<AudioSegment> AudioIn(Project project, Sequence sequence, MediaTime from, MediaTime to)
    {
        var result = new List<AudioSegment>();
        if (to <= from) return result;
        foreach (var track in sequence.Tracks)
        {
            if (track.Kind != TrackKind.Audio || track.IsMuted) continue;
            foreach (var clip in track.Clips)
            {
                if (clip.End <= from) continue;
                if (clip.Start >= to) break;
                if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { HasAudio: true } asset) continue;
                if (clip.Volume <= 0) continue;
                var start = MediaTime.Max(from, clip.Start);
                var end = MediaTime.Min(to, clip.End);
                result.Add(new AudioSegment(clip.Id, asset, start, end, SourceTimeAt(clip, start), clip.Speed, clip.Volume));
            }
        }
        return result;
    }

    /// <summary>Mídias usadas por algum clipe (para liberar decodificadores ociosos).</summary>
    public static IReadOnlySet<Guid> AssetsInUse(Sequence sequence) =>
        sequence.Tracks.SelectMany(t => t.Clips).Select(c => c.MediaAssetId).OfType<Guid>().ToHashSet();
}
