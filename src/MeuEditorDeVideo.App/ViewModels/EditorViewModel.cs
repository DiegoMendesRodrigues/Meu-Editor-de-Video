using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Seções do painel A (biblioteca), conforme protótipo.</summary>
public enum LibrarySection
{
    Media,
    Videos,
    Images,
    Audio,
    Graphics,
    Texts,
    Transitions,
    Effects,
    Color,
    AI,
}

/// <summary>
/// Tela principal de edição (§5.3). Na F0 apenas a estrutura dos painéis A–E existe;
/// os comandos ficam desabilitados até a fase que os implementa.
/// </summary>
public sealed partial class EditorViewModel(INavigationService navigation, IThemeService themes, ISettingsService settings)
    : PageViewModel
{
    [ObservableProperty]
    public partial string ProjectName { get; set; } = "Sem título";

    [ObservableProperty]
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Media;

    /// <summary>Texto do tempo atual / duração exibido no monitor.</summary>
    [ObservableProperty]
    public partial string TimeDisplay { get; set; } = "00:00:00:00 / 00:00:00:00";

    public bool IsDarkTheme => themes.Current != AppTheme.Light;

    [RelayCommand]
    private void GoHome() => navigation.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void SelectSection(LibrarySection section) => SelectedSection = section;

    [RelayCommand]
    private async Task ToggleTheme()
    {
        var next = IsDarkTheme ? AppTheme.Light : AppTheme.Dark;
        themes.Apply(next);
        await settings.SaveAsync(settings.Current with { Theme = next });
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    // Comandos das fases seguintes — desabilitados até existirem de verdade.
    private static bool NotYetAvailable() => false;

    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void Save() { }
    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void Open() { }
    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void Undo() { }
    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void Redo() { }
    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void ImportMedia() { }
    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void Export() { }
    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void PlayPause() { }
}
