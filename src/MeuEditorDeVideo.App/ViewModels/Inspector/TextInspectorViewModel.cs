using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Text;

namespace MeuEditorDeVideo.App.ViewModels.Inspector;

/// <summary>Cor de um texto (#AARRGGBB ou #RRGGBB) com amostra e cores rápidas.</summary>
public sealed partial class TextColorViewModel : ObservableObject
{
    private readonly TextInspectorViewModel _owner;
    private readonly Func<TextStyle, string> _get;
    private readonly Func<TextStyle, string, TextStyle> _set;
    private bool _updating;

    public TextColorViewModel(TextInspectorViewModel owner, string key, string label, Func<TextStyle, string> get, Func<TextStyle, string, TextStyle> set,
        IReadOnlyList<string> presets)
    {
        _owner = owner;
        Key = key;
        Label = label;
        _get = get;
        _set = set;
        Presets = presets;
    }

    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<string> Presets { get; }

    [ObservableProperty]
    public partial string Hex { get; set; } = "#FFFFFFFF";

    [ObservableProperty]
    public partial Brush Swatch { get; set; } = Brushes.White;

    partial void OnHexChanged(string value)
    {
        if (!TryParse(value, out var color)) return;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Swatch = brush;
        if (!_updating) _owner.UpdateStyle(s => _set(s, Normalize(color)), $"Cor do texto: {Label.ToLowerInvariant()}", Key);
    }

    [RelayCommand]
    private void UsePreset(string hex) => Hex = hex;

    public void Refresh(TextStyle style)
    {
        _updating = true;
        try
        {
            Hex = TryParse(_get(style), out var c) ? Normalize(c) : _get(style);
        }
        finally
        {
            _updating = false;
        }
    }

    public static string Normalize(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.Transparent;
        var t = (text ?? "").Trim().TrimStart('#');
        if (t.Length is not (6 or 8) || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        color = t.Length == 6
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}

/// <summary>
/// Aba "Texto" do painel C (F8): conteúdo, fonte, cores, contorno, sombra, caixa de fundo, posição do bloco
/// e animações de entrada/saída do clipe de texto ou legenda selecionado (RF132–RF139, RF143).
/// </summary>
public sealed partial class TextInspectorViewModel : ObservableObject, IClipEditor, IDisposable
{
    private static readonly string[] TextPresetsColors = ["#FFFFFF", "#000000", "#FFD400", "#3D9BFF", "#FF4D4D", "#2ECC71"];
    private static readonly string[] BoxPresetsColors = ["#00000000", "#A0000000", "#D9101820", "#FFFFFFFF", "#CC3D9BFF", "#CCFF4D4D"];
    private readonly IDialogService _dialogs;
    private Guid? _clipId;
    private bool _updating;

    public TextInspectorViewModel(TimelineViewModel timeline, IDialogService dialogs)
    {
        Timeline = timeline;
        _dialogs = dialogs;
        Fonts = new ObservableCollection<string>(FontCatalog.Families());

        Values =
        [
            Num("size", "Tamanho", 8, 400, " px", s => s.FontSize, (s, v) => s with { FontSize = v }, 72),
            Num("spacing", "Entrelinhas", 50, 300, "%", s => s.LineSpacing * 100, (s, v) => s with { LineSpacing = v / 100 }, 100),
            Num("maxwidth", "Largura máxima", 10, 100, "%", s => s.MaxWidth * 100, (s, v) => s with { MaxWidth = v / 100 }, 90),
        ];
        Outline = [Num("outline", "Espessura", 0, 40, " px", s => s.OutlineWidth, (s, v) => s with { OutlineWidth = v }, 0)];
        ShadowValues =
        [
            Num("shadow.x", "Deslocamento X", -60, 60, " px", s => s.ShadowOffsetX, (s, v) => s with { ShadowOffsetX = v }, 4),
            Num("shadow.y", "Deslocamento Y", -60, 60, " px", s => s.ShadowOffsetY, (s, v) => s with { ShadowOffsetY = v }, 4),
            Num("shadow.blur", "Desfoque", 0, 60, " px", s => s.ShadowBlur, (s, v) => s with { ShadowBlur = v }, 6),
        ];
        BoxValues =
        [
            Num("box.pad", "Margem interna", 0, 120, " px", s => s.BackgroundPadding, (s, v) => s with { BackgroundPadding = v }, 16),
            Num("box.radius", "Cantos", 0, 60, " px", s => s.BackgroundRadius, (s, v) => s with { BackgroundRadius = v }, 6),
        ];
        AnimationValues =
        [
            new ClipValueViewModel(this, "anim.in", "Duração da entrada", 0, 5, " s",
                c => Format(c).AnimationInDuration.TotalSeconds,
                (c, v) => c with { TextFormat = Format(c) with { AnimationInDuration = MediaTime.FromSecondsApprox(v) } }, 0.5, 0.1),
            new ClipValueViewModel(this, "anim.out", "Duração da saída", 0, 5, " s",
                c => Format(c).AnimationOutDuration.TotalSeconds,
                (c, v) => c with { TextFormat = Format(c) with { AnimationOutDuration = MediaTime.FromSecondsApprox(v) } }, 0.5, 0.1),
        ];

        TextColor = new(this, "color", "Cor do texto", s => s.Color, (s, v) => s with { Color = v }, TextPresetsColors);
        OutlineColor = new(this, "outline.color", "Cor do contorno", s => s.OutlineColor, (s, v) => s with { OutlineColor = v }, TextPresetsColors);
        ShadowColor = new(this, "shadow.color", "Cor da sombra", s => s.ShadowColor, (s, v) => s with { ShadowColor = v }, ["#B4000000", "#FF000000", "#80FFFFFF", "#B43D9BFF"]);
        BackgroundColor = new(this, "box.color", "Cor da caixa", s => s.BackgroundColor, (s, v) => s with { BackgroundColor = v }, BoxPresetsColors);
        AccentColor = new(this, "box.accent", "Barra de destaque", s => s.AccentColor, (s, v) => s with { AccentColor = v }, ["#00000000", "#3D9BFF", "#FFD400", "#FF4D4D", "#2ECC71"]);

        Timeline.Invalidated += OnTimelineInvalidated;
        Refresh();
    }

    private ClipValueViewModel Num(string key, string label, double min, double max, string unit, Func<TextStyle, double> get, Func<TextStyle, double, TextStyle> set,
        double defaultValue) =>
        new(this, key, label, min, max, unit, c => get(Format(c).Style), (c, v) => c with { TextFormat = Format(c) with { Style = set(Format(c).Style, v) } },
            defaultValue, unit == "%" ? 1 : 1);

    private static TextFormat Format(Clip c) => c.TextFormat ?? TextFormat.Default;

    public TimelineViewModel Timeline { get; }

    public ObservableCollection<string> Fonts { get; }
    public IReadOnlyList<ClipValueViewModel> Values { get; }
    public IReadOnlyList<ClipValueViewModel> Outline { get; }
    public IReadOnlyList<ClipValueViewModel> ShadowValues { get; }
    public IReadOnlyList<ClipValueViewModel> BoxValues { get; }
    public IReadOnlyList<ClipValueViewModel> AnimationValues { get; }
    public TextColorViewModel TextColor { get; }
    public TextColorViewModel OutlineColor { get; }
    public TextColorViewModel ShadowColor { get; }
    public TextColorViewModel BackgroundColor { get; }
    public TextColorViewModel AccentColor { get; }

    public IReadOnlyList<Option<TextAlign>> Alignments { get; } =
        [new(TextAlign.Left, "Esquerda"), new(TextAlign.Center, "Centro"), new(TextAlign.Right, "Direita")];

    public IReadOnlyList<Option<TextAnchor>> Anchors { get; } =
        [new(TextAnchor.Top, "Topo do bloco"), new(TextAnchor.Center, "Centro do bloco"), new(TextAnchor.Bottom, "Base do bloco")];

    public IReadOnlyList<Option<TextAnimationKind>> Animations { get; } =
    [
        new(TextAnimationKind.None, "Nenhuma"),
        new(TextAnimationKind.Fade, "Esmaecer"),
        new(TextAnimationKind.SlideUp, "Deslizar para cima"),
        new(TextAnimationKind.SlideDown, "Deslizar para baixo"),
        new(TextAnimationKind.SlideLeft, "Deslizar para a esquerda"),
        new(TextAnimationKind.SlideRight, "Deslizar para a direita"),
        new(TextAnimationKind.Zoom, "Zoom"),
        new(TextAnimationKind.Pop, "Saltar (pop)"),
        new(TextAnimationKind.Typewriter, "Máquina de escrever"),
    ];

    [ObservableProperty]
    public partial bool HasClip { get; set; }

    [ObservableProperty]
    public partial string KindLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSubtitle { get; set; }

    [ObservableProperty]
    public partial bool IsCredits { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial string? FontFamily { get; set; }

    [ObservableProperty]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    public partial bool Italic { get; set; }

    [ObservableProperty]
    public partial bool Underline { get; set; }

    [ObservableProperty]
    public partial bool Shadow { get; set; }

    [ObservableProperty]
    public partial Option<TextAlign>? Alignment { get; set; }

    [ObservableProperty]
    public partial Option<TextAnchor>? Anchor { get; set; }

    [ObservableProperty]
    public partial Option<TextAnimationKind>? AnimationIn { get; set; }

    [ObservableProperty]
    public partial Option<TextAnimationKind>? AnimationOut { get; set; }

    // ---------------------------------------------------------------- estado

    private void OnTimelineInvalidated(object? sender, EventArgs e) => Refresh();

    private (Track Track, Clip Clip)? Current()
    {
        if (Timeline.Sequence is not { } seq || Timeline.Selection.Count == 0) return null;
        foreach (var t in seq.Tracks)
            foreach (var c in t.Clips)
                if (c.IsText && Timeline.Selection.Contains(c.Id)) return (t, c);
        return null;
    }

    public MediaTime Offset(Clip c)
    {
        var offset = Timeline.FrameRate.SnapToFrame(Timeline.Playhead) - c.Start;
        return MediaTime.Max(MediaTime.Zero, MediaTime.Min(c.Duration, offset));
    }

    private void Refresh()
    {
        var current = Current();
        HasClip = current is not null;
        if (current is not { } cur)
        {
            _clipId = null;
            return;
        }
        var clip = cur.Clip;
        var format = Format(clip);
        var style = format.Style;
        _updating = true;
        try
        {
            KindLabel = format.Kind switch
            {
                TextKind.Title => "Título",
                TextKind.Credits => "Créditos rolantes",
                TextKind.LowerThird => "Tarja",
                TextKind.Subtitle => "Legenda",
                _ => "Texto",
            };
            IsSubtitle = cur.Track.Kind == TrackKind.Subtitle;
            IsCredits = format.Kind == TextKind.Credits;
            // Não sobrescreve o que está sendo digitado (o modelo pode ter normalizado quebras de linha).
            if (_clipId != clip.Id || Normalize(Text) != Normalize(clip.Text ?? "")) Text = clip.Text ?? "";
            if (!Fonts.Contains(style.FontFamily, StringComparer.OrdinalIgnoreCase)) Fonts.Add(style.FontFamily);
            FontFamily = style.FontFamily;
            Bold = style.Bold;
            Italic = style.Italic;
            Underline = style.Underline;
            Shadow = style.Shadow;
            Alignment = Alignments.First(a => a.Value == style.Align);
            Anchor = Anchors.First(a => a.Value == format.Anchor);
            AnimationIn = Animations.First(a => a.Value == format.AnimationIn);
            AnimationOut = Animations.First(a => a.Value == format.AnimationOut);
            foreach (var v in Values.Concat(Outline).Concat(ShadowValues).Concat(BoxValues).Concat(AnimationValues)) v.Refresh(clip);
            foreach (var c in new[] { TextColor, OutlineColor, ShadowColor, BackgroundColor, AccentColor }) c.Refresh(style);
        }
        finally
        {
            _updating = false;
        }
        _clipId = clip.Id;
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    // ---------------------------------------------------------------- edição

    public void UpdateClip(Func<Clip, Clip> change, string description, string? mergeKey = null)
    {
        if (Current() is not { } cur) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, change, description,
            mergeKey is null ? null : $"{cur.Clip.Id}:text:{mergeKey}"));
    }

    public void UpdateStyle(Func<TextStyle, TextStyle> change, string description, string? mergeKey = null) =>
        UpdateClip(c => c with { TextFormat = Format(c) with { Style = change(Format(c).Style) } }, description, mergeKey);

    private void UpdateFormat(Func<TextFormat, TextFormat> change, string description) =>
        UpdateClip(c => c with { TextFormat = change(Format(c)) }, description);

    // Sem efeitos nesta aba (o IClipEditor é compartilhado com os controles deslizantes).
    public void UpdateEffect(Guid effectId, Func<EffectInstance, EffectInstance> change, string description, string? mergeKey) { }
    public void RemoveEffect(Guid effectId) { }
    public void MoveEffect(Guid effectId, int direction) { }
    public void ChooseLut(EffectItemViewModel item) { }

    partial void OnTextChanged(string value)
    {
        if (_updating) return;
        var text = Normalize(value);
        if (text.Trim().Length == 0) return; // texto vazio some do quadro; mantém o último conteúdo válido
        UpdateClip(c => c with { Text = text }, "Editar texto", "content");
    }

    partial void OnFontFamilyChanged(string? value)
    {
        if (!_updating && !string.IsNullOrWhiteSpace(value)) UpdateStyle(s => s with { FontFamily = value }, "Alterar fonte");
    }

    partial void OnBoldChanged(bool value)
    {
        if (!_updating) UpdateStyle(s => s with { Bold = value }, value ? "Negrito" : "Remover negrito");
    }

    partial void OnItalicChanged(bool value)
    {
        if (!_updating) UpdateStyle(s => s with { Italic = value }, value ? "Itálico" : "Remover itálico");
    }

    partial void OnUnderlineChanged(bool value)
    {
        if (!_updating) UpdateStyle(s => s with { Underline = value }, value ? "Sublinhado" : "Remover sublinhado");
    }

    partial void OnShadowChanged(bool value)
    {
        if (!_updating) UpdateStyle(s => s with { Shadow = value }, value ? "Ativar sombra" : "Desativar sombra");
    }

    partial void OnAlignmentChanged(Option<TextAlign>? value)
    {
        if (!_updating && value is not null) UpdateStyle(s => s with { Align = value.Value }, "Alinhar texto");
    }

    partial void OnAnchorChanged(Option<TextAnchor>? value)
    {
        if (!_updating && value is not null) UpdateFormat(f => f with { Anchor = value.Value }, "Ancorar texto");
    }

    partial void OnAnimationInChanged(Option<TextAnimationKind>? value)
    {
        if (!_updating && value is not null) UpdateFormat(f => f with { AnimationIn = value.Value }, "Animação de entrada");
    }

    partial void OnAnimationOutChanged(Option<TextAnimationKind>? value)
    {
        if (!_updating && value is not null) UpdateFormat(f => f with { AnimationOut = value.Value }, "Animação de saída");
    }

    /// <summary>Copia a formatação desta legenda para todas as legendas da mesma trilha (estilo único das legendas).</summary>
    [RelayCommand]
    private void ApplyStyleToTrack()
    {
        if (Current() is not { } cur) return;
        var format = Format(cur.Clip);
        var commands = cur.Track.Clips.Where(c => c.IsText && c.Id != cur.Clip.Id)
            .Select(c => (IEditCommand)new UpdateClipCommand(Timeline.SequenceId, c.Id,
                x => x with { TextFormat = format, Transform = cur.Clip.Transform }, "Estilo da legenda"))
            .ToList();
        if (commands.Count == 0) return;
        Timeline.Execute(new CompositeCommand($"Aplicar estilo a {commands.Count + 1} legendas", commands));
    }

    /// <summary>Adiciona uma fonte (.ttf/.otf/.ttc) à pasta de fontes do aplicativo, sem instalar no Windows.</summary>
    [RelayCommand]
    private void AddFont()
    {
        var file = _dialogs.PickFile("Adicionar fonte", "Fontes (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc", null);
        if (file is null || FontCatalog.UserFontsFolder is not { } folder) return;
        try
        {
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, Path.GetFileName(file));
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(file, target, overwrite: true);
            var before = Fonts.ToHashSet(StringComparer.OrdinalIgnoreCase);
            FontCatalog.Refresh();
            var added = FontCatalog.Families().Where(f => !before.Contains(f)).ToList();
            foreach (var f in added) Fonts.Add(f);
            if (added.Count == 0)
            {
                _dialogs.ShowInfo("Adicionar fonte", "A fonte já estava disponível ou o arquivo não é uma fonte válida.");
                return;
            }
            if (HasClip) FontFamily = added[0];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowInfo("Adicionar fonte", $"Não foi possível copiar a fonte: {ex.Message}");
        }
    }

    public void Dispose() => Timeline.Invalidated -= OnTimelineInvalidated;
}
