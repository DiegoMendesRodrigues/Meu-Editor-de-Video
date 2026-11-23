using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Help;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace MeuEditorDeVideo.AIEngine.Assistant;

/// <summary>
/// Modelo de linguagem local do assistente virtual (capacidade <see cref="ModelCapability.HelpAssistant"/>, ex.:
/// Phi-4-mini-instruct ONNX int4) executado pelo ONNX Runtime GenAI: GPU via DirectML (só no Windows) quando escolhida
/// nas configurações (com retorno para CPU; no Linux e no macOS o ONNX Runtime GenAI roda na CPU) e decodificação gulosa, para respostas estáveis. O modelo é carregado na primeira pergunta
/// (alguns segundos) e fica na memória até <see cref="Unload"/>, até o modelo instalado mudar ou o aplicativo fechar.
/// Uma resposta por vez. Nada sai do computador.
/// </summary>
public sealed class GenAiAnswerGenerator : IHelpAnswerGenerator, IDisposable
{
    /// <summary>Janela de contexto usada (pergunta + resposta); bem abaixo do máximo do modelo, para caber na memória.</summary>
    public const int ContextTokens = 4096;

    private readonly IModelRegistry _registry;
    private readonly OnnxSessionFactory? _sessions;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _run = new(1, 1);
    private OgaHandle? _handle;
    private Loaded? _loaded;

    public GenAiAnswerGenerator(IModelRegistry registry, OnnxSessionFactory? sessions = null, ILogger<GenAiAnswerGenerator>? logger = null)
    {
        _registry = registry;
        _sessions = sessions;
        _log = (ILogger?)logger ?? NullLogger.Instance;
        registry.Changed += (_, _) =>
        {
            // Modelo removido ou trocado: a próxima pergunta recarrega.
            var model = registry.Find(ModelCapability.HelpAssistant);
            string? loadedDirectory;
            lock (_gate) loadedDirectory = _loaded?.Directory;
            if (loadedDirectory is not null && model?.Directory != loadedDirectory) Unload();
        };
    }

    public bool IsAvailable => _registry.IsAvailable(ModelCapability.HelpAssistant);

    /// <summary>Dispositivo do modelo carregado (null se não carregado).</summary>
    public AiDevice? Device
    {
        get
        {
            lock (_gate) return _loaded?.Device;
        }
    }

    public async IAsyncEnumerable<string> GenerateAsync(IReadOnlyList<HelpChatMessage> messages, int maxTokens = 512,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        await _run.WaitAsync(cancellationToken).ConfigureAwait(false);
        var worker = Task.Run(() =>
        {
            try
            {
                Generate(messages, maxTokens, channel.Writer, cancellationToken);
                channel.Writer.TryComplete();
            }
            catch (OnnxRuntimeGenAIException ex)
            {
                // Quem chama não depende dos tipos do GenAI.
                _log.LogError(ex, "Falha do modelo de linguagem do assistente virtual.");
                channel.Writer.TryComplete(new InvalidOperationException(ex.Message, ex));
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
            finally
            {
                _run.Release();
            }
        }, CancellationToken.None);

        await foreach (var piece in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            yield return piece;
        await worker.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Generate(IReadOnlyList<HelpChatMessage> messages, int maxTokens, ChannelWriter<string> output, CancellationToken ct)
    {
        var loaded = EnsureLoaded();
        var prompt = loaded.IsPhi ? PhiPrompt(messages) : Prompt(loaded.Tokenizer, messages);
        using var sequences = loaded.Tokenizer.Encode(prompt);
        var ids = sequences[0];
        if (ids.Length + 16 > ContextTokens)
            throw new InvalidOperationException("A pergunta e os trechos do manual passaram do limite do modelo de linguagem.");
        var max = Math.Min(ContextTokens, ids.Length + Math.Max(16, maxTokens));

        using var parameters = new GeneratorParams(loaded.Model);
        parameters.SetSearchOption("max_length", max);
        parameters.SetSearchOption("do_sample", false);
        using var generator = new Generator(loaded.Model, parameters);
        generator.AppendTokens(ids);
        using var stream = loaded.Tokenizer.CreateStream();
        while (!generator.IsDone())
        {
            if (ct.IsCancellationRequested) return; // parar: a resposta fica como está
            generator.GenerateNextToken();
            var piece = stream.Decode(generator.GetNextTokens()[0]);
            if (piece.Length > 0) output.TryWrite(piece);
        }
    }

    /// <summary>Texto da conversa no formato do modelo (modelo de conversa do tokenizador; Phi como reserva).</summary>
    private string Prompt(Tokenizer tokenizer, IReadOnlyList<HelpChatMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages.Select(m => new { role = Role(m.Role), content = m.Content }));
        try
        {
            return tokenizer.ApplyChatTemplate(null!, json, null!, true);
        }
        catch (Exception ex) when (ex is OnnxRuntimeGenAIException or ArgumentException or NullReferenceException)
        {
            _log.LogDebug(ex, "Modelo de conversa do tokenizador indisponível; usando o formato do Phi.");
            return PhiPrompt(messages);
        }
    }

    /// <summary>Formato de conversa do Phi-4-mini: <c>&lt;|system|&gt;…&lt;|end|&gt;&lt;|user|&gt;…&lt;|end|&gt;&lt;|assistant|&gt;</c>.</summary>
    public static string PhiPrompt(IReadOnlyList<HelpChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages) sb.Append("<|").Append(Role(m.Role)).Append("|>").Append(m.Content).Append("<|end|>");
        return sb.Append("<|assistant|>").ToString();
    }

    private static string Role(HelpChatRole role) => role switch
    {
        HelpChatRole.System => "system",
        HelpChatRole.Assistant => "assistant",
        _ => "user",
    };

    /// <summary>O ONNX Runtime GenAI só tem provedor de GPU (DirectML) no Windows; no Linux e no macOS roda na CPU.</summary>
    private static bool GpuSupported => OperatingSystem.IsWindows();

    private Loaded EnsureLoaded()
    {
        var model = (IsAvailable ? _registry.Find(ModelCapability.HelpAssistant) : null)
            ?? throw new InvalidOperationException("O modelo de linguagem do assistente virtual não está instalado.");
        var gpu = model.Manifest.Gpu && GpuSupported && (_sessions?.Requested ?? AiDevice.Gpu) == AiDevice.Gpu;
        lock (_gate)
        {
            if (_loaded is not null && _loaded.Directory == model.Directory && (_loaded.Device == AiDevice.Gpu) == gpu) return _loaded;
            _loaded?.Dispose(); // quem chama já tem a vez (_run): nenhuma resposta usa o modelo agora
            _loaded = null;
            _handle ??= new OgaHandle();
            var started = DateTime.UtcNow;
            try
            {
                _loaded = (gpu ? TryLoad(model.Directory, AiDevice.Gpu) : null) ?? Load(model.Directory, AiDevice.Cpu);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
            {
                // A biblioteca nativa não carrega (ex.: sistema sem ONNX Runtime GenAI): quem chama recebe um erro conhecido.
                throw new InvalidOperationException(OnnxSessionFactory.RuntimeMissing, ex);
            }
            _log.LogInformation("Modelo de linguagem {Id} carregado na {Device} em {Seconds:0.0} s.", model.Manifest.Id,
                _loaded.Device == AiDevice.Gpu ? "GPU (DirectML)" : "CPU", (DateTime.UtcNow - started).TotalSeconds);
            return _loaded;
        }
    }

    private Loaded? TryLoad(string directory, AiDevice device)
    {
        try
        {
            return Load(directory, device);
        }
        catch (Exception ex) when (ex is OnnxRuntimeGenAIException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            _log.LogWarning(ex, "GPU indisponível para o modelo de linguagem; usando CPU.");
            return null;
        }
    }

    private static Loaded Load(string directory, AiDevice device)
    {
        using var config = new Config(directory);
        config.ClearProviders();
        // Só o Windows tem provedor de GPU no GenAI (DirectML); nos demais sistemas nenhum provedor é anexado: CPU.
        if (device == AiDevice.Gpu && GpuSupported) config.AppendProvider("dml");
        var model = new Model(config);
        try
        {
            return new Loaded(directory, device, model, new Tokenizer(model), model.GetModelType().StartsWith("phi", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    /// <summary>Libera o modelo; espera a resposta em andamento terminar (cancele-a antes para não esperar).</summary>
    public void Unload()
    {
        _run.Wait(); // não libera o modelo no meio de uma resposta
        try
        {
            lock (_gate)
            {
                _loaded?.Dispose();
                _loaded = null;
            }
        }
        finally
        {
            _run.Release();
        }
    }

    public void Dispose()
    {
        Unload();
        lock (_gate)
        {
            _handle?.Dispose();
            _handle = null;
        }
    }

    /// <param name="IsPhi">Modelo da família Phi: o formato de conversa é montado aqui, igual ao do modelo.</param>
    private sealed record Loaded(string Directory, AiDevice Device, Model Model, Tokenizer Tokenizer, bool IsPhi) : IDisposable
    {
        public void Dispose()
        {
            Tokenizer.Dispose();
            Model.Dispose();
        }
    }
}
