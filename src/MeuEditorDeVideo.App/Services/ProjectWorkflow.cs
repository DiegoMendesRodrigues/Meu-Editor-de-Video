using System.IO;
using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.App.Views.Dialogs;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.Services;

/// <summary>
/// Fluxos de projeto com interação do usuário (criar, abrir, salvar, recuperar, fechar).
/// Concentra confirmações e mensagens de erro para que as telas fiquem simples.
/// </summary>
public interface IProjectWorkflow
{
    Task<bool> NewProjectAsync();
    Task<bool> OpenAsync(string? path = null);
    Task<bool> RecoverAsync(RecoverableProject recoverable);
    Task<bool> SaveAsync();
    Task<bool> SaveAsAsync();
    Task<bool> PackageMediaAsync();
    Task CreateBackupAsync();

    /// <summary>Pergunta sobre alterações não salvas. Retorna true se pode prosseguir (fechar/trocar).</summary>
    Task<bool> ConfirmDiscardOrSaveAsync();

    Task<bool> CloseProjectAsync();

    void ShowMissingMedia();
}

public sealed class ProjectWorkflow(
    IProjectService projects,
    IDialogService dialogs,
    INavigationService navigation,
    ISettingsService settings,
    IServiceProvider services,
    ILogger<ProjectWorkflow> logger) : IProjectWorkflow
{
    public async Task<bool> NewProjectAsync()
    {
        if (!await ConfirmDiscardOrSaveAsync()) return false;

        var vm = services.GetRequiredService<NewProjectViewModel>();
        if (dialogs.ShowModal(new NewProjectWindow(vm)) != true || vm.Result is null) return false;

        if (!await TryAsync(() => projects.CreateAsync(vm.Result))) return false;
        await RememberDirectoryAsync(vm.Result.Directory);
        navigation.NavigateTo<EditorViewModel>();
        return true;
    }

    public async Task<bool> OpenAsync(string? path = null)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return false;

        path ??= dialogs.PickOpenProject(settings.Current.DefaultProjectsDirectory);
        if (path is null) return false;

        if (!File.Exists(path))
        {
            dialogs.ShowError(MedErrors.ProjectFileNotFound(path));
            await RemoveRecentAsync(path);
            return false;
        }

        if (!await TryAsync(() => projects.OpenAsync(path))) return false;

        if (projects.LastOpenWarnings.Count > 0)
            dialogs.ShowInfo("Abrir projeto", string.Join("\n\n", projects.LastOpenWarnings));

        navigation.NavigateTo<EditorViewModel>();
        if (projects.Current?.HasMissingMedia == true) ShowMissingMedia();
        return true;
    }

    public async Task<bool> RecoverAsync(RecoverableProject recoverable)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return false;
        if (!await TryAsync(() => projects.OpenRecoveredAsync(recoverable))) return false;

        navigation.NavigateTo<EditorViewModel>();
        dialogs.ShowInfo("Projeto recuperado",
            $"O projeto \"{recoverable.Name}\" foi recuperado do salvamento automático de {recoverable.SavedAt.LocalDateTime:dd/MM/yyyy HH:mm}.\n\n" +
            "Salve o projeto para manter as alterações recuperadas.");
        if (projects.Current?.HasMissingMedia == true) ShowMissingMedia();
        return true;
    }

    public async Task<bool> SaveAsync()
    {
        var session = projects.Current;
        if (session is null) return false;
        if (session.FilePath is null || !Directory.Exists(Path.GetDirectoryName(session.FilePath)))
            return await SaveAsAsync();
        return await TryAsync(() => projects.SaveAsync());
    }

    public async Task<bool> SaveAsAsync()
    {
        var session = projects.Current;
        if (session is null) return false;
        var dir = session.FilePath is { } p ? Path.GetDirectoryName(p) : settings.Current.DefaultProjectsDirectory;
        var path = dialogs.PickSaveProject(session.Project.Name, dir);
        if (path is null) return false;
        return await TryAsync(() => projects.SaveAsAsync(path));
    }

    public async Task<bool> PackageMediaAsync()
    {
        if (projects.Current is not { } session) return false;
        if (!dialogs.Confirm("Empacotar mídias",
                "Todas as mídias serão copiadas para dentro do arquivo do projeto (.med), que poderá ser levado para outro computador.\n\n" +
                "O arquivo ficará maior. Deseja continuar?"))
            return false;

        if (!await TryAsync(() => projects.PackageMediaAsync())) return false;
        dialogs.ShowInfo("Empacotar mídias", $"{session.Project.Media.Count} mídia(s) empacotada(s) em:\n{session.FilePath}");
        return true;
    }

    public async Task CreateBackupAsync()
    {
        if (projects.Current is not { } session) return;
        if (session.IsDirty && !await SaveAsync()) return;

        string? backup = null;
        if (await TryAsync(async () => backup = await projects.CreateBackupAsync()) && backup is not null)
            dialogs.ShowInfo("Cópia de segurança", $"Cópia de segurança criada:\n{backup}");
    }

    public async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (projects.Current is not { IsDirty: true } session) return true;
        return dialogs.AskSaveChanges(session.Project.Name) switch
        {
            UnsavedChoice.Save => await SaveAsync(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    public async Task<bool> CloseProjectAsync()
    {
        if (!await ConfirmDiscardOrSaveAsync()) return false;
        await projects.CloseAsync();
        return true;
    }

    public void ShowMissingMedia()
    {
        var vm = services.GetRequiredService<MissingMediaViewModel>();
        dialogs.ShowModal(new MissingMediaWindow(vm));
    }

    private async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (MedException ex)
        {
            logger.LogWarning(ex, "Operação de projeto falhou: {Code}", ex.Error.Code);
            dialogs.ShowError(ex.Error);
            return false;
        }
    }

    private Task RememberDirectoryAsync(string directory) =>
        settings.Current.DefaultProjectsDirectory == directory
            ? Task.CompletedTask
            : settings.SaveAsync(settings.Current with { DefaultProjectsDirectory = directory });

    private Task RemoveRecentAsync(string path) =>
        settings.SaveAsync(settings.Current with
        {
            RecentProjects = settings.Current.RecentProjects.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList(),
        });
}
