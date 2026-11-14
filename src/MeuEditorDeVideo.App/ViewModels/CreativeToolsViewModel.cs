using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>
/// Gravação de tela, webcam e microfone e pacotes locais de conteúdo: figurinhas (Elementos gráficos), títulos salvos (Textos)
/// e modelos de projeto (Arquivo).
/// </summary>
public sealed partial class CreativeToolsViewModel(
    TimelineViewModel timeline,
    IContentPackStore packs,
    IScreenRecorder recorder,
    IMediaImportService import,
    IProjectService projects,
    IProjectWorkflow workflow,
    IDialogService dialogs,
    ILogger<CreativeToolsViewModel> logger) : ObservableObject
{
    public TimelineViewModel Timeline { get; } = timeline;

    public ObservableCollection<ContentPackItem> Stickers { get; } = [];

    public ObservableCollection<ContentPackItem> Titles { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public bool HasStickers => Stickers.Count > 0;

    public bool HasTitles => Titles.Count > 0;

    /// <summary>Relê os pacotes instalados.</summary>
    public void Refresh()
    {
        Replace(Stickers, packs.Stickers());
        Replace(Titles, packs.Titles());
        OnPropertyChanged(nameof(HasStickers));
        OnPropertyChanged(nameof(HasTitles));
    }

    private static void Replace(ObservableCollection<ContentPackItem> target, IReadOnlyList<ContentPackItem> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    private bool HasSession() => Timeline.Project is not null;

    // ---------------------------------------------------------------- pacotes

    [RelayCommand]
    private void ManagePacks()
    {
        var window = new Views.Dialogs.ContentPacksWindow(packs, dialogs);
        dialogs.ShowModal(window);
        if (window.Changed) Refresh();
    }

    /// <summary>Insere a figurinha no cursor (3 s), sobre o vídeo; a imagem é importada na biblioteca.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private async Task InsertSticker(ContentPackItem? item)
    {
        if (item is null || Timeline.Project is null) return;
        if (await ImportOneAsync(item.Path, "Figurinhas") is not { } id) return;
        var command = new AddOverlayCommand(Timeline.SequenceId, id, Timeline.Playhead, MediaTime.FromSeconds(3), "Figurinhas") { Scale = 0.4 };
        if (Timeline.Execute(command)) Timeline.Select(command.VideoClipId, additive: false);
    }

    /// <summary>Importa (ou reaproveita, se já estiver na biblioteca) e devolve o Id da mídia.</summary>
    private async Task<Guid?> ImportOneAsync(string path, string category)
    {
        var full = Path.GetFullPath(path);
        Guid? Existing() => Timeline.Project?.Media.FirstOrDefault(m =>
            string.Equals(Path.GetFullPath(m.OriginalPath), full, StringComparison.OrdinalIgnoreCase))?.Id;
        if (Existing() is { } id) return id;
        try
        {
            var result = await import.ImportAsync([path], category, null, CancellationToken.None);
            if (result.Imported.Count > 0) return result.Imported[0].Id;
            if (Existing() is { } again) return again;
            dialogs.ShowInfo("Importar", "Não foi possível importar: " + string.Join("; ", result.Failed.Select(f => f.Reason)));
        }
        catch (Core.Errors.MedException ex)
        {
            dialogs.ShowError(ex.Error);
        }
        return null;
    }

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void InsertTitle(ContentPackItem? item)
    {
        if (item is null || Timeline.Project is null) return;
        Clip clip;
        try { clip = packs.LoadTitle(item.Path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            dialogs.ShowInfo("Inserir título", ex.Message);
            return;
        }
        clip = clip with { Start = Timeline.Project.Settings.FrameRate.SnapToFrame(Timeline.Playhead) };
        var kind = clip.TextFormat?.Kind == TextKind.Subtitle ? TrackKind.Subtitle : TrackKind.Text;
        if (Timeline.Execute(new AddTextClipCommand(Timeline.SequenceId, clip, kind))) Timeline.Select(clip.Id, additive: false);
    }

    /// <summary>Salva o texto selecionado (estilo, animações e duração) como título reutilizável em "Meus itens".</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void SaveTitle()
    {
        var clip = Timeline.Sequence?.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => Timeline.Selection.Contains(c.Id) && c.IsText);
        if (clip is null)
        {
            dialogs.ShowInfo("Salvar título", "Selecione um texto na linha do tempo.");
            return;
        }
        var name = dialogs.Prompt("Salvar como título", "Nome do título", Inspector.ClipInspectorViewModel.TextTitle(clip.Text!));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            packs.SaveTitle(clip, name.Trim());
            Refresh();
            Status = $"Título \"{name.Trim()}\" salvo em Meus itens.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowInfo("Salvar título", ex.Message);
        }
    }

    /// <summary>Projeto › Salvar como modelo: copia o projeto salvo para os modelos do usuário.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private async Task SaveAsTemplate()
    {
        if (projects.Current is not { } session) return;
        if (session.FilePath is null || session.IsDirty)
        {
            if (!dialogs.Confirm("Salvar como modelo", "O projeto precisa estar salvo para virar modelo. Salvar agora?")) return;
            if (!await workflow.SaveAsync() || projects.Current?.FilePath is null) return;
            session = projects.Current;
        }
        var name = dialogs.Prompt("Salvar como modelo", "Nome do modelo", Path.GetFileNameWithoutExtension(session.FilePath));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            File.Copy(session.FilePath!, packs.NewTemplatePath(name.Trim()));
            dialogs.ShowInfo("Salvar como modelo", $"Modelo \"{name.Trim()}\" salvo. Use Arquivo › Novo a partir de modelo para começar um projeto com ele.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowInfo("Salvar como modelo", ex.Message);
        }
    }

    /// <summary>Arquivo › Novo a partir de modelo: copia o modelo para um arquivo novo e abre a cópia.</summary>
    [RelayCommand]
    private async Task NewFromTemplate()
    {
        var templates = packs.Templates();
        if (templates.Count == 0)
        {
            dialogs.ShowInfo("Novo a partir de modelo", "Nenhum modelo instalado. Salve um projeto com Projeto › Salvar como modelo ou instale um pacote em Ferramentas › Pacotes de conteúdo.");
            return;
        }
        var choice = new Views.Dialogs.ChoiceWindow("Novo a partir de modelo", "Modelo", [.. templates.Select(t => $"{t.Name}  ({t.PackName})")], 0, "Continuar");
        if (dialogs.ShowModal(choice) != true || choice.SelectedIndex < 0) return;
        var template = templates[choice.SelectedIndex];
        if (!await workflow.ConfirmDiscardOrSaveAsync()) return;
        var target = dialogs.PickSaveFile("Salvar o novo projeto como", "Projeto (*.med)|*.med", template.Name + ".med", null);
        if (target is null) return;
        try
        {
            File.Copy(template.Path, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowInfo("Novo a partir de modelo", ex.Message);
            return;
        }
        await workflow.OpenAsync(target);
    }

    // ---------------------------------------------------------------- gravação

    [RelayCommand(CanExecute = nameof(HasSession))]
    private async Task RecordScreen()
    {
        if (!recorder.IsAvailable)
        {
            dialogs.ShowInfo("Gravar tela", "O ffmpeg.exe não foi encontrado na pasta do aplicativo. Reinstale o programa.");
            return;
        }
        var baseDir = projects.Current?.FilePath is { } file ? Path.GetDirectoryName(file)! :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Meu Editor de Vídeo");
        var output = Path.Combine(baseDir, "Gravações", $"Gravação {DateTime.Now:yyyy-MM-dd HH.mm.ss}.mp4");
        var window = new Views.Dialogs.ScreenRecordWindow(recorder, output);
        if (dialogs.ShowModal(window) != true || window.RecordedPath is not { } path) return;
        logger.LogInformation("Gravação concluída: {Arquivo}", path);
        var at = Timeline.Playhead;
        if (await ImportOneAsync(path, "Gravações") is not { } id) return;
        if (window.InsertOnTimeline)
            Timeline.Execute(new AddOverlayCommand(Timeline.SequenceId, id, at, MediaTime.FromSeconds(5), "Gravação"));
        Status = "Gravação importada para a biblioteca (categoria Gravações).";
    }
}
