using System.Windows;
using MeuEditorDeVideo.Core.Errors;
using Microsoft.Win32;

namespace MeuEditorDeVideo.App.Services;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

public interface IDialogService
{
    void ShowError(MedError error);

    void ShowInfo(string title, string message);

    bool Confirm(string title, string message);

    /// <summary>"Deseja salvar as alterações?" — Sim / Não / Cancelar.</summary>
    UnsavedChoice AskSaveChanges(string projectName);

    string? PickOpenProject(string? initialDirectory);

    string? PickSaveProject(string suggestedName, string? initialDirectory);

    string? PickFile(string title, string filter, string? initialDirectory);

    /// <summary>Escolhe onde salvar um arquivo genérico (ex.: captura de quadro). Null se cancelado.</summary>
    string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory);

    string? PickFolder(string title, string? initialDirectory);

    /// <summary>Seleção de vários arquivos (RF026). Lista vazia se cancelado.</summary>
    IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory);

    /// <summary>Pede um texto ao usuário. Retorna null se cancelado.</summary>
    string? Prompt(string title, string label, string initialValue);

    /// <summary>Remover projeto recente: só da lista, também o arquivo, ou cancelar.</summary>
    RecentRemovalChoice AskRemoveRecent(string projectName, string path, bool fileExists);

    /// <summary>Abre a janela de configurações. Retorna true se o usuário salvou.</summary>
    bool ShowSettings();

    /// <summary>Mostra uma janela modal sobre a janela principal. Retorna DialogResult.</summary>
    bool? ShowModal(Window dialog);
}

public sealed class DialogService : IDialogService
{
    private const string ProjectFilter = "Projeto do Meu Editor de Vídeo (*.med)|*.med";

    private static Window? Owner => System.Windows.Application.Current?.MainWindow;

    public void ShowError(MedError error) =>
        MessageBox.Show(Owner!, $"{error.Message}\n\nCódigo: {error.Code}", AppInfo.ProductName,
            MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public UnsavedChoice AskSaveChanges(string projectName) =>
        MessageBox.Show(Owner!, $"Deseja salvar as alterações em \"{projectName}\"?", AppInfo.ProductName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => UnsavedChoice.Save,
            MessageBoxResult.No => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };

    public string? PickOpenProject(string? initialDirectory)
    {
        var dlg = new OpenFileDialog { Title = "Abrir projeto", Filter = ProjectFilter, InitialDirectory = initialDirectory ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickSaveProject(string suggestedName, string? initialDirectory)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Salvar projeto como",
            Filter = ProjectFilter,
            DefaultExt = ".med",
            AddExtension = true,
            FileName = suggestedName,
            InitialDirectory = initialDirectory ?? "",
            OverwritePrompt = true,
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedName,
            AddExtension = true,
            InitialDirectory = initialDirectory ?? "",
            OverwritePrompt = true,
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFile(string title, string filter, string? initialDirectory)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, InitialDirectory = initialDirectory ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title, string? initialDirectory)
    {
        var dlg = new OpenFolderDialog { Title = title, InitialDirectory = initialDirectory ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true, InitialDirectory = initialDirectory ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : [];
    }

    public string? Prompt(string title, string label, string initialValue)
    {
        var dialog = new Views.Dialogs.PromptWindow(title, label, initialValue);
        return ShowModal(dialog) == true ? dialog.Value : null;
    }

    public RecentRemovalChoice AskRemoveRecent(string projectName, string path, bool fileExists)
    {
        var dialog = new Views.Dialogs.RecentRemovalWindow(projectName, path, fileExists);
        return ShowModal(dialog) == true ? dialog.Choice : RecentRemovalChoice.Cancel;
    }

    public bool ShowSettings() => Views.Dialogs.SettingsWindow.ShowFor(Owner);

    public bool? ShowModal(Window dialog)
    {
        dialog.Owner = Owner;
        return dialog.ShowDialog();
    }
}
