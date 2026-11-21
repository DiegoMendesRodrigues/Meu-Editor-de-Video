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
        Show(owner, new InfoDocumentWindow("Atalhos de teclado", HelpDocuments.ShortcutsMarkdown(ShortcutCatalog.All, shortcuts.ChordsFor)));
    }

    /// <summary>Ajuda › Informações do sistema (para pedir suporte; nada é enviado).</summary>
    public static void ShowSystemInfo(Window? owner)
    {
        var services = App.Services;
        var report = SystemInfoReport.Build(AppInfo.Version, services.GetRequiredService<IHardwareInfoService>().Get(refresh: true),
            services.GetRequiredService<IModelRegistry>().Installed, services.GetRequiredService<IAppPaths>());
        var window = new InfoDocumentWindow("Informações do sistema", HelpDocuments.SystemInfoMarkdown(report), report);
        window.Status.Text = "Estas informações ficam só neste computador. Copie-as se precisar pedir ajuda.";
        Show(owner, window);
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
