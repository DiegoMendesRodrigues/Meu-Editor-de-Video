// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Arquitetura = System.Runtime.InteropServices.Architecture;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.UnitTests.AI;

/// <summary>Escolha do provedor de execução dos modelos por sistema operacional (RF167).</summary>
public sealed class AiPlatformTests
{
    [Theory]
    [InlineData(AiOs.Windows, Arquitetura.X64, AiProvider.DirectMl)]
    [InlineData(AiOs.Windows, Arquitetura.Arm64, AiProvider.DirectMl)]
    [InlineData(AiOs.MacOs, Arquitetura.Arm64, AiProvider.CoreMl)]
    [InlineData(AiOs.MacOs, Arquitetura.X64, AiProvider.Cpu)]
    [InlineData(AiOs.Linux, Arquitetura.X64, AiProvider.Cpu)]
    [InlineData(AiOs.Linux, Arquitetura.Arm64, AiProvider.Cpu)]
    public void Provedor_de_GPU_por_sistema(AiOs os, Arquitetura arch, AiProvider esperado) =>
        Assert.Equal(esperado, AiPlatform.GpuProvider(os, arch));

    [Theory]
    [InlineData(AiOs.Windows, AiDevice.Gpu, true, AiProvider.DirectMl)]
    [InlineData(AiOs.MacOs, AiDevice.Gpu, true, AiProvider.CoreMl)]
    [InlineData(AiOs.Linux, AiDevice.Gpu, true, AiProvider.Cpu)]
    [InlineData(AiOs.Windows, AiDevice.Cpu, true, AiProvider.Cpu)] // o usuário escolheu CPU
    [InlineData(AiOs.MacOs, AiDevice.Gpu, false, AiProvider.Cpu)] // modelo sem suporte a GPU
    [InlineData(AiOs.Windows, AiDevice.Gpu, false, AiProvider.Cpu)]
    public void Escolha_do_provedor_da_sessao(AiOs os, AiDevice pedido, bool modeloAceitaGpu, AiProvider esperado) =>
        Assert.Equal(esperado, AiPlatform.Choose(os, Arquitetura.Arm64, pedido, modeloAceitaGpu));

    [Fact]
    public void No_Linux_a_GPU_pedida_vira_CPU_e_o_sistema_informa_que_nao_ha_GPU()
    {
        Assert.Equal(AiDevice.Cpu, AiPlatform.EffectiveDevice(AiOs.Linux, Arquitetura.X64, AiDevice.Gpu));
        Assert.Equal(AiDevice.Gpu, AiPlatform.EffectiveDevice(AiOs.Windows, Arquitetura.X64, AiDevice.Gpu));
        Assert.Equal(AiDevice.Gpu, AiPlatform.EffectiveDevice(AiOs.MacOs, Arquitetura.Arm64, AiDevice.Gpu));
        Assert.Equal(AiDevice.Cpu, AiPlatform.EffectiveDevice(AiOs.MacOs, Arquitetura.X64, AiDevice.Gpu));
        Assert.Equal(AiDevice.Cpu, AiPlatform.EffectiveDevice(AiOs.Windows, Arquitetura.X64, AiDevice.Cpu));
    }

    [Fact]
    public void Nomes_e_rotulos_por_sistema()
    {
        Assert.Equal("GPU (DirectML)", AiPlatform.GpuLabel(AiOs.Windows, Arquitetura.X64));
        Assert.Equal("GPU (Core ML)", AiPlatform.GpuLabel(AiOs.MacOs, Arquitetura.Arm64));
        Assert.Equal("GPU", AiPlatform.GpuLabel(AiOs.Linux, Arquitetura.X64));
        Assert.Equal("windows", AiPlatform.Tag(AiOs.Windows));
        Assert.Equal("linux", AiPlatform.Tag(AiOs.Linux));
        Assert.Equal("macos", AiPlatform.Tag(AiOs.MacOs));
    }

    [Fact]
    public void Sistema_atual_e_coerente()
    {
        Assert.Equal(AiPlatform.GpuAvailable, AiPlatform.EffectiveDevice(AiDevice.Gpu) == AiDevice.Gpu);
        Assert.Equal(AiDevice.Cpu, AiPlatform.EffectiveDevice(AiDevice.Cpu));
        if (OperatingSystem.IsWindows()) Assert.Equal(AiOs.Windows, AiPlatform.CurrentOs);
    }

    [Theory]
    [InlineData(AiOs.Windows, Arquitetura.X64, true)]
    [InlineData(AiOs.Windows, Arquitetura.Arm64, true)]
    [InlineData(AiOs.Linux, Arquitetura.X64, true)]
    [InlineData(AiOs.MacOs, Arquitetura.Arm64, true)]
    [InlineData(AiOs.MacOs, Arquitetura.X64, false)]
    public void ONNX_Runtime_falta_so_no_macOS_Intel(AiOs os, Arquitetura arch, bool esperado) =>
        Assert.Equal(esperado, AiPlatform.OnnxRuntimeAvailableOn(os, arch));

    [Fact]
    public void Sem_ONNX_Runtime_so_o_reconhecimento_de_fala_funciona()
    {
        foreach (var c in Enum.GetValues<ModelCapability>())
        {
            Assert.True(AiPlatform.Supports(c, onnxRuntime: true), c.ToString());
            Assert.Equal(c == ModelCapability.SpeechRecognition, AiPlatform.Supports(c, onnxRuntime: false));
        }
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) Assert.True(AiPlatform.OnnxRuntimeAvailable);
    }
}
