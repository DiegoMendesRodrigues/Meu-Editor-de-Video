namespace MeuEditorDeVideo.Presentation.Threading;

/// <summary>Prioridade de uma ação agendada na thread da interface.</summary>
public enum UiPriority
{
    /// <summary>Depois do desenho e da entrada (atualizações de listas).</summary>
    Background,
    Normal,
    /// <summary>Junto do desenho (quadros da prévia).</summary>
    Render,
}

/// <summary>Temporizador que dispara na thread da interface.</summary>
public interface IUiTimer
{
    bool IsEnabled { get; }

    void Start();

    void Stop();
}

/// <summary>Acesso à thread da interface, sem depender do toolkit (WPF hoje, Avalonia depois).</summary>
public interface IUiDispatcher
{
    /// <summary>A thread atual é a da interface?</summary>
    bool CheckAccess();

    /// <summary>Agenda <paramref name="action"/> na thread da interface e retorna em seguida.</summary>
    void Post(Action action, UiPriority priority = UiPriority.Normal);

    /// <summary>Executa <paramref name="action"/> na thread da interface e espera terminar.</summary>
    void Invoke(Action action);

    /// <summary>Temporizador parado que chama <paramref name="tick"/> na thread da interface.</summary>
    IUiTimer CreateTimer(TimeSpan interval, Action tick, UiPriority priority = UiPriority.Normal);

    /// <summary>Uma vez por quadro de tela, na thread da interface, antes do desenho (cursor durante a reprodução).</summary>
    event EventHandler? Rendering;
}

/// <summary>
/// Despachante da interface usado pelos ViewModels. O aplicativo o troca pelo do toolkit na inicialização; sem
/// interface (testes), as ações rodam na própria thread e os temporizadores não disparam.
/// </summary>
public static class UiThread
{
    public static IUiDispatcher Dispatcher { get; set; } = InlineUiDispatcher.Instance;
}

/// <summary>Sem thread de interface: executa na hora; temporizadores nunca disparam.</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public static InlineUiDispatcher Instance { get; } = new();

    public bool CheckAccess() => true;

    public void Post(Action action, UiPriority priority = UiPriority.Normal) => action();

    public void Invoke(Action action) => action();

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiPriority priority = UiPriority.Normal) => new IdleTimer();

    /// <summary>Sem tela, não há quadros: nunca dispara.</summary>
    public event EventHandler? Rendering
    {
        add { }
        remove { }
    }

    private sealed class IdleTimer : IUiTimer
    {
        public bool IsEnabled { get; private set; }

        public void Start() => IsEnabled = true;

        public void Stop() => IsEnabled = false;
    }
}
