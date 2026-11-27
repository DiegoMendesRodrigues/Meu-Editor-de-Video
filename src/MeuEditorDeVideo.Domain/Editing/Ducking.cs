// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Atenuação automática (ducking): abaixa o volume de um clipe (normalmente a música) enquanto há fala em outras
/// trilhas, com rampas de descida e subida. O resultado são keyframes de volume comuns, que o usuário pode ajustar.
/// </summary>
public static class Ducking
{
    /// <summary>Espaço máximo entre duas falas para que o volume não volte a subir entre elas.</summary>
    public static readonly MediaTime DefaultMergeGap = MediaTime.FromSecondsApprox(0.5);

    /// <summary>
    /// Substitui os keyframes de volume de <paramref name="clip"/> por um envelope que fica em
    /// <paramref name="gainDb"/> (negativo) durante as falas (<paramref name="speech"/>, em tempo da linha do tempo).
    /// Sem fala dentro do clipe, só remove a animação de volume.
    /// </summary>
    public static Clip Apply(Clip clip, IEnumerable<TimeRange> speech, double gainDb, MediaTime fade, MediaTime? mergeGap = null)
    {
        var baseVolume = ClipAnimation.KeysOf(clip, AnimatedProperty.Volume) is { Count: > 0 } keys ? keys[0].Value : clip.Volume;
        var low = Math.Clamp(baseVolume * Math.Pow(10, Math.Min(0, gainDb) / 20), 0, ClipAnimation.MaxVolume);
        var ranges = Merge(ToClipOffsets(clip, speech), mergeGap ?? DefaultMergeGap + fade + fade);
        var cleared = clip with { Volume = baseVolume, Keyframes = clip.Keyframes.Remove(AnimatedProperty.Volume) };
        if (ranges.Count == 0) return cleared;

        var points = new SortedDictionary<long, double> { [0] = baseVolume, [clip.Duration.Ticks] = baseVolume };
        foreach (var r in ranges)
        {
            var downStart = MediaTime.Max(MediaTime.Zero, r.Start - fade);
            var upEnd = MediaTime.Min(clip.Duration, r.End + fade);
            if (downStart < r.Start) points[downStart.Ticks] = baseVolume;
            points[r.Start.Ticks] = low;
            points[r.End.Ticks] = low;
            if (upEnd > r.End) points[upEnd.Ticks] = baseVolume;
        }
        var list = points.Select(p => new Keyframe(new MediaTime(p.Key), p.Value, Easing.Linear)).ToImmutableList();
        return cleared with { Keyframes = cleared.Keyframes.SetItem(AnimatedProperty.Volume, list) };
    }

    /// <summary>Remove a atenuação (e qualquer outra animação de volume), mantendo o volume do início.</summary>
    public static Clip Remove(Clip clip) => ClipAnimation.ClearKeyframes(clip, AnimatedProperty.Volume, MediaTime.Zero);

    /// <summary>Converte intervalos da linha do tempo para offsets do clipe, recortados à duração dele.</summary>
    public static IEnumerable<TimeRange> ToClipOffsets(Clip clip, IEnumerable<TimeRange> ranges)
    {
        foreach (var r in ranges)
        {
            var s = MediaTime.Max(MediaTime.Zero, r.Start - clip.Start);
            var e = MediaTime.Min(clip.Duration, r.End - clip.Start);
            if (e > s) yield return new TimeRange(s, e);
        }
    }

    /// <summary>Ordena e junta intervalos que se tocam ou ficam a menos de <paramref name="gap"/> um do outro.</summary>
    public static IReadOnlyList<TimeRange> Merge(IEnumerable<TimeRange> ranges, MediaTime gap)
    {
        var result = new List<TimeRange>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (result.Count > 0 && r.Start - result[^1].End <= gap)
                result[^1] = new TimeRange(result[^1].Start, MediaTime.Max(result[^1].End, r.End));
            else result.Add(r);
        }
        return result;
    }

    /// <summary>Fala como o complemento dos silêncios dentro de [<paramref name="start"/>, <paramref name="end"/>).</summary>
    public static IReadOnlyList<TimeRange> Complement(IEnumerable<TimeRange> silences, MediaTime start, MediaTime end)
    {
        var result = new List<TimeRange>();
        var cursor = start;
        foreach (var s in silences.OrderBy(s => s.Start))
        {
            if (s.Start > cursor) result.Add(new TimeRange(cursor, MediaTime.Min(s.Start, end)));
            cursor = MediaTime.Max(cursor, s.End);
            if (cursor >= end) break;
        }
        if (cursor < end) result.Add(new TimeRange(cursor, end));
        return result;
    }
}
