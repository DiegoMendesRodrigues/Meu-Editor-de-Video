// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Diálogo simples: escolher um item de uma lista (ex.: a música do visualizador de áudio).</summary>
public sealed class ChoiceWindow : Window
{
    private readonly ComboBox _choice;

    public ChoiceWindow(string title, string label, IReadOnlyList<string> options, int selected = 0, string okText = "OK")
    {
        DialogParts.Setup(this, title, 440);

        _choice = new ComboBox
        {
            ItemsSource = options,
            SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(_choice, label);

        var ok = DialogParts.Accent(okText, 96);
        ok.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        cancel.Click += (_, _) => Close(false);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(DialogParts.Label(this, label, 0, 6));
        root.Children.Add(_choice);
        root.Children.Add(DialogParts.Buttons(ok, cancel));
        Content = root;
    }

    public int SelectedIndex => _choice.SelectedIndex;
}

/// <summary>Ferramentas › Texto para fala: texto, voz do sistema e velocidade.</summary>
public sealed class TextToSpeechWindow : Window
{
    private readonly TextBox _text;
    private readonly ComboBox _voice;
    private readonly Slider _rate;

    public TextToSpeechWindow(IReadOnlyList<string> voices)
    {
        DialogParts.Setup(this, "Texto para fala", 520);

        _text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140 };
        ScrollViewer.SetVerticalScrollBarVisibility(_text, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(_text, "Texto a ser falado");
        _voice = new ComboBox
        {
            ItemsSource = voices,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = Math.Max(0, voices.ToList().FindIndex(v => v.Contains("Portuguese", StringComparison.OrdinalIgnoreCase) || v.Contains("Portugu", StringComparison.OrdinalIgnoreCase))),
        };
        AutomationProperties.SetName(_voice, "Voz");
        _rate = new Slider { Minimum = -10, Maximum = 10, Value = 0, TickFrequency = 1, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(_rate, "Velocidade da fala");

        var ok = DialogParts.Accent("Gerar e inserir", 120);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_text.Text)) { _text.Focus(); return; }
            Close(true);
        };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        cancel.Click += (_, _) => Close(false);

        var root = new StackPanel { Margin = new Thickness(16) };
        // Os textos ficam idênticos aos do WPF porque são a chave das traduções (a fala usa as vozes instaladas no sistema).
        root.Children.Add(DialogParts.Label(this, Translator.T(IntroText())));
        root.Children.Add(_text);
        root.Children.Add(DialogParts.Label(this, "Voz", 10));
        root.Children.Add(_voice);
        if (voices.Count == 0)
            root.Children.Add(DialogParts.Label(this, Translator.T(NoVoiceText()), 4));
        root.Children.Add(DialogParts.Label(this, "Velocidade (lenta ← → rápida)", 10));
        root.Children.Add(_rate);
        root.Children.Add(DialogParts.Buttons(ok, cancel));
        Content = root;
        Opened += (_, _) => _text.Focus();
    }

    /// <summary>Texto de abertura: a origem das vozes depende do sistema (SAPI no Windows, espeak-ng no Linux, "say" no macOS).</summary>
    private static string IntroText() =>
        OperatingSystem.IsWindows()
            ? "Digite o texto. A narração é gerada com as vozes instaladas no Windows (sem internet) e entra na posição do cursor, numa trilha de áudio livre."
            : OperatingSystem.IsMacOS()
                ? "Digite o texto. A narração é gerada com as vozes instaladas no macOS (sem internet) e entra na posição do cursor, numa trilha de áudio livre."
                : "Digite o texto. A narração é gerada com o espeak-ng (sem internet) e entra na posição do cursor, numa trilha de áudio livre.";

    /// <summary>Como instalar vozes, conforme o sistema.</summary>
    private static string NoVoiceText() =>
        OperatingSystem.IsWindows()
            ? "Nenhuma voz encontrada. Instale vozes em Configurações do Windows › Hora e idioma › Fala."
            : OperatingSystem.IsMacOS()
                ? "Nenhuma voz encontrada. Instale vozes em Ajustes do Sistema › Acessibilidade › Conteúdo Falado."
                : "Nenhuma voz encontrada. Instale o pacote espeak-ng (por exemplo, sudo apt install espeak-ng).";

    public string Text => (_text.Text ?? "").Trim();

    public string? Voice => _voice.SelectedItem as string;

    public int Rate => (int)Math.Round(_rate.Value);
}
