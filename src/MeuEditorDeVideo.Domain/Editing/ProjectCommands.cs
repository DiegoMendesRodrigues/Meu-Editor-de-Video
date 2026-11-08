using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Comandos de nível de projeto (F1). Comandos de clipes ficam em ClipCommands (F3).</summary>
public sealed record RenameProjectCommand(string NewName) : IEditCommand
{
    public string Description => "Renomear projeto";

    public Project Apply(Project project)
    {
        var name = NewName.Trim();
        if (name.Length == 0) throw new EditRejectedException("O nome do projeto não pode ficar vazio.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new EditRejectedException("O nome do projeto contém caracteres não permitidos em nomes de arquivo.");
        return name == project.Name ? project : project with { Name = name };
    }
}

/// <summary>Altera resolução, FPS, perfil de cor e áudio do projeto (RF005–RF007).</summary>
public sealed record ChangeProjectSettingsCommand(ProjectSettings NewSettings) : IEditCommand
{
    public string Description => "Alterar configurações do projeto";

    public Project Apply(Project project)
    {
        var errors = NewSettings.Validate().ToList();
        if (errors.Count > 0) throw new EditRejectedException(string.Join(Environment.NewLine, errors));
        if (NewSettings == project.Settings) return project;

        // Ao mudar o FPS, alinhar clipes e marcadores à nova grade de quadros.
        if (NewSettings.FrameRate != project.Settings.FrameRate)
        {
            var rate = NewSettings.FrameRate;
            project = project with
            {
                Sequences = project.Sequences.Select(s => s with
                {
                    Markers = s.Markers.Select(m => m with { Time = rate.SnapToFrame(m.Time) }).ToImmutableList(),
                    Tracks = s.Tracks.Select(t => t with
                    {
                        Clips = SnapClips(t.Clips, rate),
                    }).ToImmutableList(),
                }).ToImmutableList(),
            };
        }

        return project with { Settings = NewSettings };
    }

    private static ImmutableList<Clip> SnapClips(ImmutableList<Clip> clips, FrameRate rate)
    {
        var result = ImmutableList.CreateBuilder<Clip>();
        var previousEnd = MediaTime.Zero;
        foreach (var clip in clips.OrderBy(c => c.Start))
        {
            var start = MediaTime.Max(rate.SnapToFrame(clip.Start), previousEnd);
            var end = MediaTime.Max(rate.SnapToFrame(clip.End), start + rate.FrameDuration);
            result.Add(clip with { Start = start, Duration = end - start });
            previousEnd = end;
        }

        return result.ToImmutable();
    }
}

public sealed record SetStorageModeCommand(StorageMode Mode) : IEditCommand
{
    public string Description => Mode == StorageMode.Packaged ? "Empacotar mídias no projeto" : "Vincular mídias externas";

    public Project Apply(Project project) =>
        project.StorageMode == Mode ? project : project with { StorageMode = Mode };
}

public sealed record AddMediaAssetsCommand(IReadOnlyList<MediaAsset> Assets) : IEditCommand
{
    public string Description => Assets.Count == 1 ? $"Importar \"{Assets[0].Name}\"" : $"Importar {Assets.Count} mídias";

    public Project Apply(Project project)
    {
        if (Assets.Count == 0) return project;
        var existing = project.Media.Select(m => m.Id).ToHashSet();
        if (Assets.Any(a => existing.Contains(a.Id)))
            throw new EditRejectedException("A mídia já faz parte do projeto.");
        return project with { Media = project.Media.AddRange(Assets) };
    }
}

public sealed record RemoveMediaAssetCommand(Guid AssetId) : IEditCommand
{
    public string Description => "Remover mídia";

    public Project Apply(Project project)
    {
        var asset = project.FindMedia(AssetId) ?? throw new EditRejectedException("Mídia não encontrada.");
        var inUse = project.Sequences.SelectMany(s => s.Tracks).SelectMany(t => t.Clips).Any(c => c.MediaAssetId == AssetId);
        if (inUse)
            throw new EditRejectedException($"A mídia \"{asset.Name}\" está em uso na linha do tempo. Remova os clipes antes.");
        return project with { Media = project.Media.Remove(asset) };
    }
}

/// <summary>Atualiza a localização de uma mídia ausente (RF017).</summary>
public sealed record RelinkMediaCommand(Guid AssetId, string NewPath, string? NewRelativePath, long FileSize, DateTime LastWriteUtc)
    : IEditCommand
{
    public string Description => "Localizar mídia";

    public Project Apply(Project project)
    {
        var asset = project.FindMedia(AssetId) ?? throw new EditRejectedException("Mídia não encontrada.");
        var updated = asset with
        {
            OriginalPath = NewPath,
            RelativePath = NewRelativePath,
            FileSize = FileSize,
            LastWriteUtc = LastWriteUtc,
        };
        return project with { Media = project.Media.Replace(asset, updated) };
    }
}

public sealed record AddTrackCommand(Guid SequenceId, TrackKind Kind, string Name, int? Index = null) : IEditCommand
{
    public string Description => $"Adicionar trilha \"{Name}\"";

    public Project Apply(Project project)
    {
        var seq = project.Sequences.Find(s => s.Id == SequenceId) ?? throw new EditRejectedException("Sequência não encontrada.");
        var track = new Track { Id = Guid.NewGuid(), Kind = Kind, Name = Name };
        var index = Math.Clamp(Index ?? seq.Tracks.Count, 0, seq.Tracks.Count);
        return project.WithSequence(seq with { Tracks = seq.Tracks.Insert(index, track) });
    }
}

public sealed record RemoveTrackCommand(Guid SequenceId, Guid TrackId) : IEditCommand
{
    public string Description => "Remover trilha";

    public Project Apply(Project project)
    {
        var seq = project.Sequences.Find(s => s.Id == SequenceId) ?? throw new EditRejectedException("Sequência não encontrada.");
        var track = seq.Tracks.Find(t => t.Id == TrackId) ?? throw new EditRejectedException("Trilha não encontrada.");
        if (track.IsLocked) throw new EditRejectedException($"A trilha \"{track.Name}\" está bloqueada.");
        return project.WithSequence(seq with { Tracks = seq.Tracks.Remove(track) });
    }
}

/// <summary>Renomeia, bloqueia, oculta ou silencia uma trilha (RF054–RF056).</summary>
public sealed record UpdateTrackCommand(Guid SequenceId, Guid TrackId, Func<Track, Track> Update, string Description) : IEditCommand
{
    public Project Apply(Project project)
    {
        var seq = project.Sequences.Find(s => s.Id == SequenceId) ?? throw new EditRejectedException("Sequência não encontrada.");
        var track = seq.Tracks.Find(t => t.Id == TrackId) ?? throw new EditRejectedException("Trilha não encontrada.");
        var updated = Update(track);
        if (updated == track) return project;
        if (updated.Id != track.Id || updated.Kind != track.Kind || !ReferenceEquals(updated.Clips, track.Clips))
            throw new InvalidOperationException("UpdateTrackCommand só pode alterar propriedades da trilha.");
        return project.WithSequence(seq with { Tracks = seq.Tracks.Replace(track, updated) });
    }

    public static UpdateTrackCommand SetLocked(Guid seq, Guid track, bool value) =>
        new(seq, track, t => t with { IsLocked = value }, value ? "Bloquear trilha" : "Desbloquear trilha");

    public static UpdateTrackCommand SetHidden(Guid seq, Guid track, bool value) =>
        new(seq, track, t => t with { IsHidden = value }, value ? "Ocultar trilha" : "Exibir trilha");

    public static UpdateTrackCommand SetMuted(Guid seq, Guid track, bool value) =>
        new(seq, track, t => t with { IsMuted = value }, value ? "Silenciar trilha" : "Ativar som da trilha");

    public static UpdateTrackCommand Rename(Guid seq, Guid track, string name) =>
        new(seq, track, t => t with { Name = name.Trim().Length > 0 ? name.Trim() : t.Name }, "Renomear trilha");
}

public sealed record AddMarkerCommand(Guid SequenceId, MediaTime Time, string Name = "") : IEditCommand
{
    public string Description => "Adicionar marcador";

    public Project Apply(Project project)
    {
        var seq = project.Sequences.Find(s => s.Id == SequenceId) ?? throw new EditRejectedException("Sequência não encontrada.");
        var time = project.Settings.FrameRate.SnapToFrame(MediaTime.Max(Time, MediaTime.Zero));
        var marker = new Marker { Id = Guid.NewGuid(), Time = time, Name = Name };
        return project.WithSequence(seq with { Markers = seq.Markers.Add(marker).Sort((a, b) => a.Time.CompareTo(b.Time)) });
    }
}

public sealed record RemoveMarkerCommand(Guid SequenceId, Guid MarkerId) : IEditCommand
{
    public string Description => "Remover marcador";

    public Project Apply(Project project)
    {
        var seq = project.Sequences.Find(s => s.Id == SequenceId) ?? throw new EditRejectedException("Sequência não encontrada.");
        var marker = seq.Markers.Find(m => m.Id == MarkerId) ?? throw new EditRejectedException("Marcador não encontrado.");
        return project.WithSequence(seq with { Markers = seq.Markers.Remove(marker) });
    }
}
