using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>Thread da interface do Avalonia para os ViewModels (<see cref="UiThread"/>).</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    private EventHandler? _rendering;
    private bool _frameRequested;
    private DispatcherTimer? _fallbackTimer;

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action, UiPriority priority = UiPriority.Normal) => Dispatcher.UIThread.Post(action, Map(priority));

    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiPriority priority = UiPriority.Normal)
    {
        var timer = new DispatcherTimer(Map(priority)) { Interval = interval };
        timer.Tick += (_, _) => tick();
        return new Timer(timer);
    }

    /// <summary>
    /// Uma vez por quadro de tela enquanto houver assinantes (como o CompositionTarget.Rendering do WPF): pede o próximo
    /// quadro à janela principal com <see cref="TopLevel.RequestAnimationFrame"/>. Sem janela, usa um temporizador de ~60 Hz.
    /// </summary>
    public event EventHandler? Rendering
    {
        add
        {
            Dispatcher.UIThread.VerifyAccess();
            _rendering += value;
            RequestFrame();
        }
        remove
        {
            Dispatcher.UIThread.VerifyAccess();
            _rendering -= value;
            if (_rendering is null) _fallbackTimer?.Stop();
        }
    }

    private void RequestFrame()
    {
        if (_rendering is null || _frameRequested) return;
        var top = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (top is { IsVisible: true })
        {
            _fallbackTimer?.Stop();
            _frameRequested = true;
            top.RequestAnimationFrame(_ =>
            {
                _frameRequested = false;
                _rendering?.Invoke(this, EventArgs.Empty);
                RequestFrame();
            });
            return;
        }
        if (_fallbackTimer is null)
        {
            _fallbackTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _fallbackTimer.Tick += (_, _) =>
            {
                _rendering?.Invoke(this, EventArgs.Empty);
                if (_rendering is null) _fallbackTimer.Stop();
                else RequestFrame();
            };
        }
        if (!_fallbackTimer.IsEnabled) _fallbackTimer.Start();
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
