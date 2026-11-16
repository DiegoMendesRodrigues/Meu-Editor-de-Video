using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>
/// Ferramentas › Gravar tela: escolhe tela, webcam e microfone, grava e devolve o arquivo em <see cref="RecordedPath"/>.
/// Ao gravar, o programa é minimizado, uma contagem de 5 segundos aparece na tela escolhida e a gravação começa; a tecla
/// F10 (atalho global, registrado só durante a gravação) para e traz o programa de volta.
/// </summary>
public sealed partial class ScreenRecordWindow : Window
{
    private const string None = "(nenhum)";
    private const string AllScreens = "Todas as telas";
    private const int HotkeyId = 0x4D45;
    private const int WmHotkey = 0x0312;
    private const uint VkF10 = 0x79;
    private const uint ModNoRepeat = 0x4000;
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
    private HwndSource? _source;
    private bool _hotkey;
    private WindowState _ownerState = WindowState.Maximized;

    public ScreenRecordWindow(IScreenRecorder recorder, string outputPath)
    {
        _recorder = recorder;
        _outputPath = outputPath;
        Title = "Gravar tela";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.WindowBackground");

        _screen = Check("Gravar a tela", true);
        _screensLabel = Label("Tela a gravar");
        _screens = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_screens, "Tela a gravar");
        IReadOnlyList<ScreenArea> screens;
        try { screens = recorder.Screens(); }
        catch (System.ComponentModel.Win32Exception) { screens = []; }
        if (screens.Count > 1)
        {
            _screens.ItemsSource = new object[] { AllScreens }.Concat(screens).ToList();
            _screens.SelectedItem = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        }
        else
        {
            _screensLabel.Visibility = Visibility.Collapsed;
            _screens.Visibility = Visibility.Collapsed;
        }
        // Depois da tela: gravar a tela toda ou só uma janela aberta nela.
        _mode = Combo("O que gravar", ["Tela inteira", "Apenas uma janela"]);
        _windowsLabel = Label("Janela a gravar");
        _windows = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_windows, "Janela a gravar");
        _mode.SelectionChanged += (_, _) => RefreshWindows();
        _screens.SelectionChanged += (_, _) => RefreshWindows();
        _windows.DropDownOpened += (_, _) => RefreshWindows(keepSelection: true);
        _screen.Checked += (_, _) => { _screens.IsEnabled = true; _mode.IsEnabled = true; _windows.IsEnabled = true; };
        _screen.Unchecked += (_, _) => { _screens.IsEnabled = false; _mode.IsEnabled = false; _windows.IsEnabled = false; };
        _cursor = Check("Mostrar o cursor do mouse", true);
        _camera = Combo("Webcam", [None]);
        _mic = Combo("Microfone", [None]);
        _corner = Combo("Posição da webcam", ["Canto inferior direito", "Canto inferior esquerdo", "Canto superior direito", "Canto superior esquerdo"]);
        _insert = Check("Inserir a gravação na linha do tempo, no cursor", true);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Text = "Procurando câmeras e microfones…" };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Med.TextMuted");

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

        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontWeight = FontWeights.SemiBold,
            Text = "Ao clicar em Gravar, o programa é minimizado e a gravação começa depois de uma contagem de 5 segundos. Para parar a gravação, pressione F10.",
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");

        _record = new Button { Content = "Gravar", IsDefault = true, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _record.SetResourceReference(StyleProperty, "Med.AccentButton");
        AutomationProperties.SetName(_record, "Gravar");
        _record.Click += async (_, _) => await ToggleAsync();
        var cancel = new Button { Content = "Fechar", IsCancel = true, MinWidth = 96 };
        AutomationProperties.SetName(cancel, "Fechar");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_record);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_options);
        root.Children.Add(hint);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        Content = root;

        _corner.SelectedIndex = 0;
        _timer.Tick += (_, _) =>
        {
            if (_recording is { } r) _status.Text = $"● Gravando  {r.Elapsed:hh\\:mm\\:ss}   (pressione F10 ou clique em Parar para terminar)";
        };
        Loaded += async (_, _) => await LoadDevicesAsync();
        SourceInitialized += (_, _) =>
        {
            _source = (HwndSource)PresentationSource.FromVisual(this);
            _source.AddHook(Hook);
        };
        Closed += (_, _) => UnregisterStopKey();
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

    private async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await _recorder.ListDevicesAsync();
            _camera.ItemsSource = new[] { None }.Concat(devices.Cameras).ToList();
            _mic.ItemsSource = new[] { None }.Concat(devices.Microphones).ToList();
            _camera.SelectedIndex = 0;
            _mic.SelectedIndex = devices.Microphones.Count > 0 ? 1 : 0;
            _status.Text = $"{devices.Cameras.Count} câmera(s) e {devices.Microphones.Count} microfone(s) encontrados. O vídeo é gravado em MP4.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _status.Text = "Não foi possível listar os dispositivos: " + ex.Message;
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
        var area = _screen.IsChecked == true && _screens.Visibility == Visibility.Visible ? _screens.SelectedItem as ScreenArea : null;
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

        if (!RegisterStopKey())
        {
            _status.Text = "A tecla F10 está em uso por outro programa; feche-o e tente de novo.";
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
            DialogResult = true;
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

    /// <summary>Minimiza o programa (esta janela some junto, por pertencer a ele).</summary>
    private void MinimizeApp()
    {
        if (Owner is { } owner)
        {
            _ownerState = owner.WindowState == WindowState.Minimized ? WindowState.Maximized : owner.WindowState;
            owner.WindowState = WindowState.Minimized;
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    /// <summary>Traz o programa de volta como estava antes da gravação.</summary>
    private void RestoreApp()
    {
        if (Owner is { } owner)
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
        var number = new TextBlock { FontSize = 150, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var hint = new TextBlock
        {
            Text = "A gravação vai começar.\nPressione F10 para parar.",
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
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Width = 360,
            Height = 360,
            Title = "Contagem da gravação",
            Content = new Border { CornerRadius = new CornerRadius(180), Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 28)), Child = panel },
        };
        AutomationProperties.SetName(overlay, "Contagem da gravação");
        var (cx, cy) = CenterOf(area);
        overlay.Left = cx - overlay.Width / 2;
        overlay.Top = cy - overlay.Height / 2;
        overlay.Show();
        try
        {
            for (var i = 5; i >= 1; i--)
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
        // Dá tempo de o Windows apagar a contagem antes da primeira captura.
        await Task.Delay(250);
    }

    /// <summary>Centro da tela gravada em unidades do WPF (o monitor principal quando são todas).</summary>
    private (double X, double Y) CenterOf(ScreenArea? area)
    {
        if (area is null)
        {
            var w = SystemParameters.WorkArea;
            return (w.Left + w.Width / 2, w.Top + w.Height / 2);
        }
        var toDip = _source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var center = toDip.Transform(new Point(area.X + area.Width / 2.0, area.Y + area.Height / 2.0));
        return (center.X, center.Y);
    }

    private bool RegisterStopKey()
    {
        if (_hotkey) return true;
        if (_source is null) return false;
        _hotkey = RegisterHotKey(_source.Handle, HotkeyId, ModNoRepeat, VkF10);
        return _hotkey;
    }

    private void UnregisterStopKey()
    {
        if (!_hotkey || _source is null) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _hotkey = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            if (_recording is not null && _record.IsEnabled) _ = ToggleAsync();
        }
        return IntPtr.Zero;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>Mostra a lista de janelas só no modo "Apenas uma janela", com as janelas abertas na tela escolhida.</summary>
    private void RefreshWindows(bool keepSelection = false)
    {
        var single = _mode.SelectedIndex == 1;
        _windowsLabel.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        _windows.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        if (!single) return;
        var previous = (_windows.SelectedItem as CaptureWindow)?.Title;
        var area = _screens.Visibility == Visibility.Visible ? _screens.SelectedItem as ScreenArea : null;
        IReadOnlyList<CaptureWindow> windows;
        try { windows = _recorder.Windows(area); }
        catch (System.ComponentModel.Win32Exception) { windows = []; }
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
        var c = new ComboBox { ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(c, name);
        return c;
    }

    private static TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");
        return t;
    }
}
