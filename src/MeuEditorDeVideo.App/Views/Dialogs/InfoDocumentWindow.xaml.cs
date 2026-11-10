using System.Text;
using System.Windows;
using System.Windows.Automation;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.Services.Help;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela de leitura com um documento simples (Markdown da ajuda) e, opcionalmente, um texto para copiar.</summary>
public partial class InfoDocumentWindow
{
    private readonly string? _copyText;

    public InfoDocumentWindow(string title, string markdown, string? copyText = null)
    {
        InitializeComponent();
        Title = title;
        AutomationProperties.SetName(this, title);
        Visualizador.Document = MarkdownFlowDocument.Build(MarkdownDocument.Parse(markdown), out _);
        _copyText = copyText;
        if (copyText is not null) Copiar.Visibility = Visibility.Visible;
    }

    private static void Show(Window? owner, InfoDocumentWindow window)
    {
        window.Owner = owner;
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }

    /// <summary>Ajuda › Atalhos de teclado: os atalhos em uso (inclusive os personalizados em Configurações › Atalhos).</summary>
    public static void ShowShortcuts(Window? owner)
    {
        var shortcuts = App.Services.GetRequiredService<ShortcutService>();
        Show(owner, new InfoDocumentWindow("Atalhos de teclado", ShortcutsMarkdown(ShortcutCatalog.All, shortcuts.ChordsFor)));
    }

    /// <summary>Tabela de atalhos por categoria; uma ação sem atalho aparece como "—".</summary>
    public static string ShortcutsMarkdown(IEnumerable<ShortcutAction> actions, Func<string, IReadOnlyList<KeyChord>> chords)
    {
        var sb = new StringBuilder("# Atalhos de teclado\n\n> Para mudar um atalho, use **Ferramentas › Configurações… › Atalhos**.\n");
        foreach (var group in actions.GroupBy(a => a.Category))
        {
            sb.Append("\n## ").Append(group.Key).Append("\n\n| Ação | Atalho |\n|---|---|\n");
            foreach (var action in group)
            {
                var keys = chords(action.Id).Select(c => c.Display).ToList();
                sb.Append("| ").Append(action.Label).Append(" | ").Append(keys.Count > 0 ? string.Join(" ou ", keys) : "—").Append(" |\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>Ajuda › Informações do sistema (para pedir suporte; nada é enviado).</summary>
    public static void ShowSystemInfo(Window? owner)
    {
        var services = App.Services;
        var report = SystemInfoReport.Build(AppInfo.Version, services.GetRequiredService<IHardwareInfoService>().Get(refresh: true),
            services.GetRequiredService<IModelRegistry>().Installed, services.GetRequiredService<IAppPaths>());
        var window = new InfoDocumentWindow("Informações do sistema", SystemInfoMarkdown(report), report);
        window.Status.Text = "Estas informações ficam só neste computador. Copie-as se precisar pedir ajuda.";
        Show(owner, window);
    }

    /// <summary>Converte o relatório ("Rótulo: valor" por linha) numa tabela; listas viram itens.</summary>
    public static string SystemInfoMarkdown(string report)
    {
        var sb = new StringBuilder("# Informações do sistema\n\n");
        var inTable = false;
        foreach (var raw in report.Replace("\r\n", "\n").Split('\n').Skip(1))
        {
            var line = raw.TrimEnd();
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (inTable) { sb.Append('\n'); inTable = false; }
                sb.Append("- ").Append(line[4..]).Append('\n');
            }
            else if (colon > 0)
            {
                if (!inTable) { sb.Append("\n| Item | Valor |\n|---|---|\n"); inTable = true; }
                sb.Append("| ").Append(line[..colon]).Append(" | ").Append(line[(colon + 2)..].Replace("|", "/")).Append(" |\n");
            }
            else if (line.Length > 0)
            {
                if (inTable) { sb.Append('\n'); inTable = false; }
                sb.Append("\n**").Append(line.TrimEnd(':')).Append("**\n\n");
            }
            else if (inTable)
            {
                sb.Append('\n');
                inTable = false;
            }
        }
        return sb.ToString();
    }

    private void Copiar_Click(object sender, RoutedEventArgs e)
    {
        if (_copyText is null) return;
        try
        {
            Clipboard.SetText(_copyText);
            Status.Text = "Copiado para a área de transferência.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Status.Text = "A área de transferência está em uso. Tente novamente.";
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
