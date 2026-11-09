using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Intervalo de tempo da timeline [Start, End).</summary>
public readonly record struct TimeRange(MediaTime Start, MediaTime End)
{
    public MediaTime Duration => End - Start;
}

/// <summary>
/// Remove intervalos da timeline (ex.: silêncios detectados, RF128) do clipe e dos clipes vinculados a ele,
/// fechando o espaço (ripple) nas trilhas afetadas para manter áudio e vídeo sincronizados.
/// </summary>
public sealed record RemoveTimeRangesCommand(Guid SequenceId, Guid ClipId, IReadOnlyList<TimeRange> Ranges, string Description = "Remover trechos") : IEditCommand
{
    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var clip = Require(seq, ClipId).Clip;
        var trackIds = RippleOps.Affected(seq, ClipId);

        // Alinha à grade de quadros, limita ao clipe, descarta vazios e une sobreposições.
        var ranges = Ranges
            .Select(r => new TimeRange(fr.SnapToFrame(MediaTime.Max(r.Start, clip.Start)), fr.SnapToFrame(MediaTime.Min(r.End, clip.End))))
            .Where(r => r.End > r.Start)
            .OrderBy(r => r.Start.Ticks)
            .Aggregate(new List<TimeRange>(), (acc, r) =>
            {
                if (acc.Count > 0 && r.Start <= acc[^1].End) acc[^1] = acc[^1] with { End = MediaTime.Max(acc[^1].End, r.End) };
                else acc.Add(r);
                return acc;
            });
        if (ranges.Count == 0) throw new EditRejectedException("Nenhum trecho para remover.");
        if (ranges.Sum(r => r.Duration.Ticks) >= clip.Duration.Ticks)
            throw new EditRejectedException("Os trechos cobrem o clipe inteiro; nada sobraria.");

        // Do fim para o começo: cada remoção não altera as posições dos intervalos anteriores.
        for (var i = ranges.Count - 1; i >= 0; i--)
            seq = Cut(seq, trackIds, ranges[i]);
        return Commit(project, seq);
    }

    private static Sequence Cut(Sequence seq, IReadOnlySet<Guid> trackIds, TimeRange range)
    {
        var delta = range.Duration;
        var newLinks = new Dictionary<Guid, Guid>();
        foreach (var track in seq.Tracks.Where(t => trackIds.Contains(t.Id)).ToList())
        {
            EnsureUnlocked(track);
            var result = new List<Clip>(track.Clips.Count + 1);
            foreach (var c in track.Clips)
            {
                if (c.End <= range.Start)
                {
                    result.Add(c);
                    continue;
                }
                if (c.Start >= range.End)
                {
                    result.Add(c with { Start = c.Start - delta });
                    continue;
                }

                // O clipe cruza o intervalo: mantém o que fica antes e o que fica depois, encostados.
                var hasLeft = c.Start < range.Start;
                // A saída gradual pertence ao fim real do clipe; a entrada gradual, ao começo.
                if (hasLeft) result.Add(TrimEndTo(c, range.Start) with { FadeOut = c.End > range.End ? MediaTime.Zero : c.FadeOut });
                if (c.End > range.End)
                {
                    var right = TrimStartTo(c, range.End);
                    right = right with { Start = range.Start };
                    if (hasLeft)
                    {
                        Guid? link = c.LinkId is { } l ? newLinks.TryGetValue(l, out var nl) ? nl : newLinks[l] = Guid.NewGuid() : null;
                        right = right with { Id = Guid.NewGuid(), LinkId = link, FadeIn = MediaTime.Zero };
                    }
                    result.Add(right);
                }
            }
            seq = ReplaceTrack(seq, WithClips(track, result));
        }
        return seq;
    }
}
