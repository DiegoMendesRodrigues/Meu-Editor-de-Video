using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.TimelineEngine;

/// <summary>Nó de efeito aplicado a uma camada. Fases futuras (F6–F10) acrescentam tipos concretos.</summary>
public abstract record EffectNode;

/// <summary>Camada visual de um quadro: mídia, instante de origem, transformação, opacidade e efeitos.</summary>
public sealed record VisualLayer(
    Guid ClipId,
    Guid TrackId,
    MediaAsset Asset,
    MediaTime SourceTime,
    Transform Transform,
    double Opacity,
    IReadOnlyList<EffectNode> Effects);

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
        var layers = new List<VisualLayer>();
        // De baixo para cima: percorre as trilhas da última para a primeira.
        for (var i = sequence.Tracks.Count - 1; i >= 0; i--)
        {
            var track = sequence.Tracks[i];
            if (!track.IsVisual || track.IsHidden) continue;
            var clip = ClipAt(track, t);
            if (clip?.MediaAssetId is not { } id || project.FindMedia(id) is not { HasVideo: true } asset) continue;
            if (clip.Opacity <= 0) continue;

            var source = SourceTimeAt(clip, t);
            if (asset.Kind is MediaKind.Video or MediaKind.ImageSequence && asset.Duration > MediaTime.Zero && source >= asset.Duration)
                source = asset.Duration - new MediaTime(1);

            layers.Add(new VisualLayer(clip.Id, track.Id, asset, source, clip.Transform, Math.Clamp(clip.Opacity, 0, 1), []));
        }
        return new CompositionGraph(t, project.Settings.Width, project.Settings.Height, layers);
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
