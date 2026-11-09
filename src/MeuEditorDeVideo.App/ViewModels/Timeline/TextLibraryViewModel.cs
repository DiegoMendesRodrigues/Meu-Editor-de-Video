using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.Subtitles;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.ViewModels.Timeline;

/// <summary>Modelo da seção Textos da biblioteca.</summary>
public sealed record TextPresetItem(TextKind Kind, string Name, string Description, string Sample);

/// <summary>
/// Seção "Textos" da biblioteca (F8): insere textos, títulos, créditos, tarjas e legendas na posição do cursor
/// (RF132–RF135) e importa/exporta legendas SRT e VTT (RF140, RF141).
/// </summary>
public sealed partial class TextLibraryViewModel(TimelineViewModel timeline, IDialogService dialogs) : ObservableObject
{
    private const string SubtitleFilter = "Legendas (*.srt;*.vtt)|*.srt;*.vtt|SubRip (*.srt)|*.srt|WebVTT (*.vtt)|*.vtt";

    public TimelineViewModel Timeline { get; } = timeline;

    /// <summary>Diálogos (compartilhados com o editor de legendas).</summary>
    public IDialogService Dialogs { get; } = dialogs;

    public IReadOnlyList<TextPresetItem> Presets { get; } =
    [
        new(TextKind.Text, "Texto simples", "Texto em negrito com sombra suave; entra e sai esmaecendo.", "Aa"),
        new(TextKind.Title, "Título", "Título grande no centro, com entrada em zoom.", "T"),
        new(TextKind.LowerThird, "Tarja (lower third)", "Nome e cargo no canto inferior esquerdo, com caixa e barra de destaque.", "▭"),
        new(TextKind.Credits, "Créditos rolantes", "Lista de créditos que sobe pela tela durante o clipe.", "≡"),
        new(TextKind.Subtitle, "Legenda", "Legenda na trilha de legendas, com caixa escura na base do quadro.", "cc"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPresetCommand))]
    public partial TextPresetItem? SelectedPreset { get; set; }

    private bool HasSession() => Timeline.Project is not null;

    private bool CanAddPreset(TextPresetItem? item) => HasSession() && (item ?? SelectedPreset) is not null;

    /// <summary>Insere o modelo na posição do cursor, numa trilha livre (cria uma no topo se preciso) e o seleciona.</summary>
    [RelayCommand(CanExecute = nameof(CanAddPreset))]
    private void AddPreset(TextPresetItem? item)
    {
        item ??= SelectedPreset;
        if (item is null || Timeline.Project is not { } project) return;
        var clip = TextPresets.Create(item.Kind, project.Settings, Timeline.Playhead);
        if (Timeline.Execute(new AddTextClipCommand(Timeline.SequenceId, clip, TextPresets.TrackKindFor(item.Kind))))
            Timeline.Select(clip.Id, additive: false);
    }

    /// <summary>Importa um arquivo SRT/VTT numa trilha de legendas nova (RF140, RF141).</summary>
    [RelayCommand]
    private void ImportSubtitles()
    {
        if (Timeline.Project is not { } project) return;
        var path = Dialogs.PickFile("Importar legendas", SubtitleFilter, null);
        if (path is null) return;
        IReadOnlyList<SubtitleCue> cues;
        try
        {
            cues = SubtitleFile.Load(path);
        }
        catch (SubtitleParseException ex)
        {
            Dialogs.ShowInfo("Importar legendas", ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowInfo("Importar legendas", $"Não foi possível ler o arquivo: {ex.Message}");
            return;
        }

        var clips = SubtitleOps.ToClips(cues.Select(c => new SubtitleEntry(c.Start, c.End, c.Text)), project.Settings);
        if (clips.Count == 0)
        {
            Dialogs.ShowInfo("Importar legendas", "O arquivo não tem legendas para importar.");
            return;
        }
        var name = "Legendas · " + Path.GetFileNameWithoutExtension(path);
        Timeline.Execute(new ImportSubtitlesCommand(Timeline.SequenceId, clips) { NewTrackName = name, Description = $"Importar legendas ({clips.Count})" });
    }

    /// <summary>Exporta as legendas das trilhas de legenda visíveis para SRT ou VTT.</summary>
    [RelayCommand]
    private void ExportSubtitles()
    {
        if (Timeline.Sequence is not { } seq || Timeline.Project is not { } project) return;
        var entries = SubtitleOps.FromSequence(seq);
        if (entries.Count == 0)
        {
            Dialogs.ShowInfo("Exportar legendas", "Não há legendas na linha do tempo.");
            return;
        }
        var path = Dialogs.PickSaveFile("Exportar legendas", "SubRip (*.srt)|*.srt|WebVTT (*.vtt)|*.vtt", project.Name + ".srt", null);
        if (path is null) return;
        var format = SubtitleFile.FormatFromExtension(path) ?? SubtitleFormat.Srt;
        try
        {
            SubtitleFile.Save(path, entries.Select(e => new SubtitleCue(e.Start, e.End, e.Text)), format);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowInfo("Exportar legendas", $"Não foi possível gravar o arquivo: {ex.Message}");
        }
    }

}
