using System.Globalization;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Application.Performance;

public enum PerformanceTier
{
    /// <summary>Abaixo da configuração mínima do §12.1.</summary>
    Basic,

    /// <summary>Configuração mínima do §12.1 (16 GB, GPU com 4 GB, 8 threads).</summary>
    Standard,

    /// <summary>Configuração recomendada do §12.2 (32 GB, GPU com 8 GB, 12 threads).</summary>
    High,
}

/// <summary>Recomendação de desempenho para o hardware e o projeto (§12.3).</summary>
/// <param name="Preview">Qualidade da pré-visualização recomendada (nunca <see cref="PreviewQualityMode.Auto"/>).</param>
/// <param name="PreviewMemoryMegabytes">Memória para quadros prontos da pré-visualização.</param>
/// <param name="Limitations">Limitações a indicar ao usuário (pt-BR).</param>
public sealed record PerformanceProfile(PerformanceTier Tier, PreviewQualityMode Preview, int PreviewMemoryMegabytes,
    bool RecommendProxies, IReadOnlyList<string> Limitations)
{
    public string Summary => Tier switch
    {
        PerformanceTier.High => "Desempenho alto: computador na configuração recomendada.",
        PerformanceTier.Standard => "Desempenho intermediário: computador na configuração mínima.",
        _ => "Desempenho básico: computador abaixo da configuração mínima; a pré-visualização usa resolução reduzida.",
    };
}

/// <summary>
/// Adaptação de desempenho (§12.3, RNF008, RNF009, RNF013): classifica o computador pelos requisitos do §12 e recomenda a
/// qualidade da pré-visualização, a memória para quadros prontos e o uso de proxies, indicando as limitações.
/// </summary>
public static class PerformanceAdvisor
{
    private const long GiB = 1L << 30;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Espaço livre abaixo do qual o disco é indicado como limitação.</summary>
    public const long LowDiskBytes = 20 * GiB;

    public static PerformanceProfile Evaluate(HardwareInfo hw, int projectWidth = 1920, int projectHeight = 1080)
    {
        var gpu = hw.Gpus.Where(g => g.Dedicated).OrderByDescending(g => g.VideoMemoryBytes).FirstOrDefault();
        var vram = gpu?.VideoMemoryBytes ?? 0;
        // A memória total informada pelo Windows fica um pouco abaixo do valor nominal (16 GB → ~15,8 GB).
        var ramGb = hw.TotalMemoryBytes / (double)GiB;

        var tier = ramGb >= 31 && vram >= 7.5 * GiB && hw.LogicalProcessors >= 12 ? PerformanceTier.High
            : ramGb >= 15 && vram >= 3.5 * GiB && hw.LogicalProcessors >= 8 ? PerformanceTier.Standard
            : PerformanceTier.Basic;

        var pixels = (long)Math.Max(1, projectWidth) * Math.Max(1, projectHeight);
        const long Hd = 1920L * 1080, Qhd = 2560L * 1440, Sd = 1280L * 720;
        var preview = tier switch
        {
            PerformanceTier.High => pixels <= Qhd ? PreviewQualityMode.Full : PreviewQualityMode.Half,
            PerformanceTier.Standard => pixels <= Hd ? PreviewQualityMode.Half : PreviewQualityMode.Quarter,
            _ => pixels <= Sd ? PreviewQualityMode.Half : PreviewQualityMode.Quarter,
        };

        var memory = tier switch { PerformanceTier.High => 1024, PerformanceTier.Standard => 512, _ => 256 };
        if (hw.TotalMemoryBytes > 0) memory = (int)Math.Clamp(Math.Min(memory, hw.TotalMemoryBytes / 10 / (1 << 20)), 128, memory);

        var isUhd = pixels > Qhd;
        var limitations = new List<string>();
        if (ramGb > 0 && ramGb < 15)
            limitations.Add($"Memória de {Gb(hw.TotalMemoryBytes)} (mínimo recomendado: 16 GB): projetos longos ou 4K podem ficar lentos, e a pré-visualização guarda menos quadros.");
        if (gpu is null)
            limitations.Add("Sem placa de vídeo dedicada: os efeitos e a IA ficam mais lentos e a pré-visualização usa resolução reduzida.");
        else if (vram < 3.5 * GiB)
            limitations.Add($"Memória de vídeo de {Gb(vram)} em {gpu.Name} (mínimo recomendado: 4 GB): modelos de IA podem rodar na CPU.");
        if (hw.LogicalProcessors is > 0 and < 8)
            limitations.Add($"Processador com {hw.LogicalProcessors} núcleos lógicos: exportação, transcrição e análises ficam mais lentas.");
        if (hw.HardwareEncoders.Count == 0)
            limitations.Add("Nenhum codificador de vídeo por hardware: a exportação usa o processador e é mais lenta.");
        if (!hw.HardwareDecoding)
            limitations.Add("Sem decodificação de vídeo por hardware: use proxies para vídeos de alta resolução.");
        foreach (var disk in hw.Disks.Where(d => d.TotalBytes > 0 && d.FreeBytes < LowDiskBytes))
            limitations.Add($"Pouco espaço livre em {disk.Label} ({disk.Root}): {Gb(disk.FreeBytes)}. Proxies, cache e exportações precisam de espaço.");
        if (isUhd && tier != PerformanceTier.High)
            limitations.Add("Projeto 4K neste computador: a pré-visualização usa resolução reduzida e os proxies são recomendados.");

        return new PerformanceProfile(tier, preview, memory, tier != PerformanceTier.High || isUhd, limitations);
    }

    /// <summary>Qualidade efetiva: a escolhida pelo usuário ou, em Automática, a recomendada.</summary>
    public static PreviewQualityMode ResolvePreview(PreviewQualityMode setting, PerformanceProfile profile) =>
        setting == PreviewQualityMode.Auto ? profile.Preview : setting;

    /// <summary>Memória efetiva para quadros prontos: a escolhida pelo usuário ou, em 0 (automática), a recomendada.</summary>
    public static int ResolvePreviewMemory(int settingMegabytes, PerformanceProfile profile) =>
        settingMegabytes > 0 ? Math.Clamp(settingMegabytes, 64, 16384) : profile.PreviewMemoryMegabytes;

    /// <summary>Quantidade formatada em GB (ex.: "15,8 GB").</summary>
    public static string Gb(long bytes) => (bytes / (double)GiB).ToString(bytes >= 10 * GiB ? "0" : "0.0", Pt) + " GB";
}
