using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.Core.AI;

/// <summary>Função de um modelo de IA (§8.2). Cada recurso de IA depende de um modelo com a capacidade correspondente.</summary>
public enum ModelCapability
{
    /// <summary>Segmentação de pessoas: remoção/substituição de fundo e rastreamento inteligente (RF102–RF109, RF156, RF162, RF163).</summary>
    PersonSegmentation,

    /// <summary>Reconhecimento de fala → legendas (RF142, RF157, RF158).</summary>
    SpeechRecognition,

    /// <summary>Isolamento de voz e redução inteligente de ruído (RF127, RF159, RF160).</summary>
    SpeechEnhancement,

    /// <summary>Detecção de intervalos de fala/silêncio (RF165).</summary>
    VoiceActivity,

    /// <summary>Detecção de mudanças de cena (RF161).</summary>
    SceneDetection,

    /// <summary>Melhoria de imagem / super-resolução (RF164).</summary>
    ImageEnhancement,
}

/// <summary>Dispositivo de execução escolhido pelo usuário (RF167). GPU usa DirectML e cai para CPU em caso de falha.</summary>
public enum AiDevice
{
    Cpu,
    Gpu,
}

/// <summary>
/// Manifesto de um modelo (§8.3), gravado como <c>manifest.json</c> dentro do pacote <c>.medmodel</c> e na pasta do modelo
/// instalado. Campos em camelCase no JSON.
/// </summary>
public sealed record ModelManifest
{
    /// <summary>Identificador estável (ex.: <c>modnet-photographic</c>); nome da pasta de instalação.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public required string Version { get; init; }

    /// <summary>Fabricante ou origem (autores/projeto de onde o modelo foi obtido).</summary>
    public required string Source { get; init; }

    public required ModelCapability Capability { get; init; }

    /// <summary>Finalidade em linguagem simples (exibida ao usuário).</summary>
    public required string Purpose { get; init; }

    /// <summary>Formato do arquivo: <c>ONNX</c> ou <c>GGML</c> (Whisper).</summary>
    public required string Format { get; init; }

    /// <summary>Nome do arquivo do modelo dentro do pacote/pasta.</summary>
    public required string File { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>Memória estimada necessária durante a execução (MB) — RF168.</summary>
    public int MemoryMb { get; init; }

    public bool Cpu { get; init; } = true;
    public bool Gpu { get; init; }

    /// <summary>Licença do modelo (identificador SPDX ou descrição).</summary>
    public required string License { get; init; }

    /// <summary>Arquivo com o texto da licença dentro do pacote (ex.: <c>LICENSE.txt</c>).</summary>
    public string? LicenseFile { get; init; }

    /// <summary>SHA-256 (hex minúsculo) do arquivo do modelo — integridade.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Documentação de uso dentro do pacote (ex.: <c>README.md</c>).</summary>
    public string? Documentation { get; init; }

    /// <summary>Versão mínima do formato de pacote/aplicativo compatível (compatibilidade). Atual: 1.</summary>
    public int PackageFormat { get; init; } = 1;

    /// <summary>Parâmetros específicos do modelo (tamanho de entrada, idioma, taxa de amostragem…).</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>Modelo instalado em <c>%LOCALAPPDATA%\MeuEditorDeVideo\ai-models\&lt;id&gt;</c>.</summary>
/// <param name="BuiltIn">Modelo que vem com o aplicativo (pasta <c>ai-models</c> ao lado do executável): sempre disponível e não pode ser removido.</param>
public sealed record InstalledModel(ModelManifest Manifest, string Directory, bool BuiltIn = false)
{
    public string ModelPath => Path.Combine(Directory, Manifest.File);
    public string? DocumentationPath => Manifest.Documentation is { } d ? Path.Combine(Directory, d) : null;
    public string? LicensePath => Manifest.LicenseFile is { } l ? Path.Combine(Directory, l) : null;
}

/// <summary>Gerenciador de modelos (RF166, RF168, RF170). Só instala a partir de pacotes locais; o download do catálogo oficial fica em <see cref="IModelDownloader"/>.</summary>
public interface IModelRegistry
{
    /// <summary>Formato de pacote suportado por esta versão do aplicativo.</summary>
    const int SupportedPackageFormat = 1;

    IReadOnlyList<InstalledModel> Installed { get; }

    /// <summary>Modelo instalado para a capacidade (o mais recente se houver mais de um), ou null — recurso indisponível.</summary>
    InstalledModel? Find(ModelCapability capability);

    bool IsAvailable(ModelCapability capability);

    /// <summary>
    /// Instala um pacote <c>.medmodel</c> (zip com manifest.json, o modelo, licença e documentação): valida o manifesto,
    /// a compatibilidade e o SHA-256 antes de copiar. Substitui uma instalação anterior com o mesmo Id.
    /// Lança <see cref="InvalidDataException"/> com mensagem em pt-BR quando o pacote é inválido.
    /// </summary>
    Task<InstalledModel> InstallAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>Remove um modelo instalado pelo usuário. Modelos incluídos no aplicativo não podem ser removidos (<see cref="InvalidOperationException"/>).</summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Recalcula o hash do arquivo instalado e compara com o manifesto.</summary>
    Task<bool> VerifyAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Relê a pasta de modelos.</summary>
    void Refresh();

    event EventHandler? Changed;
}

/// <summary>Trecho transcrito, em tempo da timeline.</summary>
public sealed record TranscriptSegment(MediaTime Start, MediaTime End, string Text);

/// <summary>Reconhecimento de fala local (Whisper) — RF142, RF157, RF158.</summary>
public interface ISpeechRecognitionService
{
    bool IsAvailable { get; }

    /// <param name="language">Código ISO (ex.: "pt") ou "auto".</param>
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        string language, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Detecção de fala (VAD) — RF165. Intervalos em tempo da timeline.</summary>
public interface IVoiceActivityService
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<TimeRange>> DetectSpeechAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        double threshold = 0.5, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public enum VoiceProcessingMode
{
    /// <summary>Separa voz e demais sons em dois arquivos (RF127, RF159).</summary>
    Isolate,

    /// <summary>Gera um arquivo com o ruído reduzido (RF160).</summary>
    Denoise,
}

/// <summary>Arquivos WAV (48 kHz estéreo) gerados; duram exatamente o clipe na timeline.</summary>
public sealed record VoiceProcessingResult(string VoicePath, string? ResidualPath);

public interface IVoiceEnhancementService
{
    bool IsAvailable { get; }

    /// <param name="strength">0…1: no modo Denoise, quanto do som original é removido (1 = só a voz).</param>
    Task<VoiceProcessingResult> ProcessAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        VoiceProcessingMode mode, double strength, string outputDirectory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado da detecção de cenas: cortes em tempo da timeline (dentro do clipe, sem o início).</summary>
public sealed record SceneDetectionResult(IReadOnlyList<MediaTime> Cuts, bool UsedModel);

/// <summary>Detecção de cenas (RF161). Sem o modelo usa comparação de histogramas (sempre disponível).</summary>
public interface ISceneDetectionService
{
    bool ModelAvailable { get; }

    /// <param name="sensitivity">0…1 (maior = mais cortes).</param>
    Task<SceneDetectionResult> DetectAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        double sensitivity = 0.5, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Máscara de primeiro plano (0 = fundo, 255 = pessoa) na resolução de inferência.</summary>
public sealed record Matte(int Width, int Height, byte[] Alpha);

/// <summary>Segmentação de pessoas com cache de máscaras em memória e disco (RF156, RF162, RF163).</summary>
public interface ISegmentationService
{
    bool IsAvailable { get; }

    /// <summary>
    /// Máscara do quadro (do cache quando existir). Com <paramref name="path"/>, as máscaras são calculadas sempre a partir
    /// do quadro de origem decodificado no tamanho de análise, para que a pré-visualização e a exportação tenham o mesmo
    /// resultado; <paramref name="frame"/> é usado só quando a mídia não pode ser lida. <paramref name="temporalSmoothing"/>
    /// (0…1) mistura a máscara com as dos quadros vizinhos para reduzir a oscilação da borda.
    /// </summary>
    Matte? GetMatte(MediaAsset asset, string? path, MediaTime sourceTime, BgraImage frame, double temporalSmoothing = 0);

    /// <summary>Calcula em segundo plano as máscaras de todos os quadros do clipe e guarda no cache em disco.</summary>
    Task PrecomputeAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rastreamento inteligente: caixa da pessoa em cada quadro (coordenadas normalizadas 0…1 da imagem).</summary>
    Task<IReadOnlyList<TrackSample>> TrackPersonAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Apaga o cache de máscaras do arquivo de mídia.</summary>
    void ClearCache(Guid assetId);
}

/// <summary>Aplica efeitos de IA de imagem (<c>ai.*</c>) a um quadro durante a renderização.</summary>
public interface IAiFrameProcessor
{
    /// <summary>
    /// Aplica <paramref name="effect"/> a <paramref name="image"/> (quadro de <paramref name="asset"/> em
    /// <paramref name="sourceTime"/>). <paramref name="background"/> fornece, sob demanda, o quadro de fundo do modo
    /// "imagem ou vídeo" já no tamanho da imagem. <paramref name="path"/> é o arquivo da mídia (para ler quadros vizinhos).
    /// Retorna a imagem original quando o modelo não está disponível.
    /// </summary>
    (BgraImage Image, bool AlphaChanged) Apply(EffectInstance effect, BgraImage image, MediaAsset asset, string? path, MediaTime sourceTime,
        Func<int, int, BgraImage?> background, double pixelScale);
}

/// <summary>Melhoria de imagem por IA (RF164): super-resolução 2× de fotos.</summary>
public interface IImageEnhancementService
{
    bool IsAvailable { get; }

    Task EnhanceImageAsync(string inputPath, string outputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
