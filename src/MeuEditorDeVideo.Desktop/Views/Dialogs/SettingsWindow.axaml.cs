using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Atalhos de uma categoria (o ItemsControl do Avalonia não agrupa).</summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutItemViewModel> Items);

/// <summary>Janela modal de configurações (RF208–RF214).</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow() { InitializeComponent(); }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        viewModel.CloseRequested += (_, saved) => Close(saved);
        viewModel.OpenModelManagerRequested += (_, _) =>
        {
            var vm = ActivatorUtilities.CreateInstance<ModelManagerViewModel>(App.Services);
            DialogShow.Modal(new ModelManagerWindow(vm), this);
            viewModel.ReloadAiDevice();
        };
        viewModel.Shortcuts.CollectionChanged += (_, _) => RebuildShortcutGroups();
        RebuildShortcutGroups();
        // O campo do atalho captura a combinação pressionada: o evento é interceptado na descida, antes do TextBox.
        AddHandler(KeyDownEvent, Atalho_PreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            await viewModel.RefreshHardwareAsync();
            await viewModel.RefreshCacheAsync();
        };
    }

    public SettingsViewModel? ViewModel { get; }

    /// <summary>Abre as configurações sobre <paramref name="owner"/>; devolve true se o usuário salvou.</summary>
    public static bool ShowFor(Window? owner)
    {
        var vm = ActivatorUtilities.CreateInstance<SettingsViewModel>(App.Services);
        var window = new SettingsWindow(vm);
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var before = Translator.Language;
        var saved = DialogShow.Modal(window, owner) == true;
        // O idioma escolhido vale na hora: o programa reinicia nele (com o mesmo projeto, se estiver salvo).
        if (saved && !string.Equals(vm.Language.Value, before, StringComparison.OrdinalIgnoreCase))
            ((App)Avalonia.Application.Current!).RestartForLanguage();
        return saved;
    }

    private void RebuildShortcutGroups()
    {
        if (ViewModel is null) return;
        ListaAtalhos.ItemsSource = ViewModel.Shortcuts.GroupBy(s => s.Category)
            .Select(g => new ShortcutGroup(Translator.T(g.Key), g.ToList())).ToList();
    }

    /// <summary>Captura a combinação pressionada no campo do atalho.</summary>
    private void Atalho_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: ShortcutItemViewModel item }) return;
        if (e.Key == Key.Tab) return; // navegação entre campos
        e.Handled = true;
        if (SettingsViewModel.Capture(e.Key.ToMed(), e.KeyModifiers.ToMed()) is { } chord) item.Assign(chord);
    }
}
