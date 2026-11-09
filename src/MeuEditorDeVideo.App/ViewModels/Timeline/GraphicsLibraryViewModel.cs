using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.ViewModels.Timeline;

/// <summary>Item da seção Elementos gráficos.</summary>
public sealed record GraphicPresetItem(ShapePreset Preset, string Glyph)
{
    public string Name => Preset.Name;
    public string Color => Preset.Style.FillColor;
}

/// <summary>
/// Seção "Elementos gráficos" da biblioteca (F9): formas vetoriais prontas inseridas no cursor numa trilha de gráficos
/// e importação de gráficos vetoriais SVG (RF152).
/// </summary>
public sealed partial class GraphicsLibraryViewModel(TimelineViewModel timeline, MediaLibraryViewModel library, IDialogService dialogs) : ObservableObject
{
    public TimelineViewModel Timeline { get; } = timeline;

    public IReadOnlyList<GraphicPresetItem> Presets { get; } = ShapePresets.All.Select(p => new GraphicPresetItem(p, p.Kind switch
    {
        ShapeKind.Rectangle => "▬",
        ShapeKind.Ellipse => "●",
        ShapeKind.Line => "─",
        ShapeKind.Arrow => "➜",
        ShapeKind.Star => "★",
        ShapeKind.Polygon => "⬢",
        ShapeKind.SpeechBubble => "💬",
        ShapeKind.Heart => "♥",
        _ => "◆",
    })).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPresetCommand))]
    public partial GraphicPresetItem? SelectedPreset { get; set; }

    private bool CanAdd(GraphicPresetItem? item) => Timeline.Project is not null && (item ?? SelectedPreset) is not null;

    /// <summary>Insere a forma no cursor (5 s), numa trilha de gráficos livre (cria uma no topo se preciso).</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddPreset(GraphicPresetItem? item)
    {
        item ??= SelectedPreset;
        if (item is null || Timeline.Project is null) return;
        var clip = ShapePresets.Create(item.Preset.Style, Timeline.Playhead, MediaTime.FromSeconds(5));
        if (Timeline.Execute(new AddTextClipCommand(Timeline.SequenceId, clip, TrackKind.Image)))
            Timeline.Select(clip.Id, additive: false);
    }

    /// <summary>Importa arquivos SVG para a biblioteca de mídias (ficam nítidos em qualquer escala).</summary>
    [RelayCommand]
    private async Task ImportSvg()
    {
        if (Timeline.Project is null) return;
        var files = dialogs.PickFiles("Importar gráficos vetoriais", "Gráficos vetoriais SVG (*.svg)|*.svg", null);
        if (files.Count == 0) return;
        await library.ImportPathsAsync(files);
    }
}
