using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

public enum ClipEdge
{
    Start,
    End,
}

/// <summary>Novo destino de um clipe ao mover.</summary>
public sealed record ClipMove(Guid ClipId, Guid TrackId, MediaTime Start);

/// <summary>Clipe copiado com a trilha de origem (área de transferência interna).</summary>
public sealed record ClipboardItem(Guid TrackId, Clip Clip);

internal static class Frames
{
    public static MediaTime Floor(FrameRate fr, MediaTime t) => fr.FrameToTime(fr.TimeToFrame(t));

    public static MediaTime Ceil(FrameRate fr, MediaTime t)
    {
        var f = Floor(fr, t);
        return f == t ? f : fr.FrameToTime(fr.TimeToFrame(t) + 1);
    }

    /// <summary>Duração alinhada a quadros inteiros (para baixo), mínimo 1 quadro.</summary>
    public static MediaTime Duration(FrameRate fr, MediaTime d) =>
        MediaTime.Max(fr.FrameDuration, Floor(fr, d));
}

/// <summary>Adiciona uma mídia à timeline (RF044). Vídeo com áudio gera dois clipes ligados.</summary>
public sealed record AddMediaToTimelineCommand(
    Guid SequenceId, Guid AssetId, Guid? VideoTrackId, Guid? AudioTrackId, MediaTime At, MediaTime ImageDuration) : IEditCommand
{
    public Guid VideoClipId { get; init; } = Guid.NewGuid();
    public Guid AudioClipId { get; init; } = Guid.NewGuid();

    public string Description { get; init; } = "Adicionar à linha do tempo";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var asset = project.FindMedia(AssetId) ?? throw new EditRejectedException("Mídia não encontrada.");
        var fr = project.Settings.FrameRate;
        var at = fr.SnapToFrame(MediaTime.Max(At, MediaTime.Zero));
        var source = asset.Kind == MediaKind.Image ? ImageDuration : asset.Duration;
        var duration = Frames.Duration(fr, source);

        var video = VideoTrackId is { } vt && asset.HasVideo ? RequireTrack(seq, vt) : null;
        var audio = AudioTrackId is { } at2 && asset.HasAudio ? RequireTrack(seq, at2) : null;
        if (video is null && audio is null)
            throw new EditRejectedException($"Não há trilha compatível para \"{asset.Name}\".");

        var link = video is not null && audio is not null ? Guid.NewGuid() : (Guid?)null;
        foreach (var track in new[] { video, audio }.OfType<Track>())
        {
            EnsureUnlocked(track);
            if (!Accepts(track.Kind, asset))
                throw new EditRejectedException($"A trilha \"{track.Name}\" não aceita \"{asset.Name}\".");
            var clip = new Clip
            {
                Id = track == video ? VideoClipId : AudioClipId,
                MediaAssetId = asset.Id,
                Start = at,
                Duration = duration,
                LinkId = link,
            };
            seq = ReplaceTrack(seq, Place(track, clip));
        }

        return Commit(project, seq);
    }
}

/// <summary>Move clipes no tempo e entre trilhas compatíveis (RF045, RF062). Sobrescreve o destino.</summary>
public sealed record MoveClipsCommand(Guid SequenceId, IReadOnlyList<ClipMove> Moves) : IEditCommand
{
    public string Description => Moves.Count == 1 ? "Mover clipe" : $"Mover {Moves.Count} clipes";

    public Project Apply(Project project)
    {
        if (Moves.Count == 0) return project;
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;

        var items = Moves.Select(m =>
        {
            var r = Require(seq, m.ClipId);
            var target = RequireTrack(seq, m.TrackId);
            EnsureUnlocked(r.Track);
            EnsureUnlocked(target);
            if (!Compatible(r.Track.Kind, target.Kind))
                throw new EditRejectedException($"O clipe não pode ser colocado na trilha \"{target.Name}\".");
            if (m.Start < MediaTime.Zero) throw new EditRejectedException("O clipe não pode começar antes do início da linha do tempo.");
            return (Ref: r, Target: target.Id, Start: fr.SnapToFrame(m.Start));
        }).ToList();

        if (items.All(i => i.Start == i.Ref.Clip.Start && i.Target == i.Ref.Track.Id)) return project;

        // Retira todos os clipes movidos antes de recolocá-los (permite trocar posições entre si).
        var moving = items.Select(i => i.Ref.Clip.Id).ToHashSet();
        seq = seq with { Tracks = [.. seq.Tracks.Select(t => WithClips(t, t.Clips.Where(c => !moving.Contains(c.Id))))] };

        foreach (var (r, target, start) in items)
            seq = ReplaceTrack(seq, Place(RequireTrack(seq, target), r.Clip with { Start = start }));

        return Commit(project, seq);
    }
}

/// <summary>
/// Apara o início ou o fim de um clipe (RF050). O tempo pedido é limitado pela mídia de origem,
/// pelo clipe vizinho (sem ripple) e por um quadro mínimo. Clipes ligados acompanham.
/// Com ripple, os clipes seguintes da trilha se deslocam para fechar/abrir espaço.
/// </summary>
public sealed record TrimClipCommand(Guid SequenceId, Guid ClipId, ClipEdge Edge, MediaTime Time, bool Ripple = false) : IEditCommand
{
    public string Description => Edge == ClipEdge.Start ? "Aparar início do clipe" : "Aparar fim do clipe";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var main = Require(seq, ClipId);

        // Clipes ligados com a mesma borda alinhada são aparados juntos.
        var affected = new List<ClipRef> { main };
        if (main.Clip.LinkId is { } link)
        {
            affected.AddRange(seq.Tracks.SelectMany((t, i) => t.Clips
                .Where(c => c.Id != main.Clip.Id && c.LinkId == link
                            && (Edge == ClipEdge.Start ? c.Start == main.Clip.Start : c.End == main.Clip.End))
                .Select(c => new ClipRef(i, t, c))));
        }
        foreach (var a in affected) EnsureUnlocked(a.Track);

        var edgeTime = Edge == ClipEdge.Start ? main.Clip.Start : main.Clip.End;
        var delta = fr.SnapToFrame(Time) - edgeTime;

        // Limites (o delta é ajustado para o intervalo permitido por todos os clipes afetados).
        var min = MediaTime.MinValue;
        var max = MediaTime.MaxValue;
        foreach (var (_, track, c) in affected)
        {
            var limit = SourceLimit(project, c);
            if (Edge == ClipEdge.End)
            {
                min = MediaTime.Max(min, c.Start + fr.FrameDuration - c.End);
                if (limit is { } l)
                    max = MediaTime.Min(max, Frames.Floor(fr, c.Start + c.Speed.TimelineDurationOf(l - c.SourceIn)) - c.End);
                if (!Ripple && track.Clips.Where(o => o.Start >= c.End && o.Id != c.Id).Select(o => o.Start).DefaultIfEmpty(MediaTime.MaxValue).Min() is var next && next != MediaTime.MaxValue)
                    max = MediaTime.Min(max, next - c.End);
            }
            else
            {
                max = MediaTime.Min(max, c.End - fr.FrameDuration - c.Start);
                if (limit is not null)
                    min = MediaTime.Max(min, -Frames.Floor(fr, c.Speed.TimelineDurationOf(c.SourceIn)));
                if (!Ripple)
                {
                    min = MediaTime.Max(min, -c.Start);
                    var prev = track.Clips.Where(o => o.End <= c.Start && o.Id != c.Id).Select(o => o.End).DefaultIfEmpty(MediaTime.Zero).Max();
                    min = MediaTime.Max(min, prev - c.Start);
                }
            }
        }

        if (min > max) return project;
        delta = MediaTime.Max(min, MediaTime.Min(max, delta));
        if (delta == MediaTime.Zero) return project;

        foreach (var (_, trackRef, c) in affected)
        {
            var track = RequireTrack(seq, trackRef.Id);
            var clips = track.Clips.ToList();
            var index = clips.FindIndex(x => x.Id == c.Id);

            if (Edge == ClipEdge.End)
            {
                clips[index] = c with { Duration = c.Duration + delta };
                if (Ripple) Shift(clips, c.End, delta, c.Id);
            }
            else if (!Ripple)
            {
                clips[index] = TrimStartTo(c, c.Start + delta);
            }
            else
            {
                // Ripple no início: o clipe permanece no lugar, o conteúdo muda e os seguintes se deslocam.
                var trimmed = TrimStartTo(c, c.Start + delta);
                clips[index] = trimmed with { Start = c.Start };
                Shift(clips, c.End, -delta, c.Id);
            }

            seq = ReplaceTrack(seq, WithClips(track, clips));
        }

        return Commit(project, seq);
    }

    internal static void Shift(List<Clip> clips, MediaTime from, MediaTime delta, Guid except)
    {
        for (var i = 0; i < clips.Count; i++)
            if (clips[i].Id != except && clips[i].Start >= from)
                clips[i] = clips[i] with { Start = MediaTime.Max(MediaTime.Zero, clips[i].Start + delta) };
    }
}

/// <summary>Divide clipes no instante informado (RF049, RF051). Sem lista, divide tudo que cruza o instante nas trilhas desbloqueadas.</summary>
public sealed record SplitClipsCommand(Guid SequenceId, MediaTime At, IReadOnlyList<Guid>? ClipIds = null) : IEditCommand
{
    public string Description => "Dividir clipe";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var at = project.Settings.FrameRate.SnapToFrame(At);

        var targets = ClipIds is null
            ? seq.Tracks.Where(t => !t.IsLocked).SelectMany(t => t.Clips).Where(c => c.Start < at && c.End > at).Select(c => c.Id).ToHashSet()
            : Expand(seq, ClipIds, linked: true, groups: false).ToHashSet();

        var newLinks = new Dictionary<Guid, Guid>();
        var count = 0;
        foreach (var track in seq.Tracks)
        {
            var hits = track.Clips.Where(c => targets.Contains(c.Id) && c.Start < at && c.End > at).ToList();
            if (hits.Count == 0) continue;
            EnsureUnlocked(track);
            var clips = track.Clips.ToList();
            foreach (var c in hits)
            {
                Guid? link = c.LinkId is { } l ? newLinks.TryGetValue(l, out var nl) ? nl : newLinks[l] = Guid.NewGuid() : null;
                var (left, right) = Split(c, at, Guid.NewGuid(), link)!.Value;
                var i = clips.FindIndex(x => x.Id == c.Id);
                clips[i] = left;
                clips.Insert(i + 1, right);
                count++;
            }
            seq = ReplaceTrack(seq, WithClips(track, clips));
        }

        if (count == 0) throw new EditRejectedException("Nenhum clipe sob o cursor para dividir.");
        return Commit(project, seq);
    }
}

/// <summary>Remove clipes (RF048). Com ripple, fecha o espaço deixado em cada trilha afetada.</summary>
public sealed record RemoveClipsCommand(Guid SequenceId, IReadOnlyList<Guid> ClipIds, bool Ripple = false) : IEditCommand
{
    public string Description => Ripple ? "Excluir e fechar espaço" : ClipIds.Count == 1 ? "Excluir clipe" : "Excluir clipes";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var ids = Expand(seq, ClipIds);
        if (ids.Count == 0) return project;

        foreach (var track in seq.Tracks)
        {
            var removed = track.Clips.Where(c => ids.Contains(c.Id)).ToList();
            if (removed.Count == 0) continue;
            EnsureUnlocked(track);

            var kept = track.Clips.Where(c => !ids.Contains(c.Id)).ToList();
            if (Ripple)
            {
                kept = [.. kept.Select(c =>
                {
                    var shift = removed.Where(r => r.End <= c.Start).Aggregate(MediaTime.Zero, (s, r) => s + r.Duration);
                    return c with { Start = c.Start - shift };
                })];
            }
            seq = ReplaceTrack(seq, WithClips(track, kept));
        }

        return Commit(project, seq);
    }
}

/// <summary>
/// Cola clipes copiados (RF046) a partir de <paramref name="At"/>, mantendo as posições relativas e as trilhas de origem.
/// Use <see cref="Create"/> para gerar novos identificadores. Também usado para duplicar (RF047).
/// </summary>
public sealed record PasteClipsCommand(Guid SequenceId, IReadOnlyList<ClipboardItem> Items, MediaTime At, string Description = "Colar") : IEditCommand
{
    /// <summary>Prepara cópias com novos IDs (e novos vínculos/grupos, preservando a estrutura entre as cópias).</summary>
    public static PasteClipsCommand Create(Guid sequenceId, IEnumerable<ClipboardItem> items, MediaTime at, string description = "Colar")
    {
        var links = new Dictionary<Guid, Guid>();
        var groups = new Dictionary<Guid, Guid>();
        Guid? Map(Guid? id, Dictionary<Guid, Guid> map) =>
            id is { } v ? map.TryGetValue(v, out var n) ? n : map[v] = Guid.NewGuid() : null;

        var copies = items.Select(i => i with
        {
            Clip = i.Clip with { Id = Guid.NewGuid(), LinkId = Map(i.Clip.LinkId, links), GroupId = Map(i.Clip.GroupId, groups) },
        }).ToList();
        return new PasteClipsCommand(sequenceId, copies, at, description);
    }

    /// <summary>Duplica a seleção logo após o fim dela (RF047).</summary>
    public static PasteClipsCommand Duplicate(Sequence sequence, IEnumerable<Guid> clipIds)
    {
        var ids = Expand(sequence, clipIds);
        var items = sequence.Tracks.SelectMany(t => t.Clips.Where(c => ids.Contains(c.Id)).Select(c => new ClipboardItem(t.Id, c))).ToList();
        if (items.Count == 0) throw new EditRejectedException("Selecione ao menos um clipe.");
        var end = items.Max(i => i.Clip.End);
        return Create(sequence.Id, items, end, items.Count == 1 ? "Duplicar clipe" : "Duplicar clipes");
    }

    public IEnumerable<Guid> NewClipIds => Items.Select(i => i.Clip.Id);

    public Project Apply(Project project)
    {
        if (Items.Count == 0) return project;
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var origin = Items.Min(i => i.Clip.Start);
        var at = fr.SnapToFrame(MediaTime.Max(At, MediaTime.Zero));

        foreach (var item in Items)
        {
            var track = seq.Tracks.Find(t => t.Id == item.TrackId)
                ?? throw new EditRejectedException("A trilha de origem dos clipes copiados não existe mais.");
            EnsureUnlocked(track);
            if (item.Clip.MediaAssetId is { } m && project.FindMedia(m) is null)
                throw new EditRejectedException("A mídia dos clipes copiados não faz mais parte do projeto.");
            var clip = item.Clip with { Start = at + (item.Clip.Start - origin) };
            seq = ReplaceTrack(seq, Place(track, clip));
        }

        return Commit(project, seq);
    }
}

/// <summary>
/// Altera a velocidade (RF061): a duração na timeline muda e o trecho de origem é mantido.
/// Ao aumentar a duração, os clipes seguintes da trilha se deslocam para não serem sobrescritos.
/// </summary>
public sealed record SetClipSpeedCommand(Guid SequenceId, Guid ClipId, Rational Speed) : IEditCommand
{
    public static readonly Rational Min = new(1, 10);
    public static readonly Rational Max = new(16, 1);

    public string Description => $"Velocidade {Speed.Value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}×";

    public Project Apply(Project project)
    {
        if (Speed.Value < Min.Value || Speed.Value > Max.Value)
            throw new EditRejectedException("A velocidade deve ficar entre 0,1× e 16×.");

        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var ids = Expand(seq, [ClipId], linked: true, groups: false);
        var changed = false;

        foreach (var track in seq.Tracks)
        {
            var hits = track.Clips.Where(c => ids.Contains(c.Id)).ToList();
            if (hits.Count == 0) continue;
            EnsureUnlocked(track);
            var clips = track.Clips.ToList();
            foreach (var c in hits)
            {
                if (c.Speed == Speed) continue;
                var source = c.Speed.SourceDurationOf(c.Duration);
                var duration = Frames.Duration(fr, Speed.TimelineDurationOf(source));
                var delta = duration - c.Duration;
                clips[clips.FindIndex(x => x.Id == c.Id)] = c with { Speed = Speed, Duration = duration };
                if (delta > MediaTime.Zero) TrimClipCommand.Shift(clips, c.End, delta, c.Id);
                changed = true;
            }
            seq = ReplaceTrack(seq, WithClips(track, clips));
        }

        return changed ? Commit(project, seq) : project;
    }
}

/// <summary>Agrupa (RF052) ou desagrupa (RF053) clipes.</summary>
public sealed record GroupClipsCommand(Guid SequenceId, IReadOnlyList<Guid> ClipIds, bool Group) : IEditCommand
{
    public string Description => Group ? "Agrupar" : "Desagrupar";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var ids = Group ? Expand(seq, ClipIds, linked: true, groups: true) : Expand(seq, ClipIds, linked: false, groups: true);
        if (Group && ids.Count < 2) throw new EditRejectedException("Selecione ao menos dois clipes para agrupar.");

        var groupId = Group ? Guid.NewGuid() : (Guid?)null;
        var changed = false;
        foreach (var track in seq.Tracks)
        {
            if (!track.Clips.Any(c => ids.Contains(c.Id))) continue;
            EnsureUnlocked(track);
            seq = ReplaceTrack(seq, WithClips(track, track.Clips.Select(c =>
            {
                if (!ids.Contains(c.Id) || c.GroupId == groupId) return c;
                changed = true;
                return c with { GroupId = groupId };
            })));
        }
        return changed ? Commit(project, seq) : project;
    }
}

/// <summary>Liga clipes (mantém sincronizados) ou desfaz a ligação, ex.: para mover o áudio separadamente (RF059, RF064).</summary>
public sealed record LinkClipsCommand(Guid SequenceId, IReadOnlyList<Guid> ClipIds, bool Link) : IEditCommand
{
    public string Description => Link ? "Vincular áudio e vídeo" : "Desvincular áudio e vídeo";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var ids = Link ? ClipIds.ToHashSet() : Expand(seq, ClipIds, linked: true, groups: false);
        if (Link && ids.Count < 2) throw new EditRejectedException("Selecione ao menos dois clipes para vincular.");

        var linkId = Link ? Guid.NewGuid() : (Guid?)null;
        var changed = false;
        foreach (var track in seq.Tracks)
        {
            if (!track.Clips.Any(c => ids.Contains(c.Id))) continue;
            seq = ReplaceTrack(seq, WithClips(track, track.Clips.Select(c =>
            {
                if (!ids.Contains(c.Id) || c.LinkId == linkId) return c;
                changed = true;
                return c with { LinkId = linkId };
            })));
        }
        return changed ? Commit(project, seq) : project;
    }
}

/// <summary>Move ou renomeia um marcador (RF060).</summary>
public sealed record UpdateMarkerCommand(Guid SequenceId, Guid MarkerId, MediaTime? Time = null, string? Name = null) : IEditCommand
{
    public string Description => Time is not null ? "Mover marcador" : "Renomear marcador";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var marker = seq.Markers.Find(m => m.Id == MarkerId) ?? throw new EditRejectedException("Marcador não encontrado.");
        var updated = marker with
        {
            Time = Time is { } t ? project.Settings.FrameRate.SnapToFrame(MediaTime.Max(t, MediaTime.Zero)) : marker.Time,
            Name = Name?.Trim() ?? marker.Name,
        };
        if (updated == marker) return project;
        var markers = seq.Markers.Replace(marker, updated).Sort((a, b) => a.Time.CompareTo(b.Time));
        return Commit(project, seq with { Markers = markers });
    }
}
