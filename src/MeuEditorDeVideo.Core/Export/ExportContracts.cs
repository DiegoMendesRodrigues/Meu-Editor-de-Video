using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Core.Export;

/// <summary>Contêineres de saída (RF171–RF177, plano §4.4).</summary>
public enum ExportContainer
{
    Mp4,
    Mov,
    Mkv,
    WebM,
    Avi,
    Mpeg,
    Gif,
}

public enum VideoCodec
{
    H264,
    Hevc,
    Av1,
    Vp9,
    ProRes,
    Ffv1,
    QtAnimation,
    Mjpeg,
    Mpeg2,
    Gif,
}

public enum AudioCodec
{
    None,
    Aac,
    Pcm,
    Opus,
    Flac,
    Vorbis,
    Mp3,
    Mp2,
}

/// <summary>Preferência de codificador: automático (GPU se houver), somente GPU (com recuo para software) ou somente software.</summary>
public enum EncoderPreference
{
    Auto,
    Hardware,
    Software,
}

public enum RateControl
{
    /// <summary>Taxa variável com média em <see cref="ExportSettings.VideoBitrateKbps"/>.</summary>
    Vbr,

    /// <summary>Taxa constante.</summary>
    Cbr,

    /// <summary>Qualidade constante (CRF/CQ) em <see cref="ExportSettings.Quality"/>.</summary>
    ConstantQuality,
}

/// <summary>Parâmetros de uma exportação. Campos nulos herdam do projeto.</summary>
public sealed record ExportSettings
{
    public ExportContainer Container { get; init; } = ExportContainer.Mp4;
    public VideoCodec VideoCodec { get; init; } = VideoCodec.H264;
    public EncoderPreference Encoder { get; init; } = EncoderPreference.Auto;

    /// <summary>Largura de saída (null = do projeto).</summary>
    public int? Width { get; init; }

    /// <summary>Altura de saída (null = do projeto).</summary>
    public int? Height { get; init; }

    /// <summary>Taxa de quadros (null = do projeto).</summary>
    public FrameRate? FrameRate { get; init; }

    public RateControl RateControl { get; init; } = RateControl.Vbr;
    public int VideoBitrateKbps { get; init; } = 8000;

    /// <summary>Qualidade constante no estilo CRF (0 = sem perdas aparente, 51 = pior). Padrão 23.</summary>
    public int Quality { get; init; } = 23;

    /// <summary>Perfil do codec (ex.: "high", "main", "baseline"); null = padrão do codificador.</summary>
    public string? Profile { get; init; }

    public AudioCodec AudioCodec { get; init; } = AudioCodec.Aac;
    public int AudioBitrateKbps { get; init; } = 192;
    public int SampleRate { get; init; } = 48_000;
    public int AudioChannels { get; init; } = 2;

    /// <summary>Exporta o canal alfa (fundo transparente) quando o formato permite.</summary>
    public bool Alpha { get; init; }

    /// <summary>Divide a saída em arquivos de no máximo esta duração (ex.: status do WhatsApp).</summary>
    public double? MaxSecondsPerFile { get; init; }

    /// <summary>Tamanho alvo por arquivo; a taxa de bits do vídeo é calculada a partir dele.</summary>
    public double? TargetSizeMegabytes { get; init; }

    public int ResolveWidth(ProjectSettings project) => Width ?? project.Width;

    public int ResolveHeight(ProjectSettings project) => Height ?? project.Height;

    public FrameRate ResolveFrameRate(ProjectSettings project) => FrameRate ?? project.FrameRate;
}

/// <summary>Modelo de exportação (§10): embutido (somente leitura, restaurável) ou do usuário.</summary>
public sealed record ExportTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public ExportSettings Settings { get; init; } = new();

    /// <summary>Modelo distribuído com o aplicativo (não é apagado; edições viram um modelo do usuário).</summary>
    public bool IsBuiltIn { get; init; }
}

public interface IExportTemplateStore
{
    /// <summary>Modelos embutidos seguidos pelos do usuário. Um modelo do usuário com o mesmo Id substitui o embutido.</summary>
    IReadOnlyList<ExportTemplate> GetAll();

    /// <summary>Versão original de um modelo embutido (para "restaurar padrão").</summary>
    ExportTemplate? GetBuiltIn(string id);

    Task SaveAsync(ExportTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Remove um modelo do usuário (ou a personalização de um embutido).</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Descarta todas as personalizações dos modelos embutidos.</summary>
    Task RestoreDefaultsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Codificador disponível nesta máquina (sondado em tempo de execução).</summary>
public sealed record EncoderInfo(string Name, string DisplayName, bool IsHardware);

/// <summary>Pedido de renderização. A sequência e as mídias são uma foto imutável do projeto.</summary>
public sealed record RenderRequest
{
    public required Project Project { get; init; }
    public required Guid SequenceId { get; init; }
    public required IReadOnlyDictionary<Guid, string> MediaPaths { get; init; }
    public required ExportSettings Settings { get; init; }

    /// <summary>Arquivo de saída (com extensão). Na divisão por duração, recebe " (1)", " (2)"…</summary>
    public required string OutputPath { get; init; }

    public MediaTime RangeStart { get; init; } = MediaTime.Zero;

    /// <summary>Fim do intervalo (null = fim da sequência).</summary>
    public MediaTime? RangeEnd { get; init; }

    /// <summary>Pasta de trabalho dos trechos já codificados (permite retomar).</summary>
    public required string WorkDirectory { get; init; }

    public bool HardwareDecoding { get; init; } = true;
}

public enum RenderStage
{
    Preparing,
    Encoding,
    Muxing,
    Validating,
}

public sealed record RenderProgress(
    RenderStage Stage,
    double Fraction,
    long FramesDone,
    long TotalFrames,
    double FramesPerSecond,
    TimeSpan Elapsed,
    TimeSpan? Remaining,
    int Part,
    int Parts);

public sealed record RenderResult(IReadOnlyList<string> Files, string VideoEncoder, TimeSpan Elapsed, IReadOnlyList<string> Warnings);

/// <summary>Motor de renderização (§7): avalia a timeline, compõe, codifica, multiplexa e valida.</summary>
public interface IRenderEngine
{
    /// <summary>Codificadores de vídeo utilizáveis para o codec, do mais rápido ao recuo em software.</summary>
    IReadOnlyList<EncoderInfo> AvailableEncoders(VideoCodec codec);

    Task<RenderResult> RenderAsync(RenderRequest request, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default);
}
