namespace MeuEditorDeVideo.Core.Abstractions;

/// <summary>Placa de vídeo detectada (§12.3).</summary>
/// <param name="Name">Nome comercial (ex.: "NVIDIA GeForce RTX 3070").</param>
/// <param name="VideoMemoryBytes">Memória de vídeo dedicada; 0 quando desconhecida.</param>
/// <param name="Dedicated">Placa dedicada (memória própria ≥ 1 GB e não é adaptador de software/básico).</param>
public sealed record GpuInfo(string Name, long VideoMemoryBytes, bool Dedicated);

/// <summary>Disco de uma pasta usada pelo aplicativo (cache, projetos).</summary>
/// <param name="Label">Uso da pasta (ex.: "Cache", "Projetos").</param>
/// <param name="Root">Raiz da unidade (ex.: "C:\").</param>
public sealed record DiskInfo(string Label, string Root, long FreeBytes, long TotalBytes);

/// <summary>Hardware do computador (§12.3): CPU, memória, GPU, memória de vídeo, espaço e aceleração.</summary>
public sealed record HardwareInfo
{
    public string CpuName { get; init; } = "";
    public int LogicalProcessors { get; init; }
    public long TotalMemoryBytes { get; init; }
    public long AvailableMemoryBytes { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];

    /// <summary>Codificadores de vídeo por hardware disponíveis (ex.: "NVENC", "AMF", "Quick Sync", "Media Foundation").</summary>
    public IReadOnlyList<string> HardwareEncoders { get; init; } = [];

    /// <summary>Decodificação de vídeo por hardware (D3D11VA) disponível.</summary>
    public bool HardwareDecoding { get; init; }

    /// <summary>A placa dedicada com mais memória de vídeo, ou null.</summary>
    public GpuInfo? BestGpu => Gpus.Where(g => g.Dedicated).OrderByDescending(g => g.VideoMemoryBytes).FirstOrDefault()
        ?? Gpus.OrderByDescending(g => g.VideoMemoryBytes).FirstOrDefault();
}

public interface IHardwareInfoService
{
    /// <summary>Detecta o hardware (resultado guardado; <paramref name="refresh"/> força nova leitura de memória livre e discos).</summary>
    HardwareInfo Get(bool refresh = false);
}

/// <summary>Uso do cache em disco (RF209).</summary>
/// <param name="Area">Parte do cache (ex.: "Miniaturas, formas de onda e proxies", "Máscaras de IA").</param>
public sealed record CacheUsage(string Area, string Directory, long Bytes, int Entries);

/// <summary>Limpeza e limite do cache em disco (RF209, RNF013).</summary>
public interface ICacheMaintenance
{
    IReadOnlyList<CacheUsage> Measure();

    /// <summary>Apaga as entradas usadas há mais tempo até o total ficar abaixo de <paramref name="limitBytes"/>. Devolve os bytes liberados.</summary>
    long Trim(long limitBytes, CancellationToken cancellationToken = default);

    /// <summary>Apaga todo o cache (arquivos em uso são mantidos). Devolve os bytes liberados.</summary>
    long Clear(CancellationToken cancellationToken = default);
}
