using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Export;
using L = MeuEditorDeVideo.Presentation.Localization.Translator;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>Diálogos do Avalonia; cada janela pedida pelos ViewModels abre sobre a janela ativa.</summary>
public sealed class DialogService : IDialogService
{
    private const string ProjectFilter = "Projeto do Meu Editor de Vídeo (*.med)|*.med";

    public void ShowError(MedError error) =>
        Message(AppInfo.ProductName, $"{L.T(error.Message)}\n\n{L.T("Código:")} {error.Code}", MessageKind.Error, ["OK"]);

    public void ShowInfo(string title, string message) => Message(L.T(title), L.T(message), MessageKind.Info, ["OK"]);

    public bool Confirm(string title, string message) =>
        Message(L.T(title), L.T(message), MessageKind.Question, ["Sim", "Não"], defaultIndex: 1, cancelIndex: 1) == 0;

    public UnsavedChoice AskSaveChanges(string projectName) =>
        Message(AppInfo.ProductName, L.F("Deseja salvar as alterações em \"{0}\"?", projectName), MessageKind.Warning,
            ["Salvar", "Não salvar", "Cancelar"], defaultIndex: 0, cancelIndex: 2) switch
        {
            0 => UnsavedChoice.Save,
            1 => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };

    /// <summary>Caixa de mensagem modal. Índice do botão escolhido, ou -1.</summary>
    public static int Message(string title, string message, MessageKind kind, IReadOnlyList<string> buttons, int defaultIndex = 0, int cancelIndex = -1)
    {
        var window = new MessageWindow(title, message, kind, buttons, defaultIndex, cancelIndex);
        DialogHost.ShowModal(window);
        return window.Result < 0 ? cancelIndex : window.Result;
    }

    public string? PickOpenProject(string? initialDirectory) => PickFile("Abrir projeto", ProjectFilter, initialDirectory);

    public string? PickSaveProject(string suggestedName, string? initialDirectory) =>
        PickSaveFile("Salvar projeto como", ProjectFilter, suggestedName, initialDirectory);

    public string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory) =>
        ModalLoop.Run(PickSaveFileAsync(title, filter, suggestedName, initialDirectory));

    public string? PickFile(string title, string filter, string? initialDirectory) =>
        ModalLoop.Run(PickFilesAsync(title, filter, initialDirectory, multiple: false)).FirstOrDefault();

    public IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory) =>
        ModalLoop.Run(PickFilesAsync(title, filter, initialDirectory, multiple: true));

    public string? PickFolder(string title, string? initialDirectory) => ModalLoop.Run(PickFolderAsync(title, initialDirectory));

    private static IStorageProvider Storage =>
        (DialogHost.Owner ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault())
            ?.StorageProvider ?? throw new InvalidOperationException("Nenhuma janela aberta para mostrar o seletor de arquivos.");

    private static async Task<IStorageFolder?> FolderAsync(string? path) =>
        string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : await Storage.TryGetFolderFromPathAsync(path);

    private static async Task<IReadOnlyList<string>> PickFilesAsync(string title, string filter, string? initialDirectory, bool multiple)
    {
        var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T(title),
            AllowMultiple = multiple,
            FileTypeFilter = FileFilters.Parse(filter),
            SuggestedStartLocation = await FolderAsync(initialDirectory),
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private static async Task<string?> PickSaveFileAsync(string title, string filter, string suggestedName, string? initialDirectory)
    {
        var extension = FileFilters.DefaultExtension(filter);
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T(title),
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = FileFilters.Parse(filter),
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(initialDirectory),
        });
        if (file?.TryGetLocalPath() is not { } path) return null;
        // Alguns seletores (GTK) não acrescentam a extensão do filtro.
        return extension is not null && !Path.HasExtension(path) ? path + "." + extension : path;
    }

    private static async Task<string?> PickFolderAsync(string title, string? initialDirectory)
    {
        var folders = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T(title),
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(initialDirectory),
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public string? Prompt(string title, string label, string initialValue)
    {
        var dialog = new PromptWindow(title, label, initialValue);
        return DialogHost.ShowModal(dialog) == true ? dialog.Value : null;
    }

    public bool ShowSettings() => SettingsWindow.ShowFor(DialogHost.Owner);

    public int? Choose(string title, string label, IReadOnlyList<string> options, int selected = 0, string okText = "OK")
    {
        var window = new ChoiceWindow(title, label, options, selected, okText);
        return DialogHost.ShowModal(window) == true && window.SelectedIndex >= 0 ? window.SelectedIndex : null;
    }

    public bool ShowNewProject(NewProjectViewModel viewModel) => DialogHost.ShowModal(new NewProjectWindow(viewModel)) == true;

    public void ShowMissingMedia(MissingMediaViewModel viewModel) => DialogHost.ShowModal(new MissingMediaWindow(viewModel));

    public bool ManageContentPacks(IContentPackStore store)
    {
        var window = new ContentPacksWindow(store, this);
        DialogHost.ShowModal(window);
        return window.Changed;
    }

    public ScreenRecordingResult? RecordScreen(IScreenRecorder recorder, string outputPath)
    {
        var window = new ScreenRecordWindow(recorder, outputPath);
        return DialogHost.ShowModal(window) == true && window.RecordedPath is { } path ? new ScreenRecordingResult(path, window.InsertOnTimeline) : null;
    }

    public bool ShowExport(ExportViewModel viewModel) => DialogHost.ShowModal(new ExportWindow(viewModel)) == true;

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
            DialogHost.ShowModal(window);
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
        if (_queueWindow is { IsVisible: true })
        {
            _queueWindow.Activate();
            return;
        }
        _queueWindow = new RenderQueueWindow(create());
        _queueWindow.Closed += (_, _) => _queueWindow = null;
        if (DialogHost.Owner is { } owner) _queueWindow.Show(owner);
        else _queueWindow.Show();
    }

    public ReframeChoice? AskReframe(string currentSize, bool trackingAvailable)
    {
        var window = new ReframeWindow(currentSize, trackingAvailable);
        if (DialogHost.ShowModal(window) != true) return null;
        var (w, h) = window.Format;
        return new ReframeChoice(w, h, window.FollowHead);
    }

    public TextToSpeechRequest? AskTextToSpeech(IReadOnlyList<string> voices)
    {
        var window = new TextToSpeechWindow(voices);
        return DialogHost.ShowModal(window) == true ? new TextToSpeechRequest(window.Text, window.Voice, window.Rate) : null;
    }

    public void ShowUpdateResult(UpdateInfo info, bool isNewer)
    {
        if (isNewer) UpdateWindow.ShowNewVersion(DialogHost.Owner, info);
        else UpdateWindow.ShowUpToDate(DialogHost.Owner);
    }
}
