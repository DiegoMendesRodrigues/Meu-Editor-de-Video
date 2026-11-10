using MeuEditorDeVideo.Application.Performance;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Adaptação de desempenho ao hardware (§12.3, RNF008, RNF009, RNF013).</summary>
public sealed class PerformanceAdvisorTests
{
    private const long GiB = 1L << 30;

    private static HardwareInfo Machine(double ramGb, double vramGb, int threads, bool encoders = true, bool decoding = true,
        double freeDiskGb = 500) => new()
    {
        CpuName = "CPU de teste",
        LogicalProcessors = threads,
        TotalMemoryBytes = (long)(ramGb * GiB),
        AvailableMemoryBytes = (long)(ramGb * GiB / 2),
        Gpus = vramGb > 0 ? [new GpuInfo("GPU de teste", (long)(vramGb * GiB), true)] : [new GpuInfo("Integrada", 0, false)],
        Disks = [new DiskInfo("Cache", @"C:\", (long)(freeDiskGb * GiB), 1000 * GiB)],
        HardwareEncoders = encoders ? ["NVIDIA NVENC (GPU): H.264"] : [],
        HardwareDecoding = decoding,
    };

    [Fact]
    public void Configuracao_recomendada_e_alta_com_previa_completa_em_full_hd()
    {
        var p = PerformanceAdvisor.Evaluate(Machine(63.9, 8, 24));
        Assert.Equal(PerformanceTier.High, p.Tier);
        Assert.Equal(PreviewQualityMode.Full, p.Preview);
        Assert.Equal(1024, p.PreviewMemoryMegabytes);
        Assert.False(p.RecommendProxies);
        Assert.Empty(p.Limitations);
    }

    [Fact]
    public void Configuracao_minima_e_intermediaria_com_meia_resolucao()
    {
        // 16 GB nominais aparecem como ~15,8 GB no Windows.
        var p = PerformanceAdvisor.Evaluate(Machine(15.8, 4, 8));
        Assert.Equal(PerformanceTier.Standard, p.Tier);
        Assert.Equal(PreviewQualityMode.Half, p.Preview);
        Assert.Equal(512, p.PreviewMemoryMegabytes);
        Assert.True(p.RecommendProxies);
    }

    [Fact]
    public void Abaixo_do_minimo_e_basico_e_indica_as_limitacoes()
    {
        var p = PerformanceAdvisor.Evaluate(Machine(8, 0, 4, encoders: false, decoding: false, freeDiskGb: 5));
        Assert.Equal(PerformanceTier.Basic, p.Tier);
        Assert.Equal(PreviewQualityMode.Quarter, p.Preview);
        Assert.Contains(p.Limitations, l => l.Contains("Memória de 8,0 GB"));
        Assert.Contains(p.Limitations, l => l.Contains("Sem placa de vídeo dedicada"));
        Assert.Contains(p.Limitations, l => l.Contains("4 núcleos lógicos"));
        Assert.Contains(p.Limitations, l => l.Contains("codificador de vídeo por hardware"));
        Assert.Contains(p.Limitations, l => l.Contains("decodificação de vídeo por hardware"));
        Assert.Contains(p.Limitations, l => l.Contains("Pouco espaço livre"));
    }

    [Fact]
    public void Memoria_para_quadros_nao_passa_de_10_por_cento_da_ram()
    {
        var p = PerformanceAdvisor.Evaluate(Machine(2, 0, 2));
        Assert.Equal(204, p.PreviewMemoryMegabytes);
    }

    [Fact]
    public void Projeto_4k_reduz_a_previa_e_recomenda_proxies()
    {
        var high = PerformanceAdvisor.Evaluate(Machine(64, 8, 24), 3840, 2160);
        Assert.Equal(PreviewQualityMode.Half, high.Preview);
        Assert.True(high.RecommendProxies);

        var standard = PerformanceAdvisor.Evaluate(Machine(16, 4, 8), 3840, 2160);
        Assert.Equal(PreviewQualityMode.Quarter, standard.Preview);
        Assert.Contains(standard.Limitations, l => l.Contains("Projeto 4K"));
    }

    [Fact]
    public void Pouca_memoria_de_video_e_indicada()
    {
        var p = PerformanceAdvisor.Evaluate(Machine(32, 2, 16));
        Assert.Equal(PerformanceTier.Basic, p.Tier);
        Assert.Contains(p.Limitations, l => l.Contains("Memória de vídeo de 2,0 GB"));
    }

    [Theory]
    [InlineData(PreviewQualityMode.Auto, PreviewQualityMode.Half)]
    [InlineData(PreviewQualityMode.Full, PreviewQualityMode.Full)]
    [InlineData(PreviewQualityMode.Quarter, PreviewQualityMode.Quarter)]
    public void Escolha_do_usuario_prevalece_sobre_a_automatica(PreviewQualityMode setting, PreviewQualityMode expected)
    {
        var p = PerformanceAdvisor.Evaluate(Machine(16, 4, 8));
        Assert.Equal(expected, PerformanceAdvisor.ResolvePreview(setting, p));
    }

    [Theory]
    [InlineData(0, 512)]
    [InlineData(2048, 2048)]
    [InlineData(10, 64)]
    [InlineData(100000, 16384)]
    public void Memoria_escolhida_e_limitada(int setting, int expected)
    {
        var p = PerformanceAdvisor.Evaluate(Machine(16, 4, 8));
        Assert.Equal(expected, PerformanceAdvisor.ResolvePreviewMemory(setting, p));
    }

    [Fact]
    public void Gb_formata_em_pt_br()
    {
        Assert.Equal("7,5 GB", PerformanceAdvisor.Gb((long)(7.5 * GiB)));
        Assert.Equal("64 GB", PerformanceAdvisor.Gb(64 * GiB));
    }
}
