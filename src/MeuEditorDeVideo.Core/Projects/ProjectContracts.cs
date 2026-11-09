using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Core.Projects;

/// <summary>Resultado da abertura de um arquivo .med.</summary>
public sealed record LoadedProject(
    Project Project,
    string FilePath,
    string FormatVersionOnDisk,
    bool WasMigrated,
    IReadOnlyList<string> Warnings);

/// <summary>Pedido de gravação de um arquivo .med.</summary>
/// <param name="Project">Estado a gravar.</param>
/// <param name="TargetPath">Caminho final do .med.</param>
/// <param name="IncludeMedia">Copiar as mídias para dentro do .med (projeto independente).</param>
/// <param name="MediaSources">Caminho atual no disco de cada mídia (necessário quando <paramref name="IncludeMedia"/>).</param>
/// <param name="PreservePaths">
/// Não recalcular caminhos relativos nem entradas de pacote (usado pelo autosave, que grava em outra pasta).
/// </param>
public sealed record SaveRequest(
    Project Project,
    string TargetPath,
    bool IncludeMedia,
    IReadOnlyDictionary<Guid, string> MediaSources,
    bool PreservePaths = false);

/// <summary>Leitura e gravação do formato .med (§9). Implementado pelo ProjectEngine.</summary>
public interface IProjectStore
{
    /// <summary>Versão do formato gravada por esta versão do aplicativo.</summary>
    string CurrentFormatVersion { get; }

    /// <summary>Abre, verifica integridade, migra e valida um projeto. Lança MedException com mensagem pt-BR.</summary>
    Task<LoadedProject> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava de forma atômica (arquivo temporário + substituição), devolvendo o projeto efetivamente gravado
    /// (com caminhos relativos/entradas de pacote atualizados).
    /// </summary>
    Task<Project> SaveAsync(SaveRequest request, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copia o arquivo .med atual para a pasta de cópias de segurança (RF015). Retorna o caminho da cópia.</summary>
    Task<string?> CreateBackupAsync(string projectPath, Guid projectId, CancellationToken cancellationToken = default);
}

public enum MediaStatus
{
    /// <summary>Encontrada e igual à importada.</summary>
    Available,

    /// <summary>Encontrada, mas tamanho/data diferentes do registrado.</summary>
    Changed,

    /// <summary>Não encontrada em nenhum local conhecido (RF016).</summary>
    Missing,
}

public sealed record MediaLocation(Guid AssetId, MediaStatus Status, string? ResolvedPath);

/// <summary>Localização das mídias de um projeto no disco.</summary>
public sealed record MediaResolution(IReadOnlyDictionary<Guid, MediaLocation> Locations)
{
    public static readonly MediaResolution Empty = new(new Dictionary<Guid, MediaLocation>());

    public IEnumerable<MediaLocation> Missing => Locations.Values.Where(l => l.Status == MediaStatus.Missing);

    public IReadOnlyDictionary<Guid, string> AvailablePaths =>
        Locations.Values.Where(l => l.ResolvedPath is not null).ToDictionary(l => l.AssetId, l => l.ResolvedPath!);
}

/// <summary>Candidato encontrado ao procurar uma mídia ausente.</summary>
public sealed record RelinkCandidate(Guid AssetId, string Path, bool FingerprintMatches);

public interface IMediaLocator
{
    /// <summary>
    /// Resolve cada mídia: entrada empacotada extraída → caminho absoluto → caminho relativo ao .med.
    /// Mídias empacotadas são extraídas para o cache local na primeira vez.
    /// </summary>
    Task<MediaResolution> ResolveAsync(Project project, string? projectFilePath, CancellationToken cancellationToken = default);

    /// <summary>Procura mídias ausentes em uma pasta (recursivamente), por nome e tamanho, conferindo a impressão digital.</summary>
    Task<IReadOnlyList<RelinkCandidate>> FindCandidatesAsync(
        IEnumerable<MediaAsset> assets, string folder, CancellationToken cancellationToken = default);

    /// <summary>Calcula a impressão digital rápida de um arquivo (mesmo algoritmo usado na importação).</summary>
    Task<string> ComputeFingerprintAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Projeto que pode ser recuperado após encerramento inesperado (RF011).</summary>
public sealed record RecoverableProject(Guid ProjectId, string Name, string? OriginalPath, string AutosavePath, DateTimeOffset SavedAt);

/// <summary>Salvamento automático e detecção de sessões encerradas de forma inesperada.</summary>
public interface IAutosaveStore
{
    /// <summary>Registra que o projeto está aberto nesta sessão (marca de sessão ativa).</summary>
    void BeginSession(Guid projectId, string projectName, string? originalPath);

    /// <summary>Encerramento normal: remove marca de sessão e o autosave.</summary>
    void EndSession(Guid projectId);

    Task SaveAsync(Project project, string? originalPath, IReadOnlyDictionary<Guid, string> mediaSources, CancellationToken cancellationToken = default);

    /// <summary>Sessões de outros processos que não foram encerradas normalmente e possuem autosave.</summary>
    IReadOnlyList<RecoverableProject> FindRecoverable();

    void Discard(Guid projectId);
}

/// <summary>Serialização JSON do domínio (mesmas regras do .med): usada pela fila de renderização para guardar fotos do projeto.</summary>
public interface IDomainJson
{
    string Serialize<T>(T value);

    T? Deserialize<T>(string json);
}
