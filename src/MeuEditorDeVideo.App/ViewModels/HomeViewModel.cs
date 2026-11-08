using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Item da lista de projetos recentes.</summary>
public sealed record RecentProjectItem(string Name, string FullPath, bool Exists);

/// <summary>Tela inicial (§5.1).</summary>
public sealed partial class HomeViewModel(INavigationService navigation, ISettingsService settings) : PageViewModel
{
    public string Version => AppInfo.Version;

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Exibido quando houver projeto a recuperar (implementado na F1).</summary>
    public bool HasRecoverableProject => false;

    public override void OnNavigatedTo()
    {
        RecentProjects.Clear();
        foreach (var path in settings.Current.RecentProjects)
            RecentProjects.Add(new RecentProjectItem(Path.GetFileNameWithoutExtension(path), path, File.Exists(path)));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    [RelayCommand]
    private void NewProject() => navigation.NavigateTo<EditorViewModel>();

    // F1: abrir projeto .med. Desabilitado até existir implementação real (§17: nada simulado).
    [RelayCommand(CanExecute = nameof(CanOpenProject))]
    private void OpenProject()
    {
    }

    private static bool CanOpenProject() => false;

    [RelayCommand(CanExecute = nameof(CanOpenRecent))]
    private void OpenRecent(RecentProjectItem? item)
    {
    }

    private static bool CanOpenRecent(RecentProjectItem? item) => false;

    // F11: tela de configurações.
    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings()
    {
    }

    private static bool CanOpenSettings() => false;
}
