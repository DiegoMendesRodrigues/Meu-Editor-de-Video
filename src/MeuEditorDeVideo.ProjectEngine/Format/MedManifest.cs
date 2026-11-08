using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.ProjectEngine.Format;

/// <summary>Entrada do manifesto: arquivo interno do .med com tamanho e hash (§9.2).</summary>
public sealed record MedManifestFile
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
}

/// <summary>Conteúdo de manifest.json.</summary>
public sealed record MedManifest
{
    public const string FormatName = "MED";

    public string Format { get; init; } = FormatName;
    public required string FormatVersion { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public required StorageMode StorageMode { get; init; }
    public string? AppVersion { get; init; }
    public IReadOnlyList<MedManifestFile> Files { get; init; } = [];
}

/// <summary>Constantes do contêiner .med (§9.1).</summary>
public static class MedLayout
{
    public const string Manifest = "manifest.json";
    public const string Project = "project.json";
    public const string MediaRoot = "media/";

    public static string MediaFolder(MediaKind kind) => kind switch
    {
        MediaKind.Video => "media/videos/",
        MediaKind.Audio => "media/audios/",
        MediaKind.Image or MediaKind.ImageSequence or MediaKind.AnimatedGif => "media/images/",
        _ => "media/graphics/",
    };

    public static bool IsMedia(string entry) => entry.StartsWith(MediaRoot, StringComparison.Ordinal);
}
