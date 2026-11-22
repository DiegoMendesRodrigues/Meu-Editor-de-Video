using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Tipo do aviso: muda o ícone ao lado do texto.</summary>
public enum MessageKind
{
    Info,
    Question,
    Warning,
    Error,
}

/// <summary>
/// Caixa de mensagem (o Avalonia não tem MessageBox): texto, ícone e botões. <see cref="Result"/> é o índice do botão
/// escolhido, ou -1 se a janela foi fechada sem escolher (Esc ou o X).
/// </summary>
public sealed class MessageWindow : Window
{
    public int Result { get; private set; } = -1;

    /// <param name="buttons">Rótulos em pt-BR (traduzidos ao mostrar).</param>
    /// <param name="defaultIndex">Botão do Enter (em destaque).</param>
    /// <param name="cancelIndex">Botão equivalente a Esc/fechar (-1: nenhum).</param>
    public MessageWindow(string title, string message, MessageKind kind, IReadOnlyList<string> buttons, int defaultIndex = 0, int cancelIndex = -1)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Largura mínima no conteúdo: com SizeToContent, o MinWidth da janela não vale em todas as plataformas.
        MaxWidth = 560;
        this[!BackgroundProperty] = this.GetResourceObservable("Med.WindowBackground").ToBinding();

        var text = new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
        };
        AutomationProperties.SetName(text, message);

        var body = new DockPanel { Margin = new Thickness(20, 20, 20, 12) };
        if (Glyph(kind) is { } glyph)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontSize = 28,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Classes = { "med-icon" },
            };
            if (kind == MessageKind.Error) icon[!TextBlock.ForegroundProperty] = this.GetResourceObservable("Med.Danger").ToBinding();
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }
        body.Children.Add(text);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 88, IsDefault = i == defaultIndex, IsCancel = i == cancelIndex };
            if (i == defaultIndex) button.Classes.Add("med-accent");
            AutomationProperties.SetName(button, buttons[i]);
            button.Click += (_, _) =>
            {
                Result = index;
                Close(index != cancelIndex);
            };
            row.Children.Add(button);
        }
        var footer = new Border { Padding = new Thickness(20, 12, 20, 16), Child = row };
        footer[!Border.BackgroundProperty] = this.GetResourceObservable("Med.Panel").ToBinding();

        var root = new DockPanel { MinWidth = 360 };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        // Texto longo rola, com os botões sempre à vista.
        root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && cancelIndex < 0)
            {
                e.Handled = true;
                Close(false);
            }
        };
    }

    private static string? Glyph(MessageKind kind) => kind switch
    {
        MessageKind.Warning or MessageKind.Error => "",
        _ => null,
    };
}
