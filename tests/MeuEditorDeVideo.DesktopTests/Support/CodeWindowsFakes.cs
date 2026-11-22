using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Export;
using MeuEditorDeVideo.Application.Export;

namespace MeuEditorDeVideo.DesktopTests.Support;

/// <summary>Gravador de tela falso: duas telas, três janelas, uma câmera e um microfone; a gravação só devolve o caminho.</summary>
public sealed class FakeScreenRecorder : IScreenRecorder
{
    public bool IsAvailable { get; init; } = true;

    public string? ScreenCaptureUnavailableReason { get; init; }

    public ScreenRecordingOptions? Started { get; private set; }

    public IReadOnlyList<ScreenArea> Screens() =>
    [
        new ScreenArea(1, 0, 0, 1920, 1080, true),
        new ScreenArea(2, 1920, 0, 2560, 1440, false),
    ];

    public IReadOnlyList<CaptureWindow> Windows(ScreenArea? area) =>
    [
        new CaptureWindow(1, "Praia.mp4 - Reprodutor", "player"),
        new CaptureWindow(2, "Floresta Amazônica - Navegador", "browser"),
    ];

    public Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default) =>
        Task.FromResult(new CaptureDevices(["Câmera integrada HD"], ["Microfone (Realtek)", "Microfone USB"]));

    public Task<IScreenRecording> StartAsync(ScreenRecordingOptions options, CancellationToken ct = default)
    {
        Started = options;
        return Task.FromResult<IScreenRecording>(new FakeRecording(options.OutputPath));
    }

    private sealed class FakeRecording(string path) : IScreenRecording
    {
        private readonly DateTime _start = DateTime.UtcNow;

        public TimeSpan Elapsed => DateTime.UtcNow - _start;

        public bool IsRunning { get; private set; } = true;

        public Task<string> StopAsync()
        {
            IsRunning = false;
            return Task.FromResult(path);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Pacotes de conteúdo em memória (o pacote "Básico" vem com o programa e não pode ser removido).</summary>
public sealed class FakeContentPackStore : IContentPackStore
{
    private readonly List<ContentPackInfo> _packs =
    [
        new("basico", "Básico", "", 24, 8, 3) { IsBuiltIn = true },
        new("viagem", "Viagens e Praia", "", 12, 4, 2),
    ];

    public IReadOnlyList<ContentPackInfo> Packs() => _packs.ToList();

    public IReadOnlyList<ContentPackItem> Stickers() => [];

    public IReadOnlyList<ContentPackItem> Titles() => [];

    public IReadOnlyList<ContentPackItem> Templates() => [];

    public ContentPackInfo Install(string packagePath)
    {
        var info = new ContentPackInfo("novo", "Pacote novo", "", 5, 1, 0);
        _packs.Add(info);
        return info;
    }

    public void Export(string packId, string destinationPath) { }

    public void Remove(string packId)
    {
        if (_packs.FirstOrDefault(p => p.Id == packId) is { IsBuiltIn: true })
            throw new InvalidOperationException("Pacote incluído no programa não pode ser removido.");
        _packs.RemoveAll(p => p.Id == packId);
    }

    public ContentPackItem SaveTitle(Clip textClip, string name) => throw new NotSupportedException();

    public Clip LoadTitle(string path) => throw new NotSupportedException();

    public string NewTemplatePath(string name) => throw new NotSupportedException();
}

/// <summary>Diálogos falsos: registram o que foi mostrado e devolvem respostas combinadas.</summary>
public sealed class FakeDialogService : IDialogService
{
    public List<string> Infos { get; } = [];

    public bool ConfirmAnswer { get; set; } = true;

    public string? PickedFile { get; set; }

    public void ShowError(MedError error) => Infos.Add(error.ToString() ?? "");

    public void ShowInfo(string title, string message) => Infos.Add(title + ": " + message);

    public bool Confirm(string title, string message) => ConfirmAnswer;

    public UnsavedChoice AskSaveChanges(string projectName) => UnsavedChoice.Cancel;

    public string? PickOpenProject(string? initialDirectory) => null;

    public string? PickSaveProject(string suggestedName, string? initialDirectory) => null;

    public string? PickFile(string title, string filter, string? initialDirectory) => PickedFile;

    public string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory) => null;

    public string? PickFolder(string title, string? initialDirectory) => null;

    public IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory) => [];

    public string? Prompt(string title, string label, string initialValue) => null;

    public int? Choose(string title, string label, IReadOnlyList<string> options, int selected = 0, string okText = "OK") => null;

    public bool ShowSettings() => false;

    public bool ShowNewProject(NewProjectViewModel viewModel) => false;

    public void ShowMissingMedia(MissingMediaViewModel viewModel) { }

    public bool ManageContentPacks(IContentPackStore store) => false;

    public ScreenRecordingResult? RecordScreen(IScreenRecorder recorder, string outputPath) => null;

    public bool ShowExport(ExportViewModel viewModel) => false;

    public RenderJob? ShowExportProgress(RenderQueueViewModel queue) => null;

    public void ShowRenderQueue(Func<RenderQueueViewModel> create) { }

    public ReframeChoice? AskReframe(string currentSize, bool trackingAvailable) => null;

    public TextToSpeechRequest? AskTextToSpeech(IReadOnlyList<string> voices) => null;

    public void ShowUpdateResult(UpdateInfo info, bool isNewer) { }
}
