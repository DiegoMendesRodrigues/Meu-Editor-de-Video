using L = MeuEditorDeVideo.Presentation.Localization.Translator;
using System.Windows;
using MeuEditorDeVideo.Core.Errors;
using Microsoft.Win32;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Export;
using MeuEditorDeVideo.App.Views.Dialogs;

namespace MeuEditorDeVideo.App.Services;

/// <summary>Diálogos do WPF; cada janela pedida pelos ViewModels abre sobre a janela principal.</summary>
public sealed class DialogService : IDialogService
{
    private const string ProjectFilter = "Projeto do Meu Editor de Vídeo (*.med)|*.med";

    private static Window? Owner => System.Windows.Application.Current?.MainWindow;

    public void ShowError(MedError error) =>
        MessageBox.Show(Owner!, $"{L.T(error.Message)}\n\n{L.T("Código:")} {error.Code}", AppInfo.ProductName,
            MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(Owner!, L.T(message), L.T(title), MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, L.T(message), L.T(title), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public UnsavedChoice AskSaveChanges(string projectName) =>
        MessageBox.Show(Owner!, L.F("Deseja salvar as alterações em \"{0}\"?", projectName), AppInfo.ProductName,
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

    public bool ShowSettings() => Views.Dialogs.SettingsWindow.ShowFor(Owner);

    public int? Choose(string title, string label, IReadOnlyList<string> options, int selected = 0, string okText = "OK")
    {
        var window = new ChoiceWindow(title, label, options, selected, okText);
        return ShowModal(window) == true && window.SelectedIndex >= 0 ? window.SelectedIndex : null;
    }

    public bool ShowNewProject(NewProjectViewModel viewModel) => ShowModal(new NewProjectWindow(viewModel)) == true;

    public void ShowMissingMedia(MissingMediaViewModel viewModel) => ShowModal(new MissingMediaWindow(viewModel));

    public bool ManageContentPacks(IContentPackStore store)
    {
        var window = new ContentPacksWindow(store, this);
        ShowModal(window);
        return window.Changed;
    }

    public ScreenRecordingResult? RecordScreen(IScreenRecorder recorder, string outputPath)
    {
        var window = new ScreenRecordWindow(recorder, outputPath);
        return ShowModal(window) == true && window.RecordedPath is { } path ? new ScreenRecordingResult(path, window.InsertOnTimeline) : null;
    }

    public bool ShowExport(ExportViewModel viewModel) => ShowModal(new ExportWindow(viewModel)) == true;

    public RenderJob? ShowExportProgress(RenderQueueViewModel queue)
    {
        var window = new RenderQueueWindow(queue) { Title = L.T("Exportando…") };
        RenderJob? finished = null;
        void OnFinished(object? sender, RenderJob job)
        {
            if (!window.IsVisible) return;
            finished = job;
            window.Close();
        }
        queue.FollowedJobFinished += OnFinished;
        try
        {
            ShowModal(window);
        }
        finally
        {
            queue.FollowedJobFinished -= OnFinished;
        }
        return finished;
    }

    private RenderQueueWindow? _queueWindow;

    public void ShowRenderQueue(Func<RenderQueueViewModel> create)
    {
        if (_queueWindow is { IsLoaded: true })
        {
            _queueWindow.Activate();
            return;
        }
        _queueWindow = new RenderQueueWindow(create()) { Owner = Owner };
        _queueWindow.Closed += (_, _) => _queueWindow = null;
        _queueWindow.Show();
    }

    public ReframeChoice? AskReframe(string currentSize, bool trackingAvailable)
    {
        var window = new ReframeWindow(currentSize, trackingAvailable);
        if (ShowModal(window) != true) return null;
        var (w, h) = window.Format;
        return new ReframeChoice(w, h, window.FollowHead);
    }

    public TextToSpeechRequest? AskTextToSpeech(IReadOnlyList<string> voices)
    {
        var window = new TextToSpeechWindow(voices);
        return ShowModal(window) == true ? new TextToSpeechRequest(window.Text, window.Voice, window.Rate) : null;
    }

    public void ShowUpdateResult(UpdateInfo info, bool isNewer)
    {
        if (isNewer) UpdateWindow.ShowNewVersion(Owner, info);
        else UpdateWindow.ShowUpToDate(Owner);
    }

    /// <summary>Mostra uma janela modal sobre a janela principal. Retorna DialogResult.</summary>
    public static bool? ShowModal(Window dialog)
    {
        dialog.Owner = Owner;
        return dialog.ShowDialog();
    }
}
