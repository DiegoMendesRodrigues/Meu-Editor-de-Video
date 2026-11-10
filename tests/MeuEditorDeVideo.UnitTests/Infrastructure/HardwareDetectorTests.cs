using System.IO;
using MeuEditorDeVideo.Infrastructure.Hardware;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

public sealed class HardwareDetectorTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static HardwareDetector Detector(
        IEnumerable<(string Label, string Path)>? folders = null,
        Func<IReadOnlyList<string>>? encoders = null,
        Func<bool>? decoding = null) =>
        new(folders ?? [], encoders, decoding);

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070", 8L * 1024 * 1024 * 1024, true)]
    [InlineData("Intel(R) UHD Graphics 630", 128 * 1024 * 1024, false)]
    [InlineData("Microsoft Basic Render Driver", 0L, false)]
    [InlineData("AMD Radeon RX 6600", 8L * 1024 * 1024 * 1024, true)]
    [InlineData("Parsec Virtual Display Adapter", 2L * 1024 * 1024 * 1024, false)]
    public void Placa_dedicada_e_identificada_pelo_nome_e_pela_memoria(string nome, long memoria, bool esperado)
    {
        Assert.Equal(esperado, HardwareDetector.IsDedicated(nome, memoria));
    }

    [Fact]
    public void Valores_do_registro_sao_convertidos_em_bytes()
    {
        Assert.Equal(8L << 30, HardwareDetector.Number(8L << 30));
        Assert.Equal(4293918720L, HardwareDetector.Number(unchecked((int)0xFFF00000))); // DWORD saturado
        Assert.Equal(2L << 30, HardwareDetector.Number(BitConverter.GetBytes(2L << 30)));
        Assert.Equal(1L << 30, HardwareDetector.Number(BitConverter.GetBytes((uint)(1 << 30))));
        Assert.Equal(0, HardwareDetector.Number("texto"));
        Assert.Equal(0, HardwareDetector.Number(null));
    }

    [Fact]
    public void Deteccao_real_retorna_valores_plausiveis()
    {
        var info = Detector([("Temporários", Path.GetTempPath())]).Get();

        Assert.False(string.IsNullOrWhiteSpace(info.CpuName));
        Assert.True(info.LogicalProcessors > 0);
        Assert.True(info.TotalMemoryBytes > GiB);
        Assert.True(info.AvailableMemoryBytes > 0);
        Assert.True(info.AvailableMemoryBytes <= info.TotalMemoryBytes);
        Assert.NotNull(info.Gpus);
        // Placas físicas (com driver próprio) informam a memória de vídeo no registro.
        foreach (var gpu in info.Gpus.Where(g => !g.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase)
            && !g.Name.Contains("Básic", StringComparison.OrdinalIgnoreCase) && !g.Name.Contains("Remote", StringComparison.OrdinalIgnoreCase)))
            Assert.True(gpu.VideoMemoryBytes > 0, $"memória de vídeo não lida para {gpu.Name}");

        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()));
        Assert.Contains(info.Disks, d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase) && d.FreeBytes > 0);
    }

    [Fact]
    public void Mesmo_disco_aparece_uma_vez_com_os_rotulos_unidos()
    {
        var temp = Path.GetTempPath();
        var info = Detector(
        [
            ("Cache", Path.Combine(temp, "MeuEditorDeVideo-cache")),
            ("Projetos", Path.Combine(temp, "MeuEditorDeVideo-projetos")),
        ]).Get();

        var root = Path.GetPathRoot(Path.GetFullPath(temp));
        var disk = Assert.Single(info.Disks, d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Cache e Projetos", disk.Label);
    }

    [Fact]
    public void Delegados_de_encoders_e_decodificacao_sao_refletidos()
    {
        var info = Detector(encoders: () => ["NVENC"], decoding: () => true).Get();

        Assert.Equal(["NVENC"], info.HardwareEncoders);
        Assert.True(info.HardwareDecoding);
    }

    [Fact]
    public void Delegado_de_encoders_que_lanca_resulta_em_lista_vazia()
    {
        var info = Detector(
            encoders: () => throw new InvalidOperationException("falha simulada"),
            decoding: () => throw new InvalidOperationException("falha simulada")).Get();

        Assert.Empty(info.HardwareEncoders);
        Assert.False(info.HardwareDecoding);
    }

    [Fact]
    public void Get_repetido_devolve_a_mesma_instancia()
    {
        var detector = Detector();

        var primeira = detector.Get();
        var segunda = detector.Get();

        Assert.Same(primeira, segunda);
    }

    [Fact]
    public void Get_com_refresh_devolve_nova_instancia_com_mesma_cpu()
    {
        var detector = Detector();
        var primeira = detector.Get();

        var atualizada = detector.Get(refresh: true);

        Assert.NotSame(primeira, atualizada);
        Assert.Equal(primeira.CpuName, atualizada.CpuName);
    }
}
