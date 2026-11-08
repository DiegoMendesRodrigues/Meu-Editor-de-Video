using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Projects;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Item da lista de projetos recentes.</summary>
public sealed record RecentProjectItem(string Name, string FullPath, bool Exists);

/// <summary>Tela inicial (§5.1).</summary>
public sealed partial class HomeViewModel(
    ISettingsService settings,
    IProjectService projects,
    IProjectWorkflow workflow,
    IDialogService dialogs) : PageViewModel
{
    public string Version => AppInfo.Version;

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Projeto mais recente que pode ser recuperado após encerramento inesperado (RF011).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableProject), nameof(RecoverableText))]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand), nameof(DiscardRecoveryCommand))]
    public partial RecoverableProject? Recoverable { get; set; }

    public bool HasRecoverableProject => Recoverable is not null;

    public string RecoverableText => Recoverable is { } r
        ? $"O projeto \"{r.Name}\" não foi fechado corretamente. Há um salvamento automático de {r.SavedAt.LocalDateTime:dd/MM/yyyy 'às' HH:mm}."
        : "";

    public override void OnNavigatedTo()
    {
        RecentProjects.Clear();
        foreach (var path in settings.Current.RecentProjects)
            RecentProjects.Add(new RecentProjectItem(Path.GetFileNameWithoutExtension(path), path, File.Exists(path)));
        OnPropertyChanged(nameof(HasRecentProjects));

        Recoverable = projects.FindRecoverable().FirstOrDefault();
    }

    [RelayCommand]
    private Task NewProject() => workflow.NewProjectAsync();

    [RelayCommand]
    private Task OpenProject() => workflow.OpenAsync();

    [RelayCommand]
    private async Task OpenRecent(RecentProjectItem? item)
    {
        if (item is null) return;
        if (!await workflow.OpenAsync(item.FullPath)) OnNavigatedTo(); // atualiza a lista (arquivo pode ter sumido)
    }

    [RelayCommand(CanExecute = nameof(HasRecoverableProject))]
    private async Task Recover()
    {
        if (Recoverable is { } r) await workflow.RecoverAsync(r);
    }

    [RelayCommand(CanExecute = nameof(HasRecoverableProject))]
    private void DiscardRecovery()
    {
        if (Recoverable is not { } r) return;
        if (!dialogs.Confirm("Descartar recuperação", $"Descartar as alterações recuperáveis de \"{r.Name}\"? Esta ação não pode ser desfeita."))
            return;
        projects.DiscardRecoverable(r);
        Recoverable = projects.FindRecoverable().FirstOrDefault();
    }

    // F11: tela de configurações.
    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings()
    {
    }

    private static bool CanOpenSettings() => false;
}
