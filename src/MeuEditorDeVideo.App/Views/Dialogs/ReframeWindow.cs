using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Opções de Projeto › Reenquadrar automaticamente: novo formato e o que acompanhar.</summary>
public sealed class ReframeWindow : Window
{
    public static readonly IReadOnlyList<(string Label, int W, int H)> Formats =
    [
        ("9:16 — vertical (Reels, TikTok, Shorts)", 9, 16),
        ("1:1 — quadrado", 1, 1),
        ("4:5 — retrato (feed)", 4, 5),
        ("16:9 — paisagem", 16, 9),
    ];

    private readonly ComboBox _format;
    private readonly ComboBox _follow;

    public ReframeWindow(string currentSize, bool trackingAvailable)
    {
        Title = "Reenquadrar automaticamente";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.WindowBackground");

        TextBlock Label(string text, double top = 0)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");
            return t;
        }

        _format = new ComboBox { ItemsSource = Formats.Select(f => f.Label).ToList(), SelectedIndex = 0 };
        AutomationProperties.SetName(_format, "Novo formato");
        _follow = new ComboBox
        {
            ItemsSource = new[] { "Rosto (cabeça)", "Corpo inteiro", "Centro do quadro (sem acompanhar)" },
            SelectedIndex = trackingAvailable ? 0 : 2,
            IsEnabled = trackingAvailable,
        };
        AutomationProperties.SetName(_follow, "Acompanhar");

        var ok = new Button { Content = "Reenquadrar", IsDefault = true, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Med.AccentButton");
        AutomationProperties.SetName(ok, "Reenquadrar");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Label($"Muda o formato do projeto (hoje {currentSize}) e reposiciona cada clipe de vídeo e imagem para cobrir o novo quadro. " +
                                "Os textos ficam como estão. Tudo pode ser desfeito com Ctrl+Z."));
        root.Children.Add(Label("Novo formato", 8));
        root.Children.Add(_format);
        root.Children.Add(Label("Acompanhar", 12));
        root.Children.Add(_follow);
        if (!trackingAvailable)
            root.Children.Add(Label("Para acompanhar a pessoa, instale o modelo de remoção de fundo em Ferramentas › Modelos de IA.", 6));
        root.Children.Add(buttons);
        Content = root;
    }

    public (int W, int H) Format => (Formats[_format.SelectedIndex].W, Formats[_format.SelectedIndex].H);

    /// <summary>null = não acompanhar; true = cabeça; false = corpo.</summary>
    public bool? FollowHead => _follow.SelectedIndex switch { 0 => true, 1 => false, _ => null };
}
