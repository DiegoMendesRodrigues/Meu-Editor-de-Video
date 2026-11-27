// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Vários marcadores de uma vez (ex.: batidas da música). Instantes já marcados (mesmo quadro) são ignorados.</summary>
public sealed record AddMarkersCommand(Guid SequenceId, IReadOnlyList<MediaTime> Times, string Name = "", string Color = "#F5A623") : IEditCommand
{
    public string Description => $"Adicionar {Times.Count} marcadores";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var fr = project.Settings.FrameRate;
        var used = seq.Markers.Select(m => m.Time).ToHashSet();
        var markers = seq.Markers.ToBuilder();
        foreach (var t in Times)
        {
            var time = fr.SnapToFrame(MediaTime.Max(t, MediaTime.Zero));
            if (!used.Add(time)) continue;
            markers.Add(new Marker { Id = Guid.NewGuid(), Time = time, Name = Name, Color = Color });
        }
        if (markers.Count == seq.Markers.Count) throw new EditRejectedException("Nenhum marcador novo para adicionar.");
        markers.Sort((a, b) => a.Time.CompareTo(b.Time));
        return project.WithSequence(seq with { Markers = markers.ToImmutable() });
    }
}

/// <summary>
/// Cortar nas batidas: divide os clipes indicados (e os vinculados) em cada instante. Os cortes vão do último para o
/// primeiro, então a parte esquerda mantém o Id original e os próximos cortes continuam valendo para ela.
/// </summary>
public sealed record SplitAtTimesCommand(Guid SequenceId, IReadOnlyList<MediaTime> Times, IReadOnlyList<Guid> ClipIds) : IEditCommand
{
    public string Description => "Cortar nas batidas";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var clips = Expand(seq, ClipIds, linked: true, groups: false)
            .Select(id => Find(seq, id)?.Clip).OfType<Clip>().ToList();
        var fr = project.Settings.FrameRate;
        var cuts = Times.Select(fr.SnapToFrame).Distinct()
            .Where(t => clips.Any(c => c.Start < t && t < c.End)).OrderDescending().ToList();
        if (cuts.Count == 0) throw new EditRejectedException("Nenhuma batida cai dentro dos clipes selecionados.");
        var result = project;
        foreach (var t in cuts) result = new SplitClipsCommand(SequenceId, t, ClipIds).Apply(result);
        return result;
    }
}

/// <summary>
/// Narração gerada (texto para fala): coloca o áudio no instante pedido na primeira trilha de áudio livre nesse intervalo
/// ou numa trilha nova chamada <paramref name="TrackName"/>.
/// </summary>
public sealed record AddNarrationCommand(Guid SequenceId, Guid AssetId, MediaTime At, string TrackName = "Narração") : IEditCommand
{
    public string Description => "Inserir narração";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var asset = project.FindMedia(AssetId) ?? throw new EditRejectedException("Mídia não encontrada.");
        var start = project.Settings.FrameRate.SnapToFrame(MediaTime.Max(At, MediaTime.Zero));
        var end = start + asset.Duration;
        var track = seq.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio && !t.IsLocked
            && !t.Clips.Any(c => c.Start < end && c.End > start));
        if (track is null)
        {
            track = new Track { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = TrackName };
            seq = seq with { Tracks = seq.Tracks.Insert(0, track) };
            project = project.WithSequence(seq);
        }
        return new AddMediaToTimelineCommand(SequenceId, AssetId, null, track.Id, start, MediaTime.Zero) { Description = Description }.Apply(project);
    }
}

/// <summary>Etiquetas da análise de mídia (substitui as anteriores da mídia).</summary>
public sealed record SetMediaTagsCommand(IReadOnlyDictionary<Guid, ImmutableList<string>> Tags) : IEditCommand
{
    public string Description => "Analisar mídia";

    public Project Apply(Project project)
    {
        if (!project.Media.Any(m => Tags.ContainsKey(m.Id))) throw new EditRejectedException("Mídia não encontrada.");
        return project with { Media = [.. project.Media.Select(m => Tags.TryGetValue(m.Id, out var t) ? m with { Tags = t } : m)] };
    }
}

/// <summary>Etiquetas básicas da mídia, a partir dos metadados (tipo, orientação, resolução, duração, som).</summary>
public static class MediaTags
{
    public static IReadOnlyList<string> Basic(MediaAsset asset)
    {
        var tags = new List<string>
        {
            asset.Kind switch
            {
                MediaKind.Video => "vídeo",
                MediaKind.Audio => "áudio",
                MediaKind.AnimatedGif => "gif",
                _ => "imagem",
            },
        };
        if (asset.HasVideo && asset.Width > 0 && asset.Height > 0)
        {
            tags.Add(asset.Width == asset.Height ? "quadrado" : asset.Height > asset.Width ? "vertical" : "horizontal");
            var side = Math.Max(asset.Width, asset.Height);
            if (side >= 3840) tags.Add("4K");
            else if (side >= 1920) tags.Add("Full HD");
            else if (side >= 1280) tags.Add("HD");
        }
        if (asset.Kind is MediaKind.Video or MediaKind.Audio && asset.Duration > MediaTime.Zero)
        {
            if (asset.Duration.TotalSeconds < 15) tags.Add("curto");
            else if (asset.Duration.TotalSeconds > 300) tags.Add("longo");
        }
        if (asset.Kind == MediaKind.Video) tags.Add(asset.HasAudio ? "com som" : "sem som");
        return tags;
    }
}
