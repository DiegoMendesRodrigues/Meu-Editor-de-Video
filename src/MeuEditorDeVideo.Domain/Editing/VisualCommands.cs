using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Alteração genérica de propriedades de um clipe (transformação, opacidade, efeitos, cor, keyframes, mistura).
/// Ajustes contínuos (arrastar um controle) com a mesma <see cref="MergeKey"/> viram uma única entrada no histórico.
/// </summary>
public sealed record UpdateClipCommand(Guid SequenceId, Guid ClipId, Func<Clip, Clip> Update, string Description, string? MergeKey = null) : IEditCommand
{
    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var (_, track, clip) = Require(seq, ClipId);
        EnsureUnlocked(track);
        var updated = Update(clip) with { Id = clip.Id, Start = clip.Start, Duration = clip.Duration };
        if (updated == clip) return project;
        return Commit(project, ReplaceTrack(seq, track with { Clips = track.Clips.Replace(clip, updated) }));
    }
}

/// <summary>Operações de ripple usadas por congelar/repetir: abre espaço nas trilhas afetadas.</summary>
internal static class RippleOps
{
    /// <summary>Trilhas do clipe e dos clipes vinculados a ele (mantém a sincronização áudio/vídeo).</summary>
    public static IReadOnlySet<Guid> Affected(Sequence seq, Guid clipId)
    {
        var ids = Expand(seq, [clipId], linked: true, groups: false);
        return seq.Tracks.Where(t => t.Clips.Exists(c => ids.Contains(c.Id))).Select(t => t.Id).ToHashSet();
    }

    /// <summary>Divide os clipes que cruzam <paramref name="at"/> e desloca tudo a partir dele em <paramref name="delta"/>.</summary>
    public static Sequence InsertGap(Sequence seq, IReadOnlySet<Guid> trackIds, MediaTime at, MediaTime delta)
    {
        var newLinks = new Dictionary<Guid, Guid>();
        foreach (var track in seq.Tracks.Where(t => trackIds.Contains(t.Id)).ToList())
        {
            EnsureUnlocked(track);
            var clips = new List<Clip>();
            foreach (var c in track.Clips)
            {
                if (Split(c, at, Guid.NewGuid(), c.LinkId is { } l ? newLinks.TryGetValue(l, out var nl) ? nl : newLinks[l] = Guid.NewGuid() : null) is var (left, right))
                {
                    clips.Add(left);
                    clips.Add(right with { Start = right.Start + delta });
                }
                else
                {
                    clips.Add(c.Start >= at ? c with { Start = c.Start + delta } : c);
                }
            }
            seq = ReplaceTrack(seq, WithClips(track, clips));
        }
        return seq;
    }

    public static MediaTime SourceTimeAt(Clip c, MediaTime timelineTime) =>
        c.FreezeAt ?? c.SourceIn + c.Speed.SourceDurationOf(MediaTime.Max(MediaTime.Zero, timelineTime - c.Start));
}

/// <summary>
/// Congela o quadro sob o cursor (RF073): divide o clipe, abre espaço (ripple) nas trilhas vinculadas e insere um clipe
/// que mostra o quadro fixo durante <see cref="Duration"/>.
/// </summary>
public sealed record FreezeFrameCommand(Guid SequenceId, Guid ClipId, MediaTime At, MediaTime Duration) : IEditCommand
{
    public Guid FreezeClipId { get; init; } = Guid.NewGuid();

    public string Description => "Congelar quadro";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var (_, track, clip) = Require(seq, ClipId);
        if (!track.IsVisual || clip.MediaAssetId is null)
            throw new EditRejectedException("Selecione um clipe de vídeo para congelar o quadro.");
        if (project.FindMedia(clip.MediaAssetId.Value) is { Kind: MediaKind.Image })
            throw new EditRejectedException("Imagens já são estáticas; ajuste a duração do clipe.");

        var duration = Frames.Duration(fr, Duration);
        var at = fr.SnapToFrame(At);
        at = MediaTime.Max(clip.Start, MediaTime.Min(clip.End, at));
        // No fim do clipe, o quadro congelado é o último exibido.
        var sourceAt = RippleOps.SourceTimeAt(clip, at >= clip.End ? clip.End - fr.FrameDuration : at);

        seq = RippleOps.InsertGap(seq, RippleOps.Affected(seq, clip.Id), at, duration);
        var freeze = clip with
        {
            Id = FreezeClipId,
            LinkId = null,
            GroupId = null,
            Start = at,
            Duration = duration,
            SourceIn = sourceAt,
            Speed = Rational.One,
            FreezeAt = sourceAt,
            FrameBlending = false,
            Keyframes = clip.Keyframes.Clear(),
            FadeIn = MediaTime.Zero,
            FadeOut = MediaTime.Zero,
            Transform = ClipAnimation.TransformAt(clip, at - clip.Start),
            Opacity = Math.Clamp(ClipAnimation.ValueAt(clip, AnimatedProperty.Opacity, at - clip.Start), 0, 1),
        };
        var target = RequireTrack(seq, track.Id);
        seq = ReplaceTrack(seq, WithClips(target, target.Clips.Add(freeze)));
        return Commit(project, seq);
    }
}

/// <summary>Repete o clipe (e os vinculados) <see cref="Count"/> vezes logo após ele, deslocando o restante (RF074).</summary>
public sealed record RepeatClipCommand(Guid SequenceId, Guid ClipId, int Count) : IEditCommand
{
    public string Description => Count == 1 ? "Repetir trecho" : $"Repetir trecho {Count}×";

    public Project Apply(Project project)
    {
        if (Count is < 1 or > 100) throw new EditRejectedException("O número de repetições deve ficar entre 1 e 100.");
        var seq = RequireSequence(project, SequenceId);
        var clip = Require(seq, ClipId).Clip;
        var ids = Expand(seq, [ClipId], linked: true, groups: false);
        var members = seq.Tracks.SelectMany(t => t.Clips.Where(c => ids.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToList();
        var start = members.Min(m => m.Clip.Start);
        var end = members.Max(m => m.Clip.End);
        var span = end - start;

        seq = RippleOps.InsertGap(seq, RippleOps.Affected(seq, ClipId), end, new MediaTime(span.Ticks * Count));
        for (var i = 1; i <= Count; i++)
        {
            var offset = new MediaTime(span.Ticks * i);
            var link = clip.LinkId is null ? (Guid?)null : Guid.NewGuid();
            foreach (var (t, c) in members)
            {
                var target = RequireTrack(seq, t.Id);
                var copy = c with { Id = Guid.NewGuid(), Start = c.Start + offset, LinkId = c.LinkId is null ? null : link, GroupId = null };
                seq = ReplaceTrack(seq, WithClips(target, target.Clips.Add(copy)));
            }
        }
        return Commit(project, seq);
    }
}

/// <summary>
/// Define, troca ou remove (Kind nulo = corte seco) a transição entre dois clipes adjacentes da mesma trilha (RF078–RF085).
/// </summary>
public sealed record SetTransitionCommand(Guid SequenceId, Guid FromClipId, Guid ToClipId, TransitionKind? Kind, MediaTime Duration) : IEditCommand
{
    public static readonly MediaTime DefaultDuration = MediaTime.FromSecondsApprox(1);

    public string Description => Kind is null ? "Remover transição" : "Aplicar transição";

    public string? MergeKey => Kind is null ? null : $"transition:{FromClipId}:{ToClipId}";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var from = Require(seq, FromClipId);
        var to = Require(seq, ToClipId);
        if (from.Track.Id != to.Track.Id || from.Clip.End != to.Clip.Start)
            throw new EditRejectedException("A transição precisa de dois clipes encostados na mesma trilha.");
        if (!from.Track.IsVisual) throw new EditRejectedException("Transições de imagem só se aplicam a trilhas visuais.");
        EnsureUnlocked(from.Track);

        var track = from.Track;
        var existing = track.Transitions.Find(t => t.FromClipId == FromClipId && t.ToClipId == ToClipId);
        if (Kind is null)
        {
            if (existing is null) return project;
            return Commit(project, ReplaceTrack(seq, track with { Transitions = track.Transitions.Remove(existing) }));
        }

        var max = TransitionOps.MaxDuration(from.Clip, to.Clip, fr);
        if (max < new MediaTime(fr.FrameDuration.Ticks * 2)) throw new EditRejectedException("Os clipes são curtos demais para uma transição.");
        var duration = MediaTime.Max(new MediaTime(fr.FrameDuration.Ticks * 2), MediaTime.Min(max, Frames.Floor(fr, Duration)));
        var transition = new Transition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            FromClipId = FromClipId,
            ToClipId = ToClipId,
            Kind = Kind.Value,
            Duration = duration,
        };
        var list = existing is null ? track.Transitions.Add(transition) : track.Transitions.Replace(existing, transition);
        return Commit(project, ReplaceTrack(seq, track with { Transitions = list }));
    }
}

public static class TransitionOps
{
    /// <summary>Duração máxima: a transição é centrada no corte e não pode ser maior que o clipe mais curto (metade dela fica em cada lado).</summary>
    public static MediaTime MaxDuration(Clip from, Clip to, FrameRate fr)
    {
        var shorter = MediaTime.Min(from.Duration, to.Duration);
        return Frames.Floor(fr, new MediaTime(shorter.Ticks));
    }

    public static Transition? Incoming(Track track, Guid clipId) => track.Transitions.Find(t => t.ToClipId == clipId);

    public static Transition? Outgoing(Track track, Guid clipId) => track.Transitions.Find(t => t.FromClipId == clipId);

    /// <summary>
    /// Mantém as transições coerentes após edições: religa ao clipe que agora termina onde o destino começa
    /// (ex.: após dividir o clipe de origem) e descarta as que perderam a adjacência.
    /// </summary>
    public static Track Normalize(Track track)
    {
        if (track.Transitions.IsEmpty) return track;
        var byId = track.Clips.ToDictionary(c => c.Id);
        var result = new List<Transition>();
        foreach (var t in track.Transitions)
        {
            if (!byId.TryGetValue(t.ToClipId, out var to)) continue;
            var from = byId.TryGetValue(t.FromClipId, out var f) && f.End == to.Start ? f : track.Clips.Find(c => c.End == to.Start && c.Id != to.Id);
            if (from is null || result.Exists(r => r.ToClipId == to.Id)) continue;
            var max = MediaTime.Min(from.Duration, to.Duration);
            result.Add(t with { FromClipId = from.Id, Duration = MediaTime.Min(t.Duration, max) });
        }
        return result.Count == track.Transitions.Count && result.SequenceEqual(track.Transitions) ? track : track with { Transitions = [.. result] };
    }
}
