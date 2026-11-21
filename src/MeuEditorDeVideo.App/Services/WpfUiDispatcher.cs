using System.Windows.Media;
using System.Windows.Threading;

namespace MeuEditorDeVideo.App.Services;

/// <summary>Thread da interface do WPF para os ViewModels (<see cref="UiThread"/>).</summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action, UiPriority priority = UiPriority.Normal) => dispatcher.BeginInvoke(action, Map(priority));

    public void Invoke(Action action) => dispatcher.Invoke(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiPriority priority = UiPriority.Normal)
    {
        var timer = new DispatcherTimer(Map(priority), dispatcher) { Interval = interval };
        timer.Tick += (_, _) => tick();
        return new Timer(timer);
    }

    public event EventHandler? Rendering
    {
        add => CompositionTarget.Rendering += value;
        remove => CompositionTarget.Rendering -= value;
    }

    private static DispatcherPriority Map(UiPriority priority) => priority switch
    {
        UiPriority.Background => DispatcherPriority.Background,
        UiPriority.Render => DispatcherPriority.Render,
        _ => DispatcherPriority.Normal,
    };

    private sealed class Timer(DispatcherTimer timer) : IUiTimer
    {
        public bool IsEnabled => timer.IsEnabled;

        public void Start() => timer.Start();

        public void Stop() => timer.Stop();
    }
}
