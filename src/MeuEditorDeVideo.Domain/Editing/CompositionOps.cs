using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Modelo de elemento gráfico da biblioteca (RF152).</summary>
public sealed record ShapePreset(ShapeKind Kind, string Name, ShapeStyle Style);

/// <summary>Elementos gráficos prontos.</summary>
public static class ShapePresets
{
    public static IReadOnlyList<ShapePreset> All { get; } =
    [
        new(ShapeKind.Rectangle, "Retângulo", new ShapeStyle { Kind = ShapeKind.Rectangle, Width = 480, Height = 270, CornerRadius = 0 }),
        new(ShapeKind.Rectangle, "Retângulo arredondado", new ShapeStyle { Kind = ShapeKind.Rectangle, Width = 480, Height = 270, CornerRadius = 36 }),
        new(ShapeKind.Ellipse, "Círculo", new ShapeStyle { Kind = ShapeKind.Ellipse, Width = 300, Height = 300, FillColor = "#FFFFB13D" }),
        new(ShapeKind.Line, "Linha", new ShapeStyle { Kind = ShapeKind.Line, Width = 600, Height = 8, FillColor = "#FFFFFFFF" }),
        new(ShapeKind.Arrow, "Seta", new ShapeStyle { Kind = ShapeKind.Arrow, Width = 420, Height = 160, FillColor = "#FFFF4D4D" }),
        new(ShapeKind.Star, "Estrela", new ShapeStyle { Kind = ShapeKind.Star, Width = 320, Height = 320, FillColor = "#FFFFD23D", Sides = 5 }),
        new(ShapeKind.Polygon, "Hexágono", new ShapeStyle { Kind = ShapeKind.Polygon, Width = 320, Height = 320, FillColor = "#FF4DD08A", Sides = 6 }),
        new(ShapeKind.SpeechBubble, "Balão de fala", new ShapeStyle
        {
            Kind = ShapeKind.SpeechBubble, Width = 520, Height = 320, FillColor = "#FFFFFFFF", StrokeColor = "#FF202020", StrokeWidth = 6,
        }),
        new(ShapeKind.Heart, "Coração", new ShapeStyle { Kind = ShapeKind.Heart, Width = 300, Height = 280, FillColor = "#FFFF3D6E" }),
    ];

    /// <summary>Clipe do elemento gráfico (5 s por padrão), centralizado no quadro.</summary>
    public static Clip Create(ShapeStyle style, MediaTime start, MediaTime duration) => new()
    {
        Id = Guid.NewGuid(),
        Start = start,
        Duration = duration,
        Shape = style,
    };

    public static string Name(ShapeKind kind) => All.FirstOrDefault(p => p.Kind == kind)?.Name ?? "Elemento gráfico";
}

/// <summary>Operações puras de máscaras, rastreamentos, vínculos e estabilização de um clipe (F9).</summary>
public static class CompositionOps
{
    public static Clip AddMask(Clip clip, MaskKind kind, Guid? id = null)
    {
        var n = clip.Masks.Count + 1;
        var mask = new Mask
        {
            Id = id ?? Guid.NewGuid(),
            Kind = kind,
            Name = $"Máscara {n}",
            Points = CompositionMath.DefaultShape(kind),
            Smooth = kind == MaskKind.Path,
            Feather = 0.01,
        };
        return clip with { Masks = clip.Masks.Add(mask) };
    }

    public static Clip UpdateMask(Clip clip, Guid maskId, Func<Mask, Mask> update)
    {
        var i = clip.Masks.FindIndex(m => m.Id == maskId);
        return i < 0 ? clip : clip with { Masks = clip.Masks.SetItem(i, update(clip.Masks[i])) };
    }

    public static Clip RemoveMask(Clip clip, Guid maskId) => clip with { Masks = clip.Masks.RemoveAll(m => m.Id == maskId) };

    /// <summary>Move a máscara na lista (a ordem define como as máscaras se combinam).</summary>
    public static Clip MoveMask(Clip clip, Guid maskId, int delta)
    {
        var i = clip.Masks.FindIndex(m => m.Id == maskId);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= clip.Masks.Count) return clip;
        var m = clip.Masks[i];
        return clip with { Masks = clip.Masks.RemoveAt(i).Insert(j, m) };
    }

    /// <summary>Novo rastreamento marcado numa região (centro/tamanho normalizados) no instante de origem.</summary>
    public static Clip AddTracker(Clip clip, TrackSample region, Guid? id = null)
    {
        var tracker = new MotionTracker
        {
            Id = id ?? Guid.NewGuid(),
            Name = $"Rastreamento {clip.Trackers.Count + 1}",
            Region = region,
        };
        return clip with { Trackers = clip.Trackers.Add(tracker) };
    }

    public static Clip UpdateTracker(Clip clip, Guid trackerId, Func<MotionTracker, MotionTracker> update)
    {
        var i = clip.Trackers.FindIndex(t => t.Id == trackerId);
        return i < 0 ? clip : clip with { Trackers = clip.Trackers.SetItem(i, update(clip.Trackers[i])) };
    }

    /// <summary>Junta amostras calculadas ao rastreamento.</summary>
    public static Clip AddSamples(Clip clip, Guid trackerId, IEnumerable<TrackSample> samples) =>
        UpdateTracker(clip, trackerId, t => t with { Samples = CompositionMath.MergeSamples(t.Samples, samples) });

    /// <summary>Remove o rastreamento e solta as máscaras que o seguiam.</summary>
    public static Clip RemoveTracker(Clip clip, Guid trackerId) => clip with
    {
        Trackers = clip.Trackers.RemoveAll(t => t.Id == trackerId),
        Masks = [.. clip.Masks.Select(m => m.TrackerId == trackerId ? m with { TrackerId = null } : m)],
    };

    /// <summary>Faz a máscara seguir o rastreamento a partir do instante de origem atual (nulo solta).</summary>
    public static Clip FollowTracker(Clip clip, Guid maskId, Guid? trackerId, MediaTime sourceTime) =>
        UpdateMask(clip, maskId, m => m with { TrackerId = trackerId, TrackerReference = sourceTime });

    /// <summary>Vincula o elemento ao rastreamento de outro clipe; a posição atual vira a referência.</summary>
    public static Clip Link(Clip clip, Guid sourceClipId, Guid trackerId, MediaTime referenceTime, bool followScale) =>
        clip with { MotionLink = new TrackerLink { ClipId = sourceClipId, TrackerId = trackerId, ReferenceTime = referenceTime, FollowScale = followScale } };

    public static Clip Unlink(Clip clip) => clip with { MotionLink = null };

    /// <summary>Rastreamentos disponíveis na sequência (clipe de origem, rastreamento) para vincular elementos.</summary>
    public static IEnumerable<(Clip Clip, MotionTracker Tracker)> Trackers(Sequence sequence) =>
        sequence.Tracks.SelectMany(t => t.Clips).SelectMany(c => c.Trackers.Where(k => k.Samples.Count > 0).Select(k => (c, k)));

    /// <summary>Instantes de origem de cada quadro do clipe (para análise), na taxa da mídia ou do projeto.</summary>
    public static IReadOnlyList<MediaTime> SourceFrames(Clip clip, FrameRate rate, MediaTime from, MediaTime to)
    {
        var list = new List<MediaTime>();
        if (rate.Value <= 0) return list;
        var first = rate.TimeToFrame(MediaTime.Max(clip.SourceIn, from));
        var last = rate.TimeToFrame(MediaTime.Min(clip.SourceOut, to));
        for (var f = first; f <= last; f++)
        {
            var t = rate.FrameToTime(f);
            if (t >= clip.SourceIn && t < clip.SourceOut) list.Add(t);
        }
        return list;
    }

    public static Clip SetStabilization(Clip clip, IEnumerable<MotionStep> steps, double smoothness) =>
        clip with { Stabilization = new Stabilization { Steps = [.. steps], Smoothness = smoothness, Enabled = true } };
}
