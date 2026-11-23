using System.Runtime.InteropServices;

namespace MeuEditorDeVideo.Core.AI;

/// <summary>Sistema operacional para a escolha do provedor de execução dos modelos.</summary>
public enum AiOs
{
    Windows,
    Linux,
    MacOs,
}

/// <summary>Provedor de execução do ONNX Runtime (e do ONNX Runtime GenAI).</summary>
public enum AiProvider
{
    Cpu,

    /// <summary>DirectML (Windows).</summary>
    DirectMl,

    /// <summary>Core ML (macOS, Apple Silicon).</summary>
    CoreMl,
}

/// <summary>
/// Regras puras, por sistema operacional, de qual provedor executa os modelos de IA (RF167): Windows usa DirectML; macOS
/// (Apple Silicon) usa Core ML; Linux usa só a CPU (o pacote padrão do ONNX Runtime não traz provedor de GPU). No macOS
/// Intel não há ONNX Runtime: só o Whisper (Whisper.net) funciona. Sem estado, para poder ser testado em qualquer sistema.
/// </summary>
public static class AiPlatform
{
    /// <summary>Sistema em que o programa está rodando.</summary>
    public static AiOs CurrentOs =>
        OperatingSystem.IsWindows() ? AiOs.Windows : OperatingSystem.IsMacOS() ? AiOs.MacOs : AiOs.Linux;

    /// <summary>Valor de <c>plataformas</c> do catálogo de modelos para o sistema.</summary>
    public static string Tag(AiOs os) => os switch
    {
        AiOs.Windows => "windows",
        AiOs.MacOs => "macos",
        _ => "linux",
    };

    /// <summary>
    /// O sistema tem ONNX Runtime? O ONNX Runtime 1.24.4 e o GenAI 0.14.1 não trazem biblioteca nativa para macOS Intel
    /// (osx-x64); ali os modelos ONNX não carregam.
    /// </summary>
    public static bool OnnxRuntimeAvailableOn(AiOs os, Architecture arch) => !(os == AiOs.MacOs && arch == Architecture.X64);

    /// <summary><see cref="OnnxRuntimeAvailableOn"/> no sistema atual.</summary>
    public static bool OnnxRuntimeAvailable => OnnxRuntimeAvailableOn(CurrentOs, RuntimeInformation.ProcessArchitecture);

    /// <summary>O modelo da capacidade roda no ONNX Runtime? Todos, menos o reconhecimento de fala (Whisper.net).</summary>
    public static bool NeedsOnnxRuntime(ModelCapability capability) => capability != ModelCapability.SpeechRecognition;

    /// <summary>A capacidade pode funcionar num sistema com (ou sem) ONNX Runtime?</summary>
    public static bool Supports(ModelCapability capability, bool onnxRuntime) => onnxRuntime || !NeedsOnnxRuntime(capability);

    /// <summary><see cref="Supports(ModelCapability, bool)"/> no sistema atual.</summary>
    public static bool Supports(ModelCapability capability) => Supports(capability, OnnxRuntimeAvailable);

    /// <summary>Provedor de GPU disponível no sistema, ou <see cref="AiProvider.Cpu"/> se não houver.</summary>
    public static AiProvider GpuProvider(AiOs os, Architecture arch) => os switch
    {
        AiOs.Windows => AiProvider.DirectMl,
        AiOs.MacOs when arch == Architecture.Arm64 => AiProvider.CoreMl, // o ONNX Runtime não tem nativo para macOS Intel
        _ => AiProvider.Cpu,
    };

    /// <summary>
    /// Provedor para uma sessão: o da GPU só quando o usuário escolheu a GPU, o modelo é compatível e o sistema tem
    /// provedor de GPU; senão, CPU.
    /// </summary>
    public static AiProvider Choose(AiOs os, Architecture arch, AiDevice requested, bool modelAllowsGpu) =>
        requested == AiDevice.Gpu && modelAllowsGpu ? GpuProvider(os, arch) : AiProvider.Cpu;

    /// <summary>Dispositivo que de fato será usado para o que o usuário pediu (GPU vira CPU onde não há provedor de GPU).</summary>
    public static AiDevice EffectiveDevice(AiOs os, Architecture arch, AiDevice requested) =>
        requested == AiDevice.Gpu && GpuProvider(os, arch) != AiProvider.Cpu ? AiDevice.Gpu : AiDevice.Cpu;

    /// <summary>Há GPU utilizável pelos modelos de IA neste sistema?</summary>
    public static bool GpuAvailable => GpuProvider(CurrentOs, RuntimeInformation.ProcessArchitecture) != AiProvider.Cpu;

    /// <summary>Dispositivo efetivo no sistema atual.</summary>
    public static AiDevice EffectiveDevice(AiDevice requested) => EffectiveDevice(CurrentOs, RuntimeInformation.ProcessArchitecture, requested);

    /// <summary>Nome do provedor, para mensagens.</summary>
    public static string Name(AiProvider provider) => provider switch
    {
        AiProvider.DirectMl => "DirectML",
        AiProvider.CoreMl => "Core ML",
        _ => "CPU",
    };

    /// <summary>Nome da GPU nos textos: "GPU (DirectML)", "GPU (Core ML)" ou "GPU" quando o sistema não tem provedor.</summary>
    public static string GpuLabel(AiOs os, Architecture arch) =>
        GpuProvider(os, arch) is var p && p != AiProvider.Cpu ? $"GPU ({Name(p)})" : "GPU";

    /// <summary><see cref="GpuLabel(AiOs, Architecture)"/> no sistema atual.</summary>
    public static string GpuLabel() => GpuLabel(CurrentOs, RuntimeInformation.ProcessArchitecture);
}
