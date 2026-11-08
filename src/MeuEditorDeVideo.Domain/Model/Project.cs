using System.Collections.Immutable;

namespace MeuEditorDeVideo.Domain.Model;

/// <summary>
/// Modalidade de armazenamento (§1.3): projeto vinculado a arquivos externos ou independente,
/// com as mídias empacotadas dentro do .med (RF018/RF019).
/// </summary>
public enum StorageMode
{
    Linked,
    Packaged,
}

/// <summary>Raiz do modelo de edição. Imutável: toda alteração gera um novo estado (base do desfazer/refazer).</summary>
public sealed record Project
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public ProjectSettings Settings { get; init; } = new();
    public StorageMode StorageMode { get; init; } = StorageMode.Linked;
    public ImmutableList<MediaAsset> Media { get; init; } = [];
    public required ImmutableList<Sequence> Sequences { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }

    public Sequence MainSequence => Sequences[0];

    public MediaAsset? FindMedia(Guid id) => Media.Find(m => m.Id == id);

    /// <summary>Cria um projeto com as trilhas padrão exibidas no protótipo.</summary>
    public static Project CreateNew(string name, ProjectSettings settings, StorageMode mode, DateTimeOffset now)
    {
        var sequence = new Sequence
        {
            Id = Guid.NewGuid(),
            Name = "Sequência principal",
            Tracks =
            [
                new Track { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Vídeo 1" },
                new Track { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Vídeo 2" },
                new Track { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = "Áudio 1" },
                new Track { Id = Guid.NewGuid(), Kind = TrackKind.Text, Name = "Texto 1" },
            ],
        };

        return new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Settings = settings,
            StorageMode = mode,
            Sequences = [sequence],
            CreatedAt = now,
            ModifiedAt = now,
        };
    }

    /// <summary>Substitui uma sequência (pelo Id) preservando a ordem.</summary>
    public Project WithSequence(Sequence sequence)
    {
        var index = Sequences.FindIndex(s => s.Id == sequence.Id);
        if (index < 0) throw new InvalidOperationException("Sequência não encontrada no projeto.");
        return this with { Sequences = Sequences.SetItem(index, sequence) };
    }
}
