using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using MeuEditorDeVideo.App.Localization;
using MeuEditorDeVideo.Core.Updates;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>
/// Resultado de Ajuda › Verificar atualizações (e da verificação mensal ao abrir): "versão mais recente" ou os dados da
/// nova versão, com links para a Microsoft Store e para o site. Os textos já são montados traduzidos.
/// </summary>
public sealed class UpdateWindow : Window
{
    private UpdateWindow(string title, TextBlock message)
    {
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.WindowBackground");
        AutomationProperties.SetName(this, title);

        message.TextWrapping = TextWrapping.Wrap;
        message.LineHeight = 22;
        message.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");
        AutomationProperties.SetName(message, new TextRange(message.ContentStart, message.ContentEnd).Text);

        var closeText = Translator.T("Fechar essa janela");
        var close = new Button { Content = closeText, IsDefault = true, IsCancel = true, MinWidth = 140 };
        close.SetResourceReference(StyleProperty, "Med.AccentButton");
        AutomationProperties.SetName(close, closeText);
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
    public static void ShowUpToDate(Window? owner)
    {
        var text = new TextBlock(new Run(Translator.T("Você está com a versão mais recente!"))) { FontSize = 15 };
        Show(owner, Translator.T("Verificar atualizações"), text);
    }

    /// <summary>Mostra os dados da nova versão.</summary>
    public static void ShowNewVersion(Window? owner, UpdateInfo info)
    {
        var text = new TextBlock();
        var lines = text.Inlines;
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
        lines.Add(new LineBreak());
        lines.Add(new LineBreak()); // linha em branco
        AddLink(lines, Translator.T("Link para download na Microsoft Store:"), info.MicrosoftStoreLink);
        lines.Add(new LineBreak());
        lines.Add(new LineBreak()); // linha em branco
        AddLink(lines, Translator.T("Link para download no site AplicativosIA.Online:"), info.InstallLink);
        Show(owner, Translator.T("Nova versão disponível"), text);
    }

    private static void Show(Window? owner, string title, TextBlock text)
    {
        var window = new UpdateWindow(title, text);
        if (owner is { IsLoaded: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
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
            inlines.Add(new Bold(new Run(next == i0 ? first : second)));
            rest = rest[(next + 3)..];
        }
    }

    private static void AddLink(InlineCollection inlines, string label, string? url)
    {
        // Rótulo numa linha e o link na linha de baixo.
        inlines.Add(new Run(label));
        inlines.Add(new LineBreak());
        if (string.IsNullOrWhiteSpace(url))
        {
            inlines.Add(new Run("—"));
            return;
        }
        // Só http/https vira link; outro texto aparece como veio.
        if (UpdateEndpoints.AsLink(url) is not { } uri)
        {
            inlines.Add(new Run(url));
            return;
        }
        var link = new Hyperlink(new Run(url)) { NavigateUri = uri };
        AutomationProperties.SetName(link, label + " " + url);
        // Abre no navegador padrão; o programa em si não acessa o link.
        link.RequestNavigate += (_, e) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        inlines.Add(link);
    }
}
