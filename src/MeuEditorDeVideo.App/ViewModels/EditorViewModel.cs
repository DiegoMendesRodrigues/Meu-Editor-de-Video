using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Media;
using MeuEditorDeVideo.App.ViewModels.Preview;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Domain.Editing;

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

/// <summary>Item do painel de histórico (RF014). Index = quantidade de ações aplicadas após clicar.</summary>
public sealed record HistoryItem(int Index, string Description, string Time, bool IsCurrent, bool IsUndone);

/// <summary>Tela principal de edição (§5.3).</summary>
public sealed partial class EditorViewModel(
    INavigationService navigation,
    IThemeService themes,
    ISettingsService settings,
    IProjectService projects,
    IProjectWorkflow workflow,
    ShellViewModel shell,
    IServiceProvider services)
    : PageViewModel
{
    private ProjectSession? _session;

    /// <summary>Biblioteca de mídias (painel A). Existe enquanto a tela de edição está aberta.</summary>
    [ObservableProperty]
    public partial MediaLibraryViewModel? Library { get; set; }

    /// <summary>Monitor de pré-visualização (painel B).</summary>
    [ObservableProperty]
    public partial PreviewViewModel? Preview { get; set; }

    /// <summary>Linha do tempo (painel D).</summary>
    [ObservableProperty]
    public partial TimelineViewModel? Timeline { get; set; }

    partial void OnSelectedSectionChanged(LibrarySection value)
    {
        if (Library is not null) Library.Filter = FilterFor(value);
        OnPropertyChanged(nameof(IsMediaSection));
    }

    /// <summary>Seções de mídia mostram a biblioteca; as demais (gráficos, textos…) chegam em fases futuras.</summary>
    public bool IsMediaSection => SelectedSection is LibrarySection.Media or LibrarySection.Videos or LibrarySection.Images or LibrarySection.Audio;

    private static MediaFilter FilterFor(LibrarySection s) => s switch
    {
        LibrarySection.Videos => MediaFilter.Videos,
        LibrarySection.Images => MediaFilter.Images,
        LibrarySection.Audio => MediaFilter.Audio,
        _ => MediaFilter.All,
    };

    [ObservableProperty]
    public partial string ProjectName { get; set; } = "Sem título";

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Media;

    [ObservableProperty]
    public partial string UndoToolTip { get; set; } = "Desfazer (Ctrl+Z)";

    [ObservableProperty]
    public partial string RedoToolTip { get; set; } = "Refazer (Ctrl+Y)";

    [ObservableProperty]
    public partial int MissingMediaCount { get; set; }

    /// <summary>Resumo das configurações técnicas: "1920 × 1080 · 30 FPS · Rec. 709".</summary>
    [ObservableProperty]
    public partial string ProjectSummary { get; set; } = "";

    public ObservableCollection<HistoryItem> HistoryItems { get; } = [];

    public bool IsDarkTheme => themes.Current != AppTheme.Light;

    public override void OnNavigatedTo()
    {
        Library = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MediaLibraryViewModel>(services);
        Library.Filter = FilterFor(SelectedSection);
        Timeline = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<TimelineViewModel>(services, Library);
        Preview = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<PreviewViewModel>(services, Timeline);
        projects.SessionChanged += OnSessionChanged;
        projects.Saved += OnSaved;
        AttachSession();
    }

    public override void OnNavigatedFrom()
    {
        projects.SessionChanged -= OnSessionChanged;
        projects.Saved -= OnSaved;
        if (_session is not null) _session.History.Changed -= OnHistoryChanged;
        _session = null;
        shell.Title = AppInfo.ProductName;
        Preview?.Dispose();
        Preview = null;
        Timeline?.Dispose();
        Timeline = null;
        Library?.Dispose();
        Library = null;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => AttachSession();

    private void OnSaved(object? sender, EventArgs e) => Refresh();

    private void OnHistoryChanged(object? sender, EventArgs e) => Refresh();

    private void AttachSession()
    {
        if (!ReferenceEquals(_session, projects.Current))
        {
            if (_session is not null) _session.History.Changed -= OnHistoryChanged;
            _session = projects.Current;
            if (_session is not null) _session.History.Changed += OnHistoryChanged;
        }
        Refresh();
    }

    private void Refresh()
    {
        var s = _session;
        ProjectName = s?.Project.Name ?? "Sem título";
        IsDirty = s?.IsDirty ?? false;
        MissingMediaCount = s?.Media.Missing.Count() ?? 0;
        shell.Title = s is null
            ? AppInfo.ProductName
            : $"{(IsDirty ? "• " : "")}{ProjectName} — {AppInfo.ProductName}";

        if (s is not null)
        {
            var p = s.Project.Settings;
            ProjectSummary = $"{p.Width} × {p.Height} · {p.FrameRate.ToString().Replace('.', ',')} FPS";
        }

        var history = s?.History;
        UndoToolTip = history?.UndoDescription is { } u ? $"Desfazer: {u} (Ctrl+Z)" : "Desfazer (Ctrl+Z)";
        RedoToolTip = history?.RedoDescription is { } r ? $"Refazer: {r} (Ctrl+Y)" : "Refazer (Ctrl+Y)";
        RebuildHistory(history);

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        PackageMediaCommand.NotifyCanExecuteChanged();
        ImportMediaCommand.NotifyCanExecuteChanged();
        ImportSequenceCommand.NotifyCanExecuteChanged();
        CreateBackupCommand.NotifyCanExecuteChanged();
        ShowMissingMediaCommand.NotifyCanExecuteChanged();
        GoToHistoryCommand.NotifyCanExecuteChanged();
    }

    private void RebuildHistory(EditHistory? history)
    {
        HistoryItems.Clear();
        if (history is null) return;
        HistoryItems.Add(new HistoryItem(0, "Estado inicial", "", history.AppliedCount == 0, false));
        var entries = history.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var applied = i + 1;
            HistoryItems.Add(new HistoryItem(applied, entries[i].Description, entries[i].At.LocalDateTime.ToString("HH:mm:ss"),
                applied == history.AppliedCount, applied > history.AppliedCount));
        }
    }

    private bool HasSession => _session is not null;

    [RelayCommand]
    private async Task GoHome()
    {
        if (!await workflow.CloseProjectAsync()) return;
        navigation.NavigateTo<HomeViewModel>();
    }

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

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task ImportMedia() => Library?.ImportFilesCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task ImportSequence() => Library?.ImportSequenceCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private Task NewProject() => workflow.NewProjectAsync();

    [RelayCommand]
    private Task Open() => workflow.OpenAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task Save() => workflow.SaveAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task SaveAs() => workflow.SaveAsAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task PackageMedia() => workflow.PackageMediaAsync();

    [RelayCommand(CanExecute = nameof(HasSession))]
    private Task CreateBackup() => workflow.CreateBackupAsync();

    private bool HasMissingMedia() => MissingMediaCount > 0;

    [RelayCommand(CanExecute = nameof(HasMissingMedia))]
    private void ShowMissingMedia() => workflow.ShowMissingMedia();

    private bool CanUndo() => _session?.History.CanUndo == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _session?.History.Undo();

    private bool CanRedo() => _session?.History.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _session?.History.Redo();

    private bool CanGoToHistory(HistoryItem? item) => item is not null && _session is not null && !item.IsCurrent;

    [RelayCommand(CanExecute = nameof(CanGoToHistory))]
    private void GoToHistory(HistoryItem? item)
    {
        if (item is not null) _session?.History.GoTo(item.Index);
    }

    /// <summary>Usado pela janela principal ao fechar o aplicativo.</summary>
    public Task<bool> ConfirmCloseAsync() => workflow.ConfirmDiscardOrSaveAsync();

    // Comandos das fases seguintes — desabilitados até existirem de verdade.
    private static bool NotYetAvailable() => false;

    [RelayCommand(CanExecute = nameof(NotYetAvailable))] private void Export() { }

    partial void OnMissingMediaCountChanged(int value) => ShowMissingMediaCommand.NotifyCanExecuteChanged();
}
