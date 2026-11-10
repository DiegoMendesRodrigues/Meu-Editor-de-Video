namespace MeuEditorDeVideo.Core.AI;

/// <summary>
/// Modelo do catálogo oficial (resources/ai-models/modelos.json), que o instalador de modelos pode baixar da origem,
/// empacotar em <c>.medmodel</c> e instalar.
/// </summary>
public sealed record ModelCatalogEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Source { get; init; }
    public required ModelCapability Capability { get; init; }
    public required string Purpose { get; init; }

    /// <summary>Recursos do editor que o modelo habilita (pt-BR).</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Nome do arquivo do modelo (.onnx ou .bin).</summary>
    public required string File { get; init; }

    public required string License { get; init; }

    /// <summary>Arquivo da licença em <c>licencas\</c> do catálogo.</summary>
    public required string LicenseFile { get; init; }

    public int MemoryMb { get; init; }
    public bool Gpu { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>Modelos que devem ser instalados antes deste (ids do catálogo).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>Endereço HTTPS fixado numa revisão da origem oficial.</summary>
    public required Uri Url { get; init; }

    /// <summary>SHA-256 esperado do arquivo baixado (minúsculas).</summary>
    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }
}

/// <summary>Etapa da instalação a partir do catálogo.</summary>
public enum ModelInstallStage
{
    Downloading,
    Verifying,
    Packaging,
    Installing,
    Done,
}

/// <summary>Progresso de um modelo durante a instalação.</summary>
/// <param name="Index">Posição do modelo na ordem de instalação (1 = primeiro).</param>
public sealed record ModelInstallProgress(string Id, string Name, int Index, int Count, ModelInstallStage Stage, long Bytes, long TotalBytes);

/// <summary>
/// Baixa um arquivo de modelo de uma origem permitida e confere tamanho e SHA-256 antes de entregá-lo.
/// É a única parte do aplicativo que acessa a rede (decisão de 10/11/2026): só roda quando o usuário pede a instalação,
/// só busca os endereços fixos do catálogo e não envia nenhum dado do usuário.
/// </summary>
public interface IModelDownloader
{
    /// <summary>
    /// Baixa <paramref name="url"/> para <paramref name="destination"/> (retomando um <c>.part</c> anterior quando possível).
    /// Lança <see cref="InvalidDataException"/> se o tamanho ou o hash não conferirem (o arquivo parcial é apagado).
    /// </summary>
    Task DownloadAsync(Uri url, string destination, long expectedBytes, string sha256, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);
}
