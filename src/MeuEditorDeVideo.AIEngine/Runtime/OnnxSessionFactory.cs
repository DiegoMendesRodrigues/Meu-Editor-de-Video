using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace MeuEditorDeVideo.AIEngine.Runtime;

/// <summary>Sessão ONNX aberta e o dispositivo efetivamente usado.</summary>
public sealed record OnnxModelSession(InferenceSession Session, AiDevice Device, string ModelId) : IDisposable
{
    public void Dispose() => Session.Dispose();
}

/// <summary>
/// Cria sessões do ONNX Runtime (§8.1) no dispositivo escolhido (RF167): GPU via DirectML quando o modelo declara
/// compatibilidade e a GPU funciona; caso contrário, CPU. Nenhuma sessão acessa a rede.
/// </summary>
public sealed class OnnxSessionFactory(ISettingsService? settings = null, ILogger<OnnxSessionFactory>? logger = null)
{
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Dispositivo pedido (configurações). Sem serviço de configurações: CPU.</summary>
    public AiDevice Requested => settings?.Current.AiDevice ?? AiDevice.Cpu;

    public OnnxModelSession Create(InstalledModel model) =>
        Create(model.ModelPath, model.Manifest.Id, model.Manifest.Gpu && Requested == AiDevice.Gpu);

    /// <summary>Abre um arquivo .onnx diretamente na CPU (testes e ferramentas).</summary>
    public static OnnxModelSession CreateCpu(string path)
    {
        using var options = CpuOptions();
        return new OnnxModelSession(new InferenceSession(path, options), AiDevice.Cpu, Path.GetFileNameWithoutExtension(path));
    }

    private OnnxModelSession Create(string path, string id, bool gpu)
    {
        if (gpu)
        {
            try
            {
                using var options = new SessionOptions
                {
                    EnableMemoryPattern = false, // exigido pelo DirectML
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };
                options.AppendExecutionProvider_DML(0);
                var session = new InferenceSession(path, options);
                _log.LogInformation("Modelo {Id} carregado na GPU (DirectML).", id);
                return new OnnxModelSession(session, AiDevice.Gpu, id);
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                _log.LogWarning(ex, "GPU indisponível para o modelo {Id}; usando CPU.", id);
            }
        }
        using var cpu = CpuOptions();
        var s = new InferenceSession(path, cpu);
        _log.LogInformation("Modelo {Id} carregado na CPU.", id);
        return new OnnxModelSession(s, AiDevice.Cpu, id);
    }

    private static SessionOptions CpuOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
    };
}
