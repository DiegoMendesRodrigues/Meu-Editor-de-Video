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

    /// <summary>Detalhes técnicos identificados na importação (RF029–RF032, RF035).</summary>
    public MediaTechnicalInfo? Info { get; init; }

    /// <summary>Somente para <see cref="MediaKind.ImageSequence"/>: padrão dos arquivos numerados.</summary>
    public ImageSequenceInfo? Sequence { get; init; }

    public bool HasVideo => Kind != MediaKind.Audio;
    public bool HasAudio => Kind == MediaKind.Audio || (Kind == MediaKind.Video && Info?.AudioCodec is not null);
}

/// <summary>Informações técnicas do arquivo (somente leitura, obtidas pelo FFmpeg).</summary>
public sealed record MediaTechnicalInfo
{
    /// <summary>Nome do contêiner (ex.: "mov,mp4,m4a,3gp,3g2,mj2").</summary>
    public string? Container { get; init; }
    public string? ContainerLongName { get; init; }
    public string? VideoCodec { get; init; }
    public string? VideoCodecLongName { get; init; }
    public string? PixelFormat { get; init; }
    public bool HasAlpha { get; init; }

    /// <summary>Rotação de exibição em graus (0, 90, 180, 270), comum em vídeos de celular.</summary>
    public int Rotation { get; init; }

    public string? AudioCodec { get; init; }
    public string? AudioCodecLongName { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public string? ChannelLayout { get; init; }

    /// <summary>Taxa de bits total em bits/s (0 se desconhecida).</summary>
    public long BitRate { get; init; }
    public int VideoStreams { get; init; }
    public int AudioStreams { get; init; }
    public int SubtitleStreams { get; init; }
}

/// <summary>Sequência de imagens numeradas (ex.: quadro_0001.png … quadro_0240.png).</summary>
/// <param name="Directory">Pasta das imagens.</param>
/// <param name="Prefix">Texto antes do número.</param>
/// <param name="Suffix">Texto depois do número, incluindo a extensão.</param>
/// <param name="Digits">Quantidade de dígitos com zeros à esquerda (0 = sem preenchimento).</param>
public sealed record ImageSequenceInfo(string Directory, string Prefix, string Suffix, int Digits, int FirstNumber, int FrameCount)
{
    public string FileName(int number) =>
        Prefix + (Digits > 0 ? number.ToString("D" + Digits, System.Globalization.CultureInfo.InvariantCulture)
                             : number.ToString(System.Globalization.CultureInfo.InvariantCulture)) + Suffix;

    public string PathOf(int number) => System.IO.Path.Combine(Directory, FileName(number));

    /// <summary>Padrão no formato do FFmpeg (ex.: "quadro_%04d.png").</summary>
    public string FfmpegPattern => System.IO.Path.Combine(Directory,
        Prefix.Replace("%", "%%") + (Digits > 0 ? $"%0{Digits}d" : "%d") + Suffix.Replace("%", "%%"));
}
