using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Posição de um clipe na sequência.</summary>
public readonly record struct ClipRef(int TrackIndex, Track Track, Clip Clip);

/// <summary>
/// Operações puras sobre sequências usadas pelos comandos da timeline.
/// Regras: clipes de uma trilha nunca se sobrepõem; tempos ficam alinhados à grade de quadros do projeto.
/// </summary>
public static class TimelineOps
{
    public static ClipRef? Find(Sequence sequence, Guid clipId)
    {
        for (var t = 0; t < sequence.Tracks.Count; t++)
        {
            var track = sequence.Tracks[t];
            var clip = track.Clips.Find(c => c.Id == clipId);
            if (clip is not null) return new ClipRef(t, track, clip);
        }
        return null;
    }

    public static ClipRef Require(Sequence sequence, Guid clipId) =>
        Find(sequence, clipId) ?? throw new EditRejectedException("Clipe não encontrado.");

    public static Sequence RequireSequence(Project project, Guid sequenceId) =>
        project.Sequences.Find(s => s.Id == sequenceId) ?? throw new EditRejectedException("Sequência não encontrada.");

    public static Track RequireTrack(Sequence sequence, Guid trackId) =>
        sequence.Tracks.Find(t => t.Id == trackId) ?? throw new EditRejectedException("Trilha não encontrada.");

    public static void EnsureUnlocked(Track track)
    {
        if (track.IsLocked) throw new EditRejectedException($"A trilha \"{track.Name}\" está bloqueada.");
    }

    public static Sequence ReplaceTrack(Sequence sequence, Track track)
    {
        var index = sequence.Tracks.FindIndex(t => t.Id == track.Id);
        return sequence with { Tracks = sequence.Tracks.SetItem(index, track) };
    }

    public static Track WithClips(Track track, IEnumerable<Clip> clips) =>
        track with { Clips = [.. clips.OrderBy(c => c.Start.Ticks)] };

    /// <summary>
    /// Inclui na seleção os clipes ligados (mesmo LinkId) e do mesmo grupo (GroupId),
    /// para que operações preservem sincronização e agrupamento (RF052, RF064).
    /// </summary>
    public static IReadOnlySet<Guid> Expand(Sequence sequence, IEnumerable<Guid> clipIds, bool linked = true, bool groups = true)
    {
        var all = sequence.Tracks.SelectMany(t => t.Clips).ToList();
        var result = clipIds.ToHashSet();
        bool changed;
        do
        {
            changed = false;
            var selected = all.Where(c => result.Contains(c.Id)).ToList();
            var links = linked ? selected.Select(c => c.LinkId).OfType<Guid>().ToHashSet() : [];
            var groupIds = groups ? selected.Select(c => c.GroupId).OfType<Guid>().ToHashSet() : [];
            foreach (var c in all)
            {
                if (result.Contains(c.Id)) continue;
                if ((c.LinkId is { } l && links.Contains(l)) || (c.GroupId is { } g && groupIds.Contains(g)))
                {
                    result.Add(c.Id);
                    changed = true;
                }
            }
        } while (changed);
        return result;
    }

    /// <summary>
    /// Libera o intervalo [start, end) da trilha: remove, apara ou divide os clipes sobrepostos
    /// (modo "sobrescrever", padrão de editores profissionais).
    /// </summary>
    public static Track ClearRange(Track track, MediaTime start, MediaTime end, IReadOnlySet<Guid>? keep = null)
    {
        if (end <= start) return track;
        var result = new List<Clip>(track.Clips.Count + 1);
        foreach (var c in track.Clips)
        {
            if ((keep is not null && keep.Contains(c.Id)) || c.End <= start || c.Start >= end)
            {
                result.Add(c);
                continue;
            }

            if (c.Start < start) result.Add(TrimEndTo(c, start));
            if (c.End > end)
            {
                var right = TrimStartTo(c, end);
                result.Add(c.Start < start ? right with { Id = Guid.NewGuid(), LinkId = null } : right);
            }
        }
        return WithClips(track, result);
    }

    /// <summary>Encurta o clipe para terminar em <paramref name="end"/> (início e origem inalterados).</summary>
    public static Clip TrimEndTo(Clip c, MediaTime end) => c with { Duration = end - c.Start };

    /// <summary>Move o início do clipe para <paramref name="start"/> mantendo o fim; avança/recua o ponto de origem.</summary>
    public static Clip TrimStartTo(Clip c, MediaTime start)
    {
        var delta = start - c.Start;
        var sourceDelta = c.Speed.SourceDurationOf(delta.Ticks >= 0 ? delta : -delta);
        return c with
        {
            Start = start,
            Duration = c.End - start,
            SourceIn = delta.Ticks >= 0 ? c.SourceIn + sourceDelta : MediaTime.Max(MediaTime.Zero, c.SourceIn - sourceDelta),
        };
    }

    /// <summary>Coloca o clipe na trilha sobrescrevendo o que estiver no intervalo.</summary>
    public static Track Place(Track track, Clip clip)
    {
        var cleared = ClearRange(track, clip.Start, clip.End);
        return WithClips(cleared, cleared.Clips.Add(clip));
    }

    /// <summary>Divide o clipe em <paramref name="at"/>; retorna (esquerda, direita) ou null se o ponto não estiver dentro.</summary>
    public static (Clip Left, Clip Right)? Split(Clip c, MediaTime at, Guid rightId, Guid? rightLinkId)
    {
        if (at <= c.Start || at >= c.End) return null;
        var left = TrimEndTo(c, at);
        var right = TrimStartTo(c, at) with { Id = rightId, LinkId = rightLinkId };
        return (left, right);
    }

    /// <summary>A trilha aceita conteúdo desta mídia? (vídeo/imagem em trilhas visuais, áudio em trilhas de áudio).</summary>
    public static bool Accepts(TrackKind track, MediaAsset asset) => track switch
    {
        TrackKind.Audio => asset.HasAudio,
        TrackKind.Video or TrackKind.Image => asset.HasVideo,
        _ => false,
    };

    /// <summary>Trilhas compatíveis para mover um clipe de uma trilha para outra.</summary>
    public static bool Compatible(TrackKind from, TrackKind to) =>
        from == to
        || (from is TrackKind.Video or TrackKind.Image && to is TrackKind.Video or TrackKind.Image);

    /// <summary>Duração máxima de origem disponível para o clipe (null = ilimitada: imagens, textos).</summary>
    public static MediaTime? SourceLimit(Project project, Clip clip)
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { } asset) return null;
        return asset.Kind is MediaKind.Image ? null : asset.Duration;
    }

    public static Project Commit(Project project, Sequence sequence) => project.WithSequence(sequence);
}
