// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Infrastructure.Hardware;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

public sealed class HardwareParsingTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void Meminfo_do_Linux_traz_total_e_disponivel()
    {
        const string texto = "MemTotal:       16318348 kB\nMemFree:          812340 kB\nMemAvailable:   10234567 kB\nBuffers:          123 kB\n";

        var (total, disponivel) = HardwareParsing.ParseMeminfo(texto);

        Assert.Equal(16318348L * 1024, total);
        Assert.Equal(10234567L * 1024, disponivel);
    }

    [Fact]
    public void Meminfo_sem_MemAvailable_usa_MemFree_e_texto_invalido_da_zero()
    {
        Assert.Equal((4096L * 1024, 1024L * 1024), HardwareParsing.ParseMeminfo("MemTotal: 4096 kB\nMemFree: 1024 kB"));
        Assert.Equal((0L, 0L), HardwareParsing.ParseMeminfo("lixo\n: x"));
    }

    [Fact]
    public void Cpuinfo_do_Linux_traz_o_primeiro_model_name()
    {
        const string x86 = "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: Intel(R) Core(TM) i7-9700K   CPU @ 3.60GHz\ncpu MHz\t\t: 3600\nprocessor\t: 1\nmodel name\t: Outro\n";
        const string arm = "processor\t: 0\nBogoMIPS\t: 108.00\nHardware\t: BCM2711\nModel\t\t: Raspberry Pi 4\n";

        Assert.Equal("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", HardwareParsing.ParseCpuInfoModelName(x86));
        Assert.Equal("BCM2711", HardwareParsing.ParseCpuInfoModelName(arm));
        Assert.Equal("", HardwareParsing.ParseCpuInfoModelName("nada"));
    }

    [Fact]
    public void Numeros_do_sysctl_e_do_sysfs_sao_interpretados()
    {
        Assert.Equal(17179869184L, HardwareParsing.ParseNumber("17179869184\n"));
        Assert.Equal(8573157376L, HardwareParsing.ParseNumber("8573157376"));
        Assert.Equal(0, HardwareParsing.ParseNumber("erro"));
    }

    [Fact]
    public void Vm_stat_do_macOS_soma_livre_inativa_e_especulativa()
    {
        const string texto = "Mach Virtual Memory Statistics: (page size of 16384 bytes)\n" +
            "Pages free:                               1000.\n" +
            "Pages active:                           50000.\n" +
            "Pages inactive:                           2000.\n" +
            "Pages speculative:                         500.\n" +
            "Pages wired down:                         9000.\n";

        Assert.Equal(3500L * 16384, HardwareParsing.ParseVmStatAvailable(texto));
        Assert.Equal(0, HardwareParsing.ParseVmStatAvailable("nada"));
    }

    [Theory]
    [InlineData("0x10de\n", "NVIDIA")]
    [InlineData("0x1002", "AMD")]
    [InlineData("0x8086", "Intel")]
    [InlineData("0xffff", null)]
    public void Fabricante_pelo_id_PCI(string id, string? esperado) =>
        Assert.Equal(esperado, HardwareParsing.PciVendorName(id));

    [Fact]
    public void Nome_da_placa_Linux_inclui_fabricante_e_dispositivo()
    {
        Assert.Equal("NVIDIA GPU [0x2484]", HardwareParsing.GpuNameFromPci("0x10de\n", "0x2484\n"));
        Assert.Equal("Fabricante 0xabcd GPU", HardwareParsing.GpuNameFromPci("0xabcd", ""));
    }

    [Fact]
    public void System_profiler_com_VRAM_dedicada()
    {
        const string json = """
            {"SPDisplaysDataType":[{"_name":"AMD Radeon Pro 5500M","sppci_model":"AMD Radeon Pro 5500M","spdisplays_vram":"8 GB"},
              {"_name":"Intel UHD Graphics 630","sppci_model":"Intel UHD Graphics 630","spdisplays_vram_shared":"1536 MB"}]}
            """;

        var gpus = HardwareParsing.ParseSystemProfilerDisplays(json, 16 * GiB);

        Assert.Equal(2, gpus.Count);
        Assert.Equal("AMD Radeon Pro 5500M", gpus[0].Name);
        Assert.Equal(8 * GiB, gpus[0].VideoMemoryBytes);
        Assert.True(gpus[0].Dedicated);
        Assert.Equal(1536L * 1024 * 1024, gpus[1].VideoMemoryBytes);
    }

    [Fact]
    public void System_profiler_em_Apple_Silicon_usa_a_memoria_unificada()
    {
        const string json = """{"SPDisplaysDataType":[{"_name":"Apple M2 Pro","sppci_model":"Apple M2 Pro","spdisplays_mtlgpufamilysupport":"spdisplays_metal3"}]}""";

        var gpus = HardwareParsing.ParseSystemProfilerDisplays(json, 32 * GiB);

        Assert.Equal("Apple M2 Pro", Assert.Single(gpus).Name);
        Assert.Equal(32 * GiB, gpus[0].VideoMemoryBytes);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("{}")]
    [InlineData("""{"SPDisplaysDataType":5}""")]
    public void System_profiler_invalido_nao_lanca_e_nao_traz_placas(string json) =>
        Assert.Empty(HardwareParsing.ParseSystemProfilerDisplays(json, GiB));

    [Theory]
    [InlineData("8 GB", 8L << 30)]
    [InlineData("1536 MB", 1536L << 20)]
    [InlineData("desconhecido", 0L)]
    [InlineData(null, 0L)]
    public void Tamanhos_em_texto_viram_bytes(string? texto, long esperado) =>
        Assert.Equal(esperado, HardwareParsing.ParseSize(texto));

    [Fact]
    public void Memoria_do_sistema_atual_e_plausivel()
    {
        var (total, _) = SystemMemory.Read();

        Assert.True(total > 0);
        Assert.True(SystemMemory.AvailableBytes() >= 0);
    }
}
