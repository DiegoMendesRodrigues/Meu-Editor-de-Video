// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Presentation.ViewModels.Timeline;

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

    /// <summary>Menu Ferramentas de desenho da barra de botões da linha do tempo (ícone Segoe Fluent Icons e forma).</summary>
    public IReadOnlyList<GraphicPresetItem> DrawingTools { get; } =
    [
        new(new ShapePreset(ShapeKind.Rectangle, "Retângulo", new ShapeStyle { Kind = ShapeKind.Rectangle, Width = 480, Height = 270 }), ""),
        new(new ShapePreset(ShapeKind.Arrow, "Seta", new ShapeStyle { Kind = ShapeKind.Arrow, Width = 420, Height = 160, FillColor = "#FFFF4D4D" }), ""),
        new(new ShapePreset(ShapeKind.Ellipse, "Elipse", new ShapeStyle { Kind = ShapeKind.Ellipse, Width = 420, Height = 260, FillColor = "#FFFFB13D" }), ""),
        new(new ShapePreset(ShapeKind.Polygon, "Triângulo", new ShapeStyle { Kind = ShapeKind.Polygon, Width = 320, Height = 280, FillColor = "#FF4DD08A", Sides = 3 }), "△"),
        new(new ShapePreset(ShapeKind.Line, "Linha", new ShapeStyle { Kind = ShapeKind.Line, Width = 600, Height = 8, FillColor = "#FFFFFFFF" }), "╱"),
        new(new ShapePreset(ShapeKind.Polygon, "Polígono", new ShapeStyle { Kind = ShapeKind.Polygon, Width = 320, Height = 320, FillColor = "#FFB36BFF", Sides = 5 }), "⬠"),
    ];

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
            Timeline.SelectInserted(clip.Id);
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
