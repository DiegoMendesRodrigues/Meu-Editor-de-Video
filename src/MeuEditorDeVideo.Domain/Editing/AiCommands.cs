using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Arquivos gerados a partir de um clipe: um por trilha nova, na ordem de <see cref="PlaceGeneratedAudioCommand.TrackNames"/>.</summary>
public sealed record GeneratedAudio(Guid SourceClipId, IReadOnlyList<Guid> AssetIds);

/// <summary>
/// Coloca o áudio gerado pela IA (isolamento de voz/redução de ruído, RF127, RF159, RF160): cria uma trilha nova para cada
/// saída (ex.: "Voz (IA)" e "Ambiente (IA)") logo abaixo da trilha do primeiro clipe de origem; cada arquivo começa no
/// mesmo instante e tem a mesma duração do seu clipe de origem. Os clipes originais podem ser silenciados (volume 0).
/// Uma única ação de desfazer.
/// </summary>
public sealed record PlaceGeneratedAudioCommand(Guid SequenceId, IReadOnlyList<GeneratedAudio> Items, IReadOnlyList<string> TrackNames,
    bool MuteSource, string Description = "Inserir áudio processado") : IEditCommand
{
    public Project Apply(Project project)
    {
        if (Items.Count == 0 || TrackNames.Count == 0) throw new EditRejectedException("Nada a inserir.");
        var seq = RequireSequence(project, SequenceId);
        var first = Require(seq, Items[0].SourceClipId).Clip;
        var index = seq.Tracks.FindIndex(t => t.Clips.Any(c => c.Id == first.Id)) + 1;

        var tracks = TrackNames.Select(n => new List<Clip>()).ToList();
        foreach (var item in Items)
        {
            var source = Require(seq, item.SourceClipId).Clip;
            for (var k = 0; k < Math.Min(item.AssetIds.Count, TrackNames.Count); k++)
            {
                var asset = project.FindMedia(item.AssetIds[k]) ?? throw new EditRejectedException("Mídia gerada não encontrada.");
                if (!asset.HasAudio) throw new EditRejectedException($"\"{asset.Name}\" não tem áudio.");
                var duration = asset.Duration > MediaTime.Zero ? MediaTime.Min(source.Duration, asset.Duration) : source.Duration;
                tracks[k].Add(new Clip { Id = Guid.NewGuid(), MediaAssetId = asset.Id, Start = source.Start, Duration = duration });
            }
        }
        for (var k = 0; k < TrackNames.Count; k++)
        {
            var track = WithClips(new Track { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = TrackNames[k] }, tracks[k].OrderBy(c => c.Start.Ticks));
            seq = seq with { Tracks = seq.Tracks.Insert(Math.Clamp(index + k, 0, seq.Tracks.Count), track) };
        }

        if (MuteSource)
        {
            var ids = Items.Select(i => i.SourceClipId).ToHashSet();
            foreach (var track in seq.Tracks.Where(t => t.Clips.Any(c => ids.Contains(c.Id))).ToList())
            {
                EnsureUnlocked(track);
                seq = ReplaceTrack(seq, WithClips(track, track.Clips.Select(c => ids.Contains(c.Id) ? c with { Volume = 0 } : c).ToList()));
            }
        }
        return Commit(project, seq);
    }
}
