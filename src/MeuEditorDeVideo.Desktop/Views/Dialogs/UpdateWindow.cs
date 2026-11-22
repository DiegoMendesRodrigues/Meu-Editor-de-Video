using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Desktop.Services;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>
/// Resultado de Ajuda › Verificar atualizações (e da verificação mensal ao abrir): "versão mais recente" ou os dados da
/// nova versão, com o link da Microsoft Store (só no Windows) e o do site. Os textos já são montados traduzidos.
/// </summary>
public sealed class UpdateWindow : Window
{
    private UpdateWindow(string title, Control message, string messageName)
    {
        DialogParts.Setup(this, title, 560);
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetName(message, messageName);

        var closeText = Translator.T("Fechar essa janela");
        var close = DialogParts.Accent(closeText, 140);
        close.IsCancel = true;
        close.Margin = new Thickness(0);
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(close);

        var root = new StackPanel { Margin = new Thickness(20) };
        // Espaço vertical antes e depois do texto.
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(message);
        root.Children.Add(new Border { Height = 20 });
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>Mostra que a versão instalada é a mais recente.</summary>
    public static void ShowUpToDate(Window? owner) => Show(owner, CreateUpToDate());

    /// <summary>Janela "versão mais recente" (sem mostrar).</summary>
    public static UpdateWindow CreateUpToDate()
    {
        var message = Translator.T("Você está com a versão mais recente!");
        var text = new TextBlock { Text = message, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        return new UpdateWindow(Translator.T("Verificar atualizações"), text, message);
    }

    /// <summary>Mostra os dados da nova versão.</summary>
    public static void ShowNewVersion(Window? owner, UpdateInfo info) => Show(owner, CreateNewVersion(info));

    /// <summary>Janela com os dados da nova versão (sem mostrar).</summary>
    public static UpdateWindow CreateNewVersion(UpdateInfo info)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
        var lines = text.Inlines!;
        // A versão ({0}) e o nome do programa ({1}) ficam em negrito.
        AddWithBold(lines, Translator.T("A nova versão {0} do {1} está disponível para você poder atualizar a que está utilizando."),
            info.Version, "Meu Editor de Vídeos");
        lines.Add(new LineBreak());
        lines.Add(new LineBreak()); // linha em branco
        lines.Add(new Run(Translator.F("Os idiomas disponíveis são: {0}.", info.Languages)));
        lines.Add(new LineBreak());
        lines.Add(new Run(Translator.F("Sistemas operacionais suportados: {0}.", info.OperatingSystem)));
        lines.Add(new LineBreak());
        lines.Add(new Run(Translator.F("Autor: {0}.", info.Author)));

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(text);
        // A Microsoft Store existe só no Windows; no Linux e no macOS o servidor responde com o link do site para o sistema.
        if (UpdateEndpoints.ShowsMicrosoftStoreLink)
            AddLink(panel, Translator.T("Link para download na Microsoft Store:"), info.MicrosoftStoreLink);
        AddLink(panel, Translator.T("Link para download no site AplicativosIA.Online:"), info.InstallLink);
        var name = string.Concat(text.Inlines!.Select(i => i is Run r ? r.Text : i is Bold b ? string.Concat(b.Inlines.OfType<Run>().Select(x => x.Text)) : " "));
        return new UpdateWindow(Translator.T("Nova versão disponível"), panel, name);
    }

    private static void Show(Window? owner, UpdateWindow window)
    {
        if (owner is { IsVisible: true })
        {
            ModalLoop.Run(window.ShowDialog(owner));
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DialogHost.ShowModal(window);
    }

    /// <summary>Monta o texto trocando {0} e {1} por trechos em negrito (a ordem pode mudar na tradução).</summary>
    internal static void AddWithBold(InlineCollection inlines, string template, string first, string second)
    {
        var rest = template;
        while (rest.Length > 0)
        {
            var i0 = rest.IndexOf("{0}", StringComparison.Ordinal);
            var i1 = rest.IndexOf("{1}", StringComparison.Ordinal);
            var next = i0 < 0 ? i1 : i1 < 0 ? i0 : Math.Min(i0, i1);
            if (next < 0)
            {
                inlines.Add(new Run(rest));
                break;
            }
            if (next > 0) inlines.Add(new Run(rest[..next]));
            inlines.Add(new Bold { Inlines = { new Run(next == i0 ? first : second) } });
            rest = rest[(next + 3)..];
        }
    }

    /// <summary>Rótulo numa linha e o link na linha de baixo (só http/https vira link; outro texto aparece como veio).</summary>
    private static void AddLink(Panel panel, string label, string? url)
    {
        var caption = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 22, 0, 0) };
        caption[!TextBlock.ForegroundProperty] = panel.GetResourceObservable("Med.Text").ToBinding();
        panel.Children.Add(caption);
        if (string.IsNullOrWhiteSpace(url))
        {
            panel.Children.Add(new TextBlock { Text = "—" });
            return;
        }
        if (UpdateEndpoints.AsLink(url) is not { } uri)
        {
            panel.Children.Add(new TextBlock { Text = url, TextWrapping = TextWrapping.Wrap });
            return;
        }
        var link = new HyperlinkButton { Content = url, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(link, label + " " + url);
        // Abre no navegador padrão pelo sistema; o programa em si não acessa o link.
        link.Click += (_, e) =>
        {
            SystemShell.Open(uri.AbsoluteUri);
            e.Handled = true;
        };
        panel.Children.Add(link);
    }
}
