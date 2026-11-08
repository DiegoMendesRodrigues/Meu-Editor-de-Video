using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Model;

public enum MediaKind
{
    Video,
    Audio,
    Image,
    ImageSequence,
    AnimatedGif,
}

/// <summary>
/// Mídia importada no projeto. Os arquivos originais nunca são modificados (edição não destrutiva, §6.1).
/// </summary>
public sealed record MediaAsset
{
    public required Guid Id { get; init; }
    public required MediaKind Kind { get; init; }
    public required string Name { get; init; }

    /// <summary>Caminho absoluto no momento da importação/última localização.</summary>
    public required string OriginalPath { get; init; }

    /// <summary>Caminho relativo à pasta do arquivo .med (permite mover projeto e mídias juntos).</summary>
    public string? RelativePath { get; init; }

    /// <summary>Entrada dentro do .med quando o projeto é independente (ex.: "media/videos/{id}.mp4").</summary>
    public string? PackagedEntry { get; init; }

    public long FileSize { get; init; }
    public DateTime LastWriteUtc { get; init; }

    /// <summary>Impressão digital rápida (SHA-256 de tamanho + início + fim), usada para relocalizar arquivos.</summary>
    public string? Fingerprint { get; init; }

    public MediaTime Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public FrameRate? FrameRate { get; init; }
    public string? Category { get; init; }
}
