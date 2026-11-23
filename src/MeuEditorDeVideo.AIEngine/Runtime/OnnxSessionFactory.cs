using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace MeuEditorDeVideo.AIEngine.Runtime;

/// <summary>Sessão ONNX aberta e o dispositivo efetivamente usado.</summary>
public sealed record OnnxModelSession(InferenceSession Session, AiDevice Device, string ModelId) : IDisposable
{
    public void Dispose() => Session.Dispose();
}

/// <summary>
/// Cria sessões do ONNX Runtime (§8.1) no dispositivo escolhido (RF167): GPU quando o modelo declara compatibilidade,
/// o sistema tem provedor de GPU (DirectML no Windows, Core ML no macOS; <see cref="AiPlatform"/>) e a GPU funciona; caso
/// contrário, CPU (no Linux, sempre CPU). Nenhuma sessão acessa a rede.
/// </summary>
public sealed class OnnxSessionFactory(ISettingsService? settings = null, ILogger<OnnxSessionFactory>? logger = null)
{
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>Dispositivo pedido (configurações). Sem serviço de configurações: CPU.</summary>
    public AiDevice Requested => settings?.Current.AiDevice ?? AiDevice.Cpu;

    /// <summary>Dispositivo pedido já ajustado ao sistema: GPU vira CPU onde não há provedor de GPU (Linux, macOS Intel).</summary>
    public AiDevice Effective => AiPlatform.EffectiveDevice(Requested);

    public OnnxModelSession Create(InstalledModel model) =>
        Create(model.ModelPath, model.Manifest.Id, model.Manifest.Gpu && Effective == AiDevice.Gpu);

    /// <summary>Abre um arquivo .onnx diretamente na CPU (testes e ferramentas).</summary>
    public static OnnxModelSession CreateCpu(string path)
    {
        using var options = CpuOptions();
        return new OnnxModelSession(new InferenceSession(path, options), AiDevice.Cpu, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Mensagem quando a biblioteca nativa do ONNX Runtime não carrega.</summary>
    public const string RuntimeMissing = "O ONNX Runtime não está disponível neste sistema; os recursos de IA que dependem dele não funcionam aqui.";

    private OnnxModelSession Create(string path, string id, bool gpu)
    {
        var provider = AiPlatform.Choose(AiPlatform.CurrentOs, RuntimeInformation.ProcessArchitecture, gpu ? AiDevice.Gpu : AiDevice.Cpu, gpu);
        if (provider != AiProvider.Cpu)
        {
            try
            {
                using var options = new SessionOptions
                {
                    EnableMemoryPattern = provider != AiProvider.DirectMl, // desligado: exigido pelo DirectML
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };
                AppendGpuProvider(options, provider);
                var session = new InferenceSession(path, options);
                _log.LogInformation("Modelo {Id} carregado na GPU ({Provider}).", id, AiPlatform.Name(provider));
                return new OnnxModelSession(session, AiDevice.Gpu, id);
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException
                                           or TypeInitializationException)
            {
                _log.LogWarning(ex, "GPU indisponível para o modelo {Id}; usando CPU.", id);
            }
        }
        InferenceSession s;
        try
        {
            using var cpu = CpuOptions();
            s = new InferenceSession(path, cpu);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            // A biblioteca nativa não carrega (o erro chega embrulhado em TypeInitializationException): erro conhecido.
            _log.LogError(ex, "ONNX Runtime indisponível para o modelo {Id}.", id);
            throw new InvalidOperationException(RuntimeMissing, ex);
        }
        _log.LogInformation("Modelo {Id} carregado na CPU.", id);
        return new OnnxModelSession(s, AiDevice.Cpu, id);
    }

    /// <summary>
    /// Registra o provedor de GPU. DirectML: dispositivo 0. Core ML: formato ML Program e todas as unidades de computação
    /// (CPU, GPU e Neural Engine); passado por nome, já que a API tipada do Core ML não existe em todos os pacotes.
    /// </summary>
    private static void AppendGpuProvider(SessionOptions options, AiProvider provider)
    {
        switch (provider)
        {
            case AiProvider.DirectMl:
                options.AppendExecutionProvider_DML(0);
                break;
            case AiProvider.CoreMl:
                options.AppendExecutionProvider("CoreML", new Dictionary<string, string>
                {
                    ["ModelFormat"] = "MLProgram",
                    ["MLComputeUnits"] = "ALL",
                });
                break;
        }
    }

    private static SessionOptions CpuOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
    };
}
