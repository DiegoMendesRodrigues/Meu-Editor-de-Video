using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Core.Media;

public enum MediaProbeStatus
{
    Ok,

    /// <summary>Formato ou codec não suportado.</summary>
    Unsupported,

    /// <summary>Arquivo danificado, incompleto ou que não pôde ser decodificado.</summary>
    Corrupted,
}

/// <summary>Resultado da análise de um arquivo de mídia (RF029–RF032, RF036).</summary>
public sealed record MediaProbeResult(
    string Path,
    MediaProbeStatus Status,
    MediaKind Kind,
    MediaTime Duration,
    int Width,
    int Height,
    FrameRate? FrameRate,
    MediaTechnicalInfo Info,
    string? Error = null)
{
    public bool IsOk => Status == MediaProbeStatus.Ok;

    public static MediaProbeResult Failed(string path, MediaProbeStatus status, string error) =>
        new(path, status, MediaKind.Video, MediaTime.Zero, 0, 0, null, new MediaTechnicalInfo(), error);
}

/// <summary>Imagem BGRA 32 bits (pré-multiplicada não), linha a linha sem preenchimento (Stride = Width * 4).</summary>
public sealed record BgraImage(int Width, int Height, byte[] Pixels)
{
    public int Stride => Width * 4;
}

/// <summary>Picos de áudio para desenhar a forma de onda (valores −1…1, mono).</summary>
public sealed record WaveformData(int PeaksPerSecond, float[] Min, float[] Max, MediaTime Duration)
{
    public int Count => Min.Length;
}

public interface IMediaProbe
{
    /// <summary>Analisa o arquivo e tenta decodificar o primeiro quadro/amostras para detectar arquivos danificados.</summary>
    Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Analisa uma sequência de imagens numeradas (RF025).</summary>
    Task<MediaProbeResult> ProbeSequenceAsync(ImageSequenceInfo sequence, FrameRate frameRate, CancellationToken cancellationToken = default);
}

public interface IThumbnailProvider
{
    /// <summary>
    /// Miniatura do quadro mais próximo de <paramref name="at"/>, com largura máxima <paramref name="maxWidth"/>.
    /// Resultados ficam em cache em disco. Retorna null se não houver vídeo/imagem.
    /// </summary>
    Task<BgraImage?> GetThumbnailAsync(MediaAsset asset, string sourcePath, MediaTime at, int maxWidth, CancellationToken cancellationToken = default);
}

public interface IWaveformProvider
{
    /// <summary>Forma de onda do áudio (em cache em disco). Retorna null se não houver áudio.</summary>
    Task<WaveformData?> GetWaveformAsync(MediaAsset asset, string sourcePath, CancellationToken cancellationToken = default);
}

/// <summary>Extensões aceitas na importação (§4.1–4.3). A decisão final é do FFmpeg (probe).</summary>
public static class MediaFormats
{
    public static readonly IReadOnlySet<string> Video = Set(
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".m2ts", ".mts", ".ts", ".3gp", ".ogv", ".mxf", ".vob");

    public static readonly IReadOnlySet<string> Audio = Set(
        ".mp3", ".wav", ".aac", ".m4a", ".flac", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif", ".ac3", ".amr");

    public static readonly IReadOnlySet<string> Image = Set(
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".tga", ".jfif", ".svg");

    public static readonly IReadOnlySet<string> Gif = Set(".gif");

    public static bool IsSupported(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return Video.Contains(ext) || Audio.Contains(ext) || Image.Contains(ext) || Gif.Contains(ext);
    }

    public static bool IsImage(string path) => Image.Contains(System.IO.Path.GetExtension(path));

    /// <summary>Filtro para a janela "Abrir arquivo".</summary>
    public static string OpenFileFilter =>
        "Mídias suportadas|" + Pattern(Video.Concat(Audio).Concat(Image).Concat(Gif)) +
        "|Vídeos|" + Pattern(Video) +
        "|Áudios|" + Pattern(Audio) +
        "|Imagens e GIFs|" + Pattern(Image.Concat(Gif)) +
        "|Todos os arquivos|*.*";

    private static string Pattern(IEnumerable<string> exts) => string.Join(";", exts.Select(e => "*" + e));

    private static HashSet<string> Set(params string[] exts) => new(exts, StringComparer.OrdinalIgnoreCase);
}
