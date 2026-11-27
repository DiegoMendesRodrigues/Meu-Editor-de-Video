// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

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
        DialogParts.Setup(this, "Reenquadrar automaticamente", 460);

        _format = new ComboBox { ItemsSource = Formats.Select(f => f.Label).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_format, "Novo formato");
        _follow = new ComboBox
        {
            ItemsSource = new[] { "Rosto (cabeça)", "Corpo inteiro", "Centro do quadro (sem acompanhar)" },
            SelectedIndex = trackingAvailable ? 0 : 2,
            IsEnabled = trackingAvailable,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(_follow, "Acompanhar");

        var ok = DialogParts.Accent("Reenquadrar", 110);
        ok.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        cancel.Click += (_, _) => Close(false);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(DialogParts.Label(this, Translator.F("Muda o formato do projeto (hoje {0}) e reposiciona cada clipe de vídeo e imagem para cobrir o novo quadro. " +
                                                                "Os textos ficam como estão. Tudo pode ser desfeito com Ctrl+Z.", currentSize)));
        root.Children.Add(DialogParts.Label(this, "Novo formato", 8));
        root.Children.Add(_format);
        root.Children.Add(DialogParts.Label(this, "Acompanhar", 12));
        root.Children.Add(_follow);
        if (!trackingAvailable)
            root.Children.Add(DialogParts.Label(this, "Para acompanhar a pessoa, instale o modelo de remoção de fundo em Ferramentas › Modelos de IA.", 6));
        root.Children.Add(DialogParts.Buttons(ok, cancel));
        Content = root;
    }

    public (int W, int H) Format
    {
        get
        {
            var f = Formats[Math.Max(0, _format.SelectedIndex)];
            return (f.W, f.H);
        }
    }

    /// <summary>null = não acompanhar; true = cabeça; false = corpo.</summary>
    public bool? FollowHead => _follow.SelectedIndex switch { 0 => true, 1 => false, _ => null };
}
