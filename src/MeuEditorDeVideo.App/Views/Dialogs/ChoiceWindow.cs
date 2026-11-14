using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Diálogo simples: escolher um item de uma lista (ex.: a música do visualizador de áudio).</summary>
public sealed class ChoiceWindow : Window
{
    private readonly ComboBox _choice;

    public ChoiceWindow(string title, string label, IReadOnlyList<string> options, int selected = 0, string okText = "OK")
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.WindowBackground");

        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");
        _choice = new ComboBox { ItemsSource = options, SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1)) };
        AutomationProperties.SetName(_choice, label);

        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Med.AccentButton");
        AutomationProperties.SetName(ok, okText);
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(text);
        root.Children.Add(_choice);
        root.Children.Add(buttons);
        Content = root;
    }

    public int SelectedIndex => _choice.SelectedIndex;
}

/// <summary>Ferramentas › Texto para fala: texto, voz do Windows e velocidade.</summary>
public sealed class TextToSpeechWindow : Window
{
    private readonly TextBox _text;
    private readonly ComboBox _voice;
    private readonly Slider _rate;

    public TextToSpeechWindow(IReadOnlyList<string> voices)
    {
        Title = "Texto para fala";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.WindowBackground");

        TextBlock Label(string s, double top = 0)
        {
            var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");
            return t;
        }

        _text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(_text, "Texto a ser falado");
        _voice = new ComboBox { ItemsSource = voices, SelectedIndex = Math.Max(0, voices.ToList().FindIndex(v => v.Contains("Portuguese", StringComparison.OrdinalIgnoreCase) || v.Contains("Portugu", StringComparison.OrdinalIgnoreCase))) };
        AutomationProperties.SetName(_voice, "Voz");
        _rate = new Slider { Minimum = -10, Maximum = 10, Value = 0, TickFrequency = 1, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(_rate, "Velocidade da fala");

        var ok = new Button { Content = "Gerar e inserir", IsDefault = true, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Med.AccentButton");
        AutomationProperties.SetName(ok, "Gerar e inserir");
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_text.Text)) { _text.Focus(); return; }
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Label("Digite o texto. A narração é gerada com as vozes instaladas no Windows (sem internet) e entra na posição do cursor, numa trilha de áudio livre."));
        root.Children.Add(_text);
        root.Children.Add(Label("Voz", 10));
        root.Children.Add(_voice);
        if (voices.Count == 0)
            root.Children.Add(Label("Nenhuma voz encontrada. Instale vozes em Configurações do Windows › Hora e idioma › Fala.", 4));
        root.Children.Add(Label("Velocidade (lenta ← → rápida)", 10));
        root.Children.Add(_rate);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _text.Focus();
    }

    public string Text => _text.Text.Trim();
    public string? Voice => _voice.SelectedItem as string;
    public int Rate => (int)Math.Round(_rate.Value);
}
