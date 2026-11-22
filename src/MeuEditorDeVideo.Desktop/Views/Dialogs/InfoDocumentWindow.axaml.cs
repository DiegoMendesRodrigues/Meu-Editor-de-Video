using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Desktop.Services.Help;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Janela de leitura com um documento simples (Markdown da ajuda) e, opcionalmente, um texto para copiar.</summary>
public partial class InfoDocumentWindow : Window
{
    private readonly string? _copyText;

    public InfoDocumentWindow() : this("Informações", "") { }

    public InfoDocumentWindow(string title, string markdown, string? copyText = null)
    {
        InitializeComponent();
        Title = title;
        AutomationProperties.SetName(this, title);
        Visualizador.Content = MarkdownControls.Build(MarkdownDocument.Parse(markdown)).Root;
        _copyText = copyText;
        if (copyText is not null) Copiar.IsVisible = true;
    }

    /// <summary>Texto da barra de estado (testes).</summary>
    public string? StatusText => Status.Text;

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
        window.Status.Text = Translator.T("Estas informações ficam só neste computador. Copie-as se precisar pedir ajuda.");
        Show(owner, window);
    }

    private static void Show(Window? owner, InfoDocumentWindow window)
    {
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DialogShow.Modal(window, owner);
    }

    private async void Copiar_Click(object? sender, RoutedEventArgs e)
    {
        if (_copyText is null) return;
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Sem área de transferência.");
            await clipboard.SetTextAsync(_copyText);
            Status.Text = Translator.T("Copiado para a área de transferência.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            Status.Text = Translator.T("A área de transferência está em uso. Tente novamente.");
        }
    }

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();
}
