using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.AIEngine.Runtime;

/// <summary>
/// Mantém aberta a sessão do modelo de uma capacidade para uso repetido (quadro a quadro) e a reabre quando o modelo
/// instalado ou o dispositivo escolhido (CPU/GPU) mudam.
/// </summary>
public sealed class ModelSessionHolder<T> : IDisposable where T : class
{
    private readonly IModelRegistry _registry;
    private readonly OnnxSessionFactory _sessions;
    private readonly ModelCapability _capability;
    private readonly Func<OnnxModelSession, InstalledModel, T> _wrap;
    private readonly Lock _gate = new();
    private OnnxModelSession? _session;
    private T? _value;
    private (string Id, string Version, AiDevice Device)? _key;

    public ModelSessionHolder(IModelRegistry registry, OnnxSessionFactory sessions, ModelCapability capability,
        Func<OnnxModelSession, InstalledModel, T> wrap)
    {
        _registry = registry;
        _sessions = sessions;
        _capability = capability;
        _wrap = wrap;
        registry.Changed += (_, _) => Reset();
    }

    public bool IsAvailable => _registry.IsAvailable(_capability);

    /// <summary>Dispositivo da sessão aberta (null se nenhuma).</summary>
    public AiDevice? Device
    {
        get
        {
            lock (_gate) return _session?.Device;
        }
    }

    /// <summary>Instância pronta para uso, ou null quando o modelo não está instalado.</summary>
    public T? Get()
    {
        var model = _registry.Find(_capability);
        if (model is null || !File.Exists(model.ModelPath))
        {
            Reset();
            return null;
        }
        var key = (model.Manifest.Id, model.Manifest.Version, _sessions.Requested);
        lock (_gate)
        {
            if (_value is not null && _key == key) return _value;
            _session?.Dispose();
            _session = _sessions.Create(model);
            _value = _wrap(_session, model);
            _key = key;
            return _value;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
            _value = null;
            _key = null;
        }
    }

    public void Dispose() => Reset();
}
