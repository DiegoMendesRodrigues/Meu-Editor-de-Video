// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Presentation.Localization;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>
/// Ferramentas › Gravar tela: escolhe tela, webcam e microfone, grava e devolve o arquivo em <see cref="RecordedPath"/>.
/// Ao gravar, o programa é minimizado, uma contagem de 5 segundos aparece na tela escolhida e a gravação começa; a tecla
/// F10 (atalho global quando <see cref="GlobalHotkeys.Current"/> tem uma implementação real, registrado só durante a
/// gravação; senão, a tecla da própria janela) para e traz o programa de volta.
/// Tudo vem de <see cref="IScreenRecorder"/> (Windows, Linux X11 ou macOS); sem P/Invoke aqui.
/// </summary>
public sealed class ScreenRecordWindow : Window
{
    private const string None = "(nenhum)";
    private const string AllScreens = "Todas as telas";
    private readonly IScreenRecorder _recorder;
    private readonly string _outputPath;
    private readonly CheckBox _screen;
    private readonly TextBlock _screensLabel;
    private readonly ComboBox _screens;
    private readonly ComboBox _mode;
    private readonly TextBlock _windowsLabel;
    private readonly ComboBox _windows;
    private readonly CheckBox _cursor;
    private readonly ComboBox _camera;
    private readonly ComboBox _mic;
    private readonly ComboBox _corner;
    private readonly CheckBox _insert;
    private readonly Button _record;
    private readonly TextBlock _status;
    private readonly StackPanel _options;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IScreenRecording? _recording;
    private IDisposable? _hotkey;
    private WindowState _ownerState = WindowState.Maximized;
    private readonly bool _screenAvailable;

    public ScreenRecordWindow(IScreenRecorder recorder, string outputPath)
    {
        _recorder = recorder;
        _outputPath = outputPath;
        DialogParts.Setup(this, "Gravar tela", 460);

        var unavailable = recorder.ScreenCaptureUnavailableReason;
        _screenAvailable = unavailable is null;
        _screen = Check("Gravar a tela", _screenAvailable);
        _screensLabel = Label("Tela a gravar");
        _screens = new ComboBox { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_screens, "Tela a gravar");
        IReadOnlyList<ScreenArea> screens;
        try { screens = recorder.Screens(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { screens = []; }
        if (screens.Count > 1)
        {
            _screens.ItemsSource = new object[] { AllScreens }.Concat(screens).ToList();
            _screens.SelectedItem = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        }
        else
        {
            _screensLabel.IsVisible = false;
            _screens.IsVisible = false;
        }
        // Depois da tela: gravar a tela toda ou só uma janela aberta nela.
        _mode = Combo("O que gravar", ["Tela inteira", "Apenas uma janela"]);
        _windowsLabel = Label("Janela a gravar");
        _windows = new ComboBox { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_windows, "Janela a gravar");
        _mode.SelectionChanged += (_, _) => RefreshWindows();
        _screens.SelectionChanged += (_, _) => RefreshWindows();
        _windows.DropDownOpened += (_, _) => RefreshWindows(keepSelection: true);
        _screen.IsCheckedChanged += (_, _) =>
        {
            var on = _screen.IsChecked == true;
            _screens.IsEnabled = on;
            _mode.IsEnabled = on;
            _windows.IsEnabled = on;
        };
        _cursor = Check("Mostrar o cursor do mouse", true);
        _camera = Combo("Webcam", [None]);
        _mic = Combo("Microfone", [None]);
        _corner = Combo("Posição da webcam", ["Canto inferior direito", "Canto inferior esquerdo", "Canto superior direito", "Canto superior esquerdo"]);
        _insert = Check("Inserir a gravação na linha do tempo, no cursor", true);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Text = "Procurando câmeras e microfones…" };
        _status[!TextBlock.ForegroundProperty] = this.GetResourceObservable("Med.TextMuted").ToBinding();

        _options = new StackPanel();
        _options.Children.Add(_screen);
        _options.Children.Add(_screensLabel);
        _options.Children.Add(_screens);
        _options.Children.Add(Label("O que gravar"));
        _options.Children.Add(_mode);
        _options.Children.Add(_windowsLabel);
        _options.Children.Add(_windows);
        _options.Children.Add(_cursor);
        _options.Children.Add(Label("Webcam"));
        _options.Children.Add(_camera);
        _options.Children.Add(Label("Posição da webcam sobre a tela"));
        _options.Children.Add(_corner);
        _options.Children.Add(Label("Microfone"));
        _options.Children.Add(_mic);
        _options.Children.Add(_insert);
        RefreshWindows();

        var hint = DialogParts.Label(this, GlobalHotkeys.Current.IsGlobal
            ? "Ao clicar em Gravar, o programa é minimizado e a gravação começa depois de uma contagem de 5 segundos. Para parar a gravação, pressione F10."
            : "Ao clicar em Gravar, o programa é minimizado e a gravação começa depois de uma contagem de 5 segundos. Para parar a gravação, abra o programa de novo (pela barra de tarefas ou pelo Dock) e clique em Parar.",
            6, 0);
        hint.FontWeight = FontWeight.SemiBold;

        _record = DialogParts.Accent("Gravar", 110);
        _record.IsEnabled = false;
        _record.Click += async (_, _) => await ToggleAsync();
        var cancel = new Button { Content = "Fechar", IsCancel = true, MinWidth = 96 };
        AutomationProperties.SetName(cancel, "Fechar");
        cancel.Click += (_, _) => Close(false);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_options);
        root.Children.Add(hint);
        root.Children.Add(_status);
        root.Children.Add(DialogParts.Buttons(_record, cancel));
        Content = root;

        if (!_screenAvailable)
        {
            // Ex.: sessão Wayland. Webcam e microfone continuam valendo.
            _screen.IsChecked = false;
            _screen.IsEnabled = false;
            _screens.IsEnabled = false;
            _mode.IsEnabled = false;
            _windows.IsEnabled = false;
        }

        _corner.SelectedIndex = 0;
        _timer.Tick += (_, _) =>
        {
            if (_recording is { } r) _status.Text = Translator.F("● Gravando  {0}   (pressione F10 ou clique em Parar para terminar)", r.Elapsed.ToString(@"hh\:mm\:ss"));
        };
        Opened += async (_, _) => await LoadDevicesAsync();
        // Sem atalho global, o F10 só vale com esta janela em primeiro plano.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F10 && _recording is not null && _record.IsEnabled)
            {
                e.Handled = true;
                _ = ToggleAsync();
            }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            UnregisterStopKey();
        };
        Closing += (_, e) =>
        {
            if (_recording is null) return;
            e.Cancel = true;
            _status.Text = "Pare a gravação antes de fechar.";
        };
    }

    /// <summary>Arquivo gravado (nulo se nada foi gravado).</summary>
    public string? RecordedPath { get; private set; }

    public bool InsertOnTimeline => _insert.IsChecked == true;

    /// <summary>Segundos da contagem antes de gravar (5 no uso normal; os testes encurtam).</summary>
    public int CountdownSeconds { get; set; } = 5;

    private async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await _recorder.ListDevicesAsync();
            _camera.ItemsSource = new[] { None }.Concat(devices.Cameras).ToList();
            _mic.ItemsSource = new[] { None }.Concat(devices.Microphones).ToList();
            _camera.SelectedIndex = 0;
            _mic.SelectedIndex = devices.Microphones.Count > 0 ? 1 : 0;
            _status.Text = _screenAvailable
                ? $"{devices.Cameras.Count} câmera(s) e {devices.Microphones.Count} microfone(s) encontrados. O vídeo é gravado em MP4."
                : Translator.T(_recorder.ScreenCaptureUnavailableReason ?? "") + Translator.T($" {devices.Cameras.Count} câmera(s) e {devices.Microphones.Count} microfone(s) encontrados.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _status.Text = "Não foi possível listar os dispositivos: " + ex.Message;
        }
        if (!_recorder.IsAvailable)
        {
            _status.Text = "A gravação de tela não está disponível neste sistema.";
            return;
        }
        _record.IsEnabled = true;
    }

    private async Task ToggleAsync()
    {
        _record.IsEnabled = false;
        try
        {
            if (_recording is null) await StartAsync();
            else await StopAsync();
        }
        finally
        {
            _record.IsEnabled = true;
        }
    }

    private async Task StartAsync()
    {
        var camera = _camera.SelectedItem as string is { } c && c != None ? c : null;
        var mic = _mic.SelectedItem as string is { } m && m != None ? m : null;
        if (_screen.IsChecked != true && camera is null)
        {
            _status.Text = "Escolha gravar a tela, a webcam ou as duas.";
            return;
        }
        var area = _screen.IsChecked == true && _screens.IsVisible ? _screens.SelectedItem as ScreenArea : null;
        CaptureWindow? window = null;
        if (_screen.IsChecked == true && _mode.SelectedIndex == 1)
        {
            window = _windows.SelectedItem as CaptureWindow;
            if (window is null)
            {
                _status.Text = "Escolha a janela a gravar.";
                return;
            }
        }
        var options = new ScreenRecordingOptions(_outputPath, _screen.IsChecked == true, camera, mic,
            ShowCursor: _cursor.IsChecked == true, Corner: (WebcamCorner)Math.Max(0, _corner.SelectedIndex), Area: area, Window: window);

        if (!RegisterStopKey(out var error))
        {
            _status.Text = error;
            return;
        }
        _options.IsEnabled = false;
        MinimizeApp();
        await CountdownAsync(area);
        try
        {
            _recording = await _recorder.StartAsync(options);
        }
        catch (InvalidOperationException ex)
        {
            UnregisterStopKey();
            RestoreApp();
            _options.IsEnabled = true;
            _status.Text = ex.Message;
            return;
        }
        _record.Content = "Parar";
        AutomationProperties.SetName(_record, "Parar");
        _timer.Start();
    }

    private async Task StopAsync()
    {
        var recording = _recording!;
        _timer.Stop();
        UnregisterStopKey();
        RestoreApp();
        _status.Text = "Finalizando o arquivo…";
        try
        {
            RecordedPath = await recording.StopAsync();
            _recording = null;
            await recording.DisposeAsync();
            Close(true);
        }
        catch (InvalidOperationException ex)
        {
            _recording = null;
            await recording.DisposeAsync();
            _status.Text = ex.Message;
            _options.IsEnabled = true;
            _record.Content = "Gravar";
            AutomationProperties.SetName(_record, "Gravar");
        }
    }

    /// <summary>Minimiza o programa e esta janela, para não aparecerem na gravação.</summary>
    private void MinimizeApp()
    {
        if (Owner is Window owner)
        {
            _ownerState = owner.WindowState == WindowState.Minimized ? WindowState.Maximized : owner.WindowState;
            owner.WindowState = WindowState.Minimized;
        }
        WindowState = WindowState.Minimized;
    }

    /// <summary>Traz o programa de volta como estava antes da gravação.</summary>
    private void RestoreApp()
    {
        if (Owner is Window owner)
        {
            owner.WindowState = _ownerState;
            owner.Activate();
        }
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Contagem 5, 4, 3, 2, 1 no centro da tela gravada; fecha antes de a gravação começar.</summary>
    private async Task CountdownAsync(ScreenArea? area)
    {
        if (CountdownSeconds <= 0) return;
        var number = new TextBlock { FontSize = 150, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var hint = new TextBlock
        {
            Text = GlobalHotkeys.Current.IsGlobal ? "A gravação vai começar.\nPressione F10 para parar." : "A gravação vai começar.",
            FontSize = 18,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        panel.Children.Add(number);
        panel.Children.Add(hint);
        var overlay = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Width = 360,
            Height = 360,
            Title = "Contagem da gravação",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Border { CornerRadius = new CornerRadius(180), Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 28)), Child = panel },
        };
        AutomationProperties.SetName(overlay, "Contagem da gravação");
        overlay.Position = OverlayPosition(area, overlay.Width);
        overlay.Show();
        try
        {
            for (var i = CountdownSeconds; i >= 1; i--)
            {
                number.Text = i.ToString(System.Globalization.CultureInfo.CurrentCulture);
                AutomationProperties.SetName(number, number.Text);
                await Task.Delay(1000);
            }
        }
        finally
        {
            overlay.Close();
        }
        // Dá tempo de o sistema apagar a contagem antes da primeira captura.
        await Task.Delay(250);
    }

    /// <summary>Canto superior esquerdo da contagem para ela ficar no centro da tela gravada (o monitor principal quando são todas).</summary>
    private PixelPoint OverlayPosition(ScreenArea? area, double size)
    {
        PixelPoint center;
        double scale;
        if (area is not null)
        {
            center = new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2);
            scale = Screens.ScreenFromPoint(center)?.Scaling ?? RenderScaling;
        }
        else if (Screens.Primary is { } primary)
        {
            center = primary.WorkingArea.Center;
            scale = primary.Scaling;
        }
        else
        {
            return new PixelPoint(0, 0);
        }
        var half = (int)Math.Round(size * scale / 2);
        return new PixelPoint(center.X - half, center.Y - half);
    }

    private bool RegisterStopKey(out string error)
    {
        error = "";
        if (_hotkey is not null) return true;
        try
        {
            _hotkey = GlobalHotkeys.Current.Register(MedKey.F10, () => Dispatcher.UIThread.Post(() =>
            {
                if (_recording is not null && _record.IsEnabled) _ = ToggleAsync();
            }));
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = string.IsNullOrWhiteSpace(ex.Message) ? "A tecla F10 está em uso por outro programa; feche-o e tente de novo." : ex.Message;
            return false;
        }
    }

    private void UnregisterStopKey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
    }

    /// <summary>Mostra a lista de janelas só no modo "Apenas uma janela", com as janelas abertas na tela escolhida.</summary>
    private void RefreshWindows(bool keepSelection = false)
    {
        var single = _mode.SelectedIndex == 1;
        _windowsLabel.IsVisible = single;
        _windows.IsVisible = single;
        if (!single) return;
        var previous = (_windows.SelectedItem as CaptureWindow)?.Title;
        var area = _screens.IsVisible ? _screens.SelectedItem as ScreenArea : null;
        IReadOnlyList<CaptureWindow> windows;
        try { windows = _recorder.Windows(area); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { windows = []; }
        _windows.ItemsSource = windows;
        _windows.SelectedItem = (keepSelection ? windows.FirstOrDefault(w => w.Title == previous) : null) ?? windows.FirstOrDefault();
        if (windows.Count == 0) _status.Text = "Nenhuma janela aberta nesta tela.";
    }

    private static CheckBox Check(string text, bool value)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(c, text);
        return c;
    }

    private static ComboBox Combo(string name, IReadOnlyList<string> items)
    {
        var c = new ComboBox { ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(c, name);
        return c;
    }

    private TextBlock Label(string text) => DialogParts.Label(this, text, 4, 4);
}
