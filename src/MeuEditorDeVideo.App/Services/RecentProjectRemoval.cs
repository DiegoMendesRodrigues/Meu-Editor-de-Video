using System.IO;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Projects;

namespace MeuEditorDeVideo.App.Services;

/// <summary>O que fazer com um item da lista de projetos recentes.</summary>
public enum RecentRemovalChoice
{
    Cancel,

    /// <summary>Tira o projeto da lista; o arquivo continua no disco.</summary>
    RemoveFromList,

    /// <summary>Tira da lista e manda o arquivo do projeto (.med) para a Lixeira.</summary>
    DeleteFiles,
}

/// <summary>Exclusão de arquivos pela Lixeira do Windows (recuperável).</summary>
public interface IFileRecycler
{
    void Recycle(string path);
}

public sealed class RecycleBinFileRecycler : IFileRecycler
{
    public void Recycle(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
}

/// <summary>
/// Remove um projeto da lista de recentes da tela inicial e, se o usuário pedir, exclui o arquivo do projeto.
/// Só o .med vai para a Lixeira: as mídias originais vinculadas nunca são apagadas.
/// </summary>
public sealed class RecentProjectRemoval(
    ISettingsService settings,
    IDialogService dialogs,
    IAutosaveStore autosave,
    IFileRecycler recycler)
{
    /// <summary>Pergunta e executa. Retorna true se a lista mudou.</summary>
    /// <param name="openProjectPath">Projeto aberto no momento (não pode ser excluído).</param>
    public async Task<bool> RemoveAsync(string path, string? openProjectPath = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var exists = File.Exists(path);
        var choice = dialogs.AskRemoveRecent(name, path, exists);
        if (choice == RecentRemovalChoice.Cancel) return false;

        if (choice == RecentRemovalChoice.DeleteFiles && exists)
        {
            if (openProjectPath is not null && SamePath(openProjectPath, path))
            {
                dialogs.ShowInfo("Excluir projeto", $"O projeto \"{name}\" está aberto. Feche-o antes de excluir o arquivo.");
                return false;
            }
            try
            {
                recycler.Recycle(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                dialogs.ShowInfo("Excluir projeto",
                    $"Não foi possível excluir \"{path}\". O projeto continua na lista.\n\n{ex.Message}");
                return false;
            }
            foreach (var r in autosave.FindRecoverable().Where(r => r.OriginalPath is { } p && SamePath(p, path)))
                autosave.Discard(r.ProjectId);
        }

        await settings.SaveAsync(settings.Current with
        {
            RecentProjects = settings.Current.RecentProjects.Where(p => !SamePath(p, path)).ToList(),
        });
        return true;
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
