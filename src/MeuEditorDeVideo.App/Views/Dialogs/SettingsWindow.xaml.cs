using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeuEditorDeVideo.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela modal de configurações (RF208–RF214).</summary>
public partial class SettingsWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, saved) =>
        {
            DialogResult = saved;
        };
        viewModel.OpenModelManagerRequested += (_, _) =>
        {
            var vm = ActivatorUtilities.CreateInstance<ModelManagerViewModel>(App.Services);
            var window = new ModelManagerWindow(vm) { Owner = this };
            window.ShowDialog();
            viewModel.ReloadAiDevice();
        };
        Loaded += async (_, _) =>
        {
            await viewModel.RefreshHardwareAsync();
            await viewModel.RefreshCacheAsync();
        };
    }

    /// <summary>Abre as configurações sobre <paramref name="owner"/>; devolve true se o usuário salvou.</summary>
    public static bool ShowFor(Window? owner)
    {
        var vm = ActivatorUtilities.CreateInstance<SettingsViewModel>(App.Services);
        var window = new SettingsWindow(vm) { Owner = owner };
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true;
    }

    /// <summary>Captura a combinação pressionada no campo do atalho.</summary>
    private void Atalho_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ShortcutItemViewModel item }) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return; // navegação entre campos
        e.Handled = true;
        if (SettingsViewModel.Capture(key, Keyboard.Modifiers) is { } chord) item.Assign(chord);
    }
}
