using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Microsoft.Extensions.DependencyInjection;
using MeuEditorDeVideo.Desktop.Converters;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>Base comum: ligação ao <see cref="TimelineViewModel"/>, pincéis do tema e texto.</summary>
public abstract class TimelineSurface : Control
{
    /// <summary>Formato de arrastar-e-soltar de mídias da biblioteca para a linha do tempo (o valor é o Guid da mídia, em texto).</summary>
    public const string MediaDragFormat = "MeuEditorDeVideo.MediaAsset";

    /// <summary>O mesmo formato como <see cref="DataFormat"/> (API de arrastar do Avalonia 12) para quem inicia o arraste.</summary>
    public static readonly DataFormat<string> MediaDataFormat = DataFormat.CreateStringApplicationFormat(MediaDragFormat);

    public static readonly StyledProperty<TimelineViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<TimelineSurface, TimelineViewModel?>(nameof(ViewModel));

    static TimelineSurface()
    {
        ViewModelProperty.Changed.AddClassHandler<TimelineSurface>((s, e) =>
            s.OnViewModelChanged(e.OldValue as TimelineViewModel, e.NewValue as TimelineViewModel));
    }

    public TimelineViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    protected TimelineSurface()
    {
        Focusable = true;
        // As cores Med.* vêm do tema: ao trocar claro/escuro o desenho é refeito.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ClipToBounds = true;
        UseLayoutRounding = true;
    }

    protected virtual void OnViewModelChanged(TimelineViewModel? old, TimelineViewModel? now)
    {
        if (old is not null) old.Invalidated -= OnInvalidated;
        if (now is not null) now.Invalidated += OnInvalidated;
        InvalidateVisual();
    }

    private void OnInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>Atalhos configuráveis (RF212); nulo fora do aplicativo (ex.: testes de desenho).</summary>
    private static ShortcutService? Shortcuts => App.ServicesOrNull?.GetService<ShortcutService>();

    /// <summary>Atalhos da linha do tempo (RF049, RF058, RF060, RF212): só valem com o foco na régua ou nas trilhas.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { } vm) return;
        var shortcuts = Shortcuts;
        var action = shortcuts?.Find(e.Key.ToMed(), e.KeyModifiers.ToMed());
        if (action == ShortcutCatalog.PlayPause)
        {
            vm.RequestPlayPause();
            e.Handled = true;
            return;
        }
        System.Windows.Input.ICommand? command = action switch
        {
            ShortcutCatalog.StepBack => vm.StepBackCommand,
            ShortcutCatalog.StepForward => vm.StepForwardCommand,
            ShortcutCatalog.StepBackSecond => vm.StepBackSecondCommand,
            ShortcutCatalog.StepForwardSecond => vm.StepForwardSecondCommand,
            ShortcutCatalog.GoToStart => vm.GoToStartCommand,
            ShortcutCatalog.GoToEnd => vm.GoToEndCommand,
            ShortcutCatalog.PreviousEdit => vm.PreviousEditPointCommand,
            ShortcutCatalog.NextEdit => vm.NextEditPointCommand,
            ShortcutCatalog.Split => vm.SplitCommand,
            ShortcutCatalog.Delete => vm.DeleteCommand,
            ShortcutCatalog.RippleDelete => vm.RippleDeleteCommand,
            ShortcutCatalog.Copy => vm.CopyCommand,
            ShortcutCatalog.Cut => vm.CutCommand,
            ShortcutCatalog.Paste => vm.PasteCommand,
            ShortcutCatalog.Duplicate => vm.DuplicateCommand,
            ShortcutCatalog.SelectAll => vm.SelectAllCommand,
            ShortcutCatalog.Group => vm.GroupCommand,
            ShortcutCatalog.Ungroup => vm.UngroupCommand,
            ShortcutCatalog.Marker => vm.AddMarkerCommand,
            ShortcutCatalog.Snap => vm.ToggleSnapCommand,
            ShortcutCatalog.ZoomIn => vm.ZoomInCommand,
            ShortcutCatalog.ZoomOut => vm.ZoomOutCommand,
            ShortcutCatalog.ZoomFit => vm.ZoomToFitCommand,
            ShortcutCatalog.Camera1 or ShortcutCatalog.Camera2 or ShortcutCatalog.Camera3 or ShortcutCatalog.Camera4 => vm.SwitchCameraCommand,
            _ => null,
        };
        if (command is null) return;
        object? parameter = command == vm.SwitchCameraCommand ? action![^1..] : null;
        if (command.CanExecute(parameter)) command.Execute(parameter);
        e.Handled = true;
    }

    protected IBrush Res(string key, IBrush fallback) => this.Brush(key, fallback);

    // O desenho roda a cada quadro da reprodução: fontes, pincéis e cores analisadas são criados uma vez só.
    private static readonly CultureInfo TextCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Typeface Regular = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface SemiBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Dictionary<uint, IBrush> SolidBrushes = [];
    private static readonly Dictionary<string, IBrush?> ParsedBrushes = new(StringComparer.OrdinalIgnoreCase);

    protected static FormattedText Text(string text, double size, IBrush brush, bool bold = false) =>
        new(text, TextCulture, FlowDirection.LeftToRight, bold ? SemiBold : Regular, size, brush);

    /// <summary>Pincel sólido imutável, reaproveitado entre os desenhos (só na thread da interface).</summary>
    protected static IBrush Solid(byte a, byte r, byte g, byte b)
    {
        var key = (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | b;
        if (!SolidBrushes.TryGetValue(key, out var brush))
            SolidBrushes[key] = brush = new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));
        return brush;
    }

    /// <summary>Pincel de uma cor em texto (#RRGGBB, nome), imutável e guardado; nulo quando a cor é inválida.</summary>
    internal static IBrush? ParseBrush(string color)
    {
        if (ParsedBrushes.TryGetValue(color, out var cached)) return cached;
        IBrush? brush = Color.TryParse(color, out var parsed) ? new ImmutableSolidColorBrush(parsed) : null;
        if (ParsedBrushes.Count > 512) ParsedBrushes.Clear();
        ParsedBrushes[color] = brush;
        return brush;
    }

    protected static Pen MakePen(IBrush brush, double thickness, IDashStyle? dash = null) => new(brush, thickness, dash);
}

/// <summary>Régua de tempo (RF057): marcas adaptadas ao zoom, marcadores e cabeça do cursor. Clique/arraste posiciona o cursor.</summary>
public sealed class TimelineRuler : TimelineSurface
{
    private bool _scrubbing;
    private Guid? _draggingMarker;
    private Point _down;

    public TimelineRuler()
    {
        AutomationProperties.SetName(this, "Régua de tempo");
        // O menu só aparece sobre marcadores (este manipulador roda antes de o menu de contexto abrir).
        AddHandler(ContextRequestedEvent, (_, e) => { if (ContextMarker is null) e.Handled = true; }, RoutingStrategies.Tunnel);
    }

    /// <summary>Marcador sob o último clique direito (para o menu de contexto).</summary>
    public Guid? ContextMarker { get; private set; }

    private static readonly double[] Steps = [1.0 / 30, 1.0 / 10, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var muted = Res("Med.TextMuted", Brushes.Gray);
        context.DrawRectangle(Res("Med.Panel", Brushes.Black), null, new Rect(0, 0, w, h));
        context.DrawLine(MakePen(Res("Med.Border", Brushes.DimGray), 1), new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (ViewModel is not { } vm) return;

        var pps = vm.PixelsPerSecond;
        var step = Steps.FirstOrDefault(s => s * pps >= 90, 3600);
        var minor = step / 5;
        var tickPen = MakePen(muted, 1);
        var startSec = vm.HorizontalOffset / pps;
        var endSec = (vm.HorizontalOffset + w) / pps;

        for (var i = (long)Math.Floor(startSec / minor); i * minor <= endSec; i++)
        {
            var sec = i * minor;
            var x = Math.Round(sec * pps - vm.HorizontalOffset) + 0.5;
            var major = i % 5 == 0;
            context.DrawLine(tickPen, new Point(x, h - (major ? 10 : 5)), new Point(x, h));
            if (major) context.DrawText(Text(Label(sec, step, vm.FrameRate), 10.5, muted), new Point(x + 3, 3));
        }

        if (vm.Sequence is { } seq)
        {
            foreach (var m in seq.Markers)
            {
                var x = vm.TimeToX(m.Time);
                if (x < -10 || x > w + 10) continue;
                var brush = MarkerBrush(m);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x - 5, h - 14), true);
                    c.LineTo(new Point(x + 5, h - 14), false);
                    c.LineTo(new Point(x + 5, h - 6), false);
                    c.LineTo(new Point(x, h - 1), false);
                    c.LineTo(new Point(x - 5, h - 6), false);
                    c.EndFigure(true);
                }
                context.DrawGeometry(brush, null, g);
                if (!string.IsNullOrEmpty(m.Name)) context.DrawText(Text(m.Name, 10, brush), new Point(x + 7, h - 15));
            }
        }

        var px = vm.TimeToX(vm.Playhead);
        var playhead = Res("Med.Playhead", Brushes.DodgerBlue);
        // Alça arredondada no alto da régua, com a linha descendo até as trilhas
        var lx = Math.Round(px) + 0.5;
        context.DrawLine(MakePen(playhead, 2), new Point(lx, 8), new Point(lx, h));
        context.DrawRectangle(playhead, null, new Rect(px - 6, 1, 12, 14), 6, 6);
    }

    /// <summary>Pincel da cor de identificação do clipe; nulo quando automática ou inválida.</summary>
    internal static IBrush? LabelBrush(string? color) => string.IsNullOrEmpty(color) ? null : ParseBrush(color);

    internal static IBrush MarkerBrush(Marker m) => (string.IsNullOrEmpty(m.Color) ? null : ParseBrush(m.Color)) ?? Brushes.DodgerBlue;

    private static string Label(double sec, double step, FrameRate fr)
    {
        var total = (long)Math.Round(sec * 1000);
        var s = total / 1000;
        var text = s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        if (step < 1)
        {
            var frame = (long)Math.Round((sec - s) * fr.Value);
            text += $":{frame:00}";
        }
        return text;
    }

    private Guid? MarkerAt(double x)
    {
        if (ViewModel?.Sequence is not { } seq) return null;
        return seq.Markers.Where(m => Math.Abs(ViewModel.TimeToX(m.Time) - x) <= 6).Select(m => (Guid?)m.Id).FirstOrDefault();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            ContextMarker = MarkerAt(e.GetPosition(this).X);
            return;
        }
        if (!props.IsLeftButtonPressed || ViewModel is not { HasSession: true } vm) return;
        Focus();
        _down = e.GetPosition(this);
        if (e.ClickCount == 2 && MarkerAt(_down.X) is { } dbl)
        {
            vm.RenameMarker(dbl);
            e.Handled = true;
            return;
        }
        _draggingMarker = _down.Y > Bounds.Height - 16 ? MarkerAt(_down.X) : null;
        _scrubbing = _draggingMarker is null;
        if (_scrubbing) vm.SetPlayhead(vm.XToTime(_down.X), keepVisible: false);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is not { } vm || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var x = e.GetPosition(this).X;
        if (_scrubbing) vm.SetPlayhead(vm.XToTime(x), keepVisible: false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (ViewModel is { } vm && _draggingMarker is { } id)
        {
            var x = e.GetPosition(this).X;
            if (Math.Abs(x - _down.X) > 2)
            {
                var snap = vm.CreateSnap().Snap(vm.XToTime(x), vm.SnapTolerance, vm.SnapEnabled);
                vm.MoveMarker(id, snap.Time);
            }
        }
        _scrubbing = false;
        _draggingMarker = null;
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _scrubbing = false;
        _draggingMarker = null;
    }
}

/// <summary>
/// Área das trilhas (RF037–RF064): desenha clipes (miniaturas no vídeo, forma de onda no áudio), marcadores e cursor;
/// trata seleção, mover (inclusive entre trilhas), aparar bordas, rolagem/zoom com a roda e soltar mídias da biblioteca.
/// </summary>
public sealed class TimelineControl : TimelineSurface
{
    private const double EdgeZone = 6;
    private const double DragThreshold = 3;

    private enum Mode { None, Scrub, Move, Trim }

    private Mode _mode;
    private Point _down;
    private bool _dragStarted;
    private Clip? _primary;
    private Track? _primaryTrack;
    private ClipEdge _edge;
    private IReadOnlySet<Guid> _moving = new HashSet<Guid>();

    // Estado de pré-visualização do arraste.
    private MediaTime _moveDelta;
    private int _trackSteps;
    private MediaTime _trimTime;
    private MediaTime? _snapGuide;
    private (Guid? TrackId, MediaTime At)? _dropPreview;

    public TimelineControl()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AutomationProperties.SetName(this, "Trilhas da linha do tempo");
    }

    protected override void OnViewModelChanged(TimelineViewModel? old, TimelineViewModel? now)
    {
        base.OnViewModelChanged(old, now);
        if (now is not null && Bounds.Width > 0) now.ViewportWidth = Bounds.Width;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (ViewModel is { } vm) vm.ViewportWidth = finalSize.Width;
        return base.ArrangeOverride(finalSize);
    }

    // ---------------------------------------------------------------- desenho

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        context.DrawRectangle(Res("Med.PanelAlt", Brushes.Black), null, new Rect(0, 0, w, h));
        if (ViewModel is not { } vm || vm.Sequence is not { } seq) return;

        var border = MakePen(Res("Med.Border", Brushes.DimGray), 1);
        var text = Res("Med.Text", Brushes.White);
        var accent = Res("Med.Accent", Brushes.DodgerBlue);
        var selectedPen = MakePen(Brushes.White, 2);
        var linkedPen = MakePen(accent, 1.5);

        double top = 0;
        foreach (var header in vm.Tracks)
        {
            var track = seq.Tracks.Find(t => t.Id == header.Id) ?? header.Track;
            var th = header.Height;
            if (_dropPreview is { TrackId: { } dt } && dt == track.Id)
                context.DrawRectangle(Solid(40, 30, 123, 255), null, new Rect(0, top, w, th));
            context.DrawLine(border, new Point(0, top + th - 0.5), new Point(w, top + th - 0.5));

            foreach (var clip in track.Clips)
            {
                var isMoving = _dragStarted && _mode == Mode.Move && _moving.Contains(clip.Id);
                var isTrimming = _dragStarted && _mode == Mode.Trim && _primary is { } p && (p.Id == clip.Id || (p.LinkId is { } l && clip.LinkId == l));
                var shown = isTrimming ? PreviewTrim(vm, clip) : clip;
                DrawClip(context, vm, track, shown, top, th, text, vm.Selection.Contains(clip.Id) ? selectedPen : null,
                    clip.LinkId is not null ? linkedPen : null, ghost: isMoving ? 0.35 : 1);
            }

            // Transições: faixa centrada no corte (RF084) ou, na entrada, a partir do início do clipe.
            foreach (var tr in track.Transitions)
            {
                var to = track.Clips.Find(c => c.Id == tr.ToClipId);
                if (to is null) continue;
                var half = tr.Duration.TotalSeconds * vm.PixelsPerSecond / 2;
                // Entrada (sem clipe antes): a faixa começa no início do clipe.
                var cx = vm.TimeToX(to.Start) + (tr.IsEntrance ? half : 0);
                if (cx + half < 0 || cx - half > w) continue;
                var band = new Rect(cx - half, top + th / 2 - 7, Math.Max(4, half * 2), 14);
                context.DrawRectangle(Solid(220, 255, 255, 255), MakePen(Brushes.Black, 1), band, 3, 3);
                context.DrawLine(MakePen(Brushes.Black, 1), new Point(band.Left + 2, band.Bottom - 2), new Point(band.Right - 2, band.Top + 2));
            }

            if (track.IsLocked)
                context.DrawRectangle(Solid(70, 0, 0, 0), null, new Rect(0, top, w, th - 1));
            top += th;
        }

        // Clipes sendo movidos, na posição de destino.
        if (_dragStarted && _mode == Mode.Move)
        {
            foreach (var (clip, targetId) in MovePreview(seq))
            {
                var tt = vm.TrackTop(targetId);
                var target = seq.Tracks.Find(t => t.Id == targetId);
                if (tt < 0 || target is null) continue;
                DrawClip(context, vm, target, clip, tt, TimelineViewModel.HeightOf(target.Kind), text, selectedPen, null, ghost: 0.85);
            }
        }

        // Marcadores atravessando as trilhas.
        foreach (var m in seq.Markers)
        {
            var x = Math.Round(vm.TimeToX(m.Time)) + 0.5;
            if (x < 0 || x > w) continue;
            context.DrawLine(MakePen(TimelineRuler.MarkerBrush(m), 1, DashStyle.Dash), new Point(x, 0), new Point(x, h));
        }

        if (_snapGuide is { } guide)
        {
            var x = Math.Round(vm.TimeToX(guide)) + 0.5;
            context.DrawLine(MakePen(Brushes.Gold, 1), new Point(x, 0), new Point(x, h));
        }

        if (_dropPreview is { } drop)
        {
            var x = Math.Round(vm.TimeToX(drop.At)) + 0.5;
            context.DrawLine(MakePen(accent, 2), new Point(x, 0), new Point(x, h));
        }

        var px = Math.Round(vm.TimeToX(vm.Playhead)) + 0.5;
        context.DrawLine(MakePen(Res("Med.Playhead", Brushes.DodgerBlue), 2), new Point(px, 0), new Point(px, h));
    }

    private void DrawClip(DrawingContext dc, TimelineViewModel vm, Track track, Clip clip, double top, double th, IBrush text,
        Pen? selected, Pen? linked, double ghost)
    {
        var x1 = vm.TimeToX(clip.Start);
        var x2 = vm.TimeToX(clip.End);
        if (x2 < -2 || x1 > Bounds.Width + 2) return;
        var rect = new Rect(x1 + 0.5, top + 3, Math.Max(2, x2 - x1 - 1), th - 7);

        var asset = clip.MediaAssetId is { } id ? vm.Project?.FindMedia(id) : null;
        var item = asset is not null ? vm.Library.Find(asset.Id) : null;
        var fill = TimelineRuler.LabelBrush(clip.LabelColor) ?? track.Kind switch
        {
            TrackKind.Audio => Res("Med.ClipAudio", Brushes.MediumPurple),
            TrackKind.Text or TrackKind.Subtitle => Res("Med.ClipText", Brushes.MediumPurple),
            _ when asset?.Kind is MediaKind.Image => Res("Med.ClipImage", Brushes.SeaGreen),
            _ => Res("Med.ClipVideo", Brushes.RoyalBlue),
        };

        using var opacity = dc.PushOpacity(ghost * (track.IsHidden || !clip.Enabled || (track.IsMuted && track.Kind == TrackKind.Audio) ? 0.45 : 1));
        dc.DrawRectangle(fill, null, rect, 4, 4);
        using (dc.PushClip(new RoundedRect(rect, 4)))
        {
            // Miniaturas repetidas ao longo do clipe de vídeo/imagem.
            if (track.Kind != TrackKind.Audio && item?.Thumbnail is { } thumb && rect.Height > 24)
            {
                var bitmap = BgraBitmaps.ToBitmap(thumb);
                var source = new Rect(bitmap.Size);
                var tileH = rect.Height - 18;
                var tileW = Math.Max(8, tileH * thumb.Width / Math.Max(1, thumb.Height));
                var startX = Math.Max(rect.Left, rect.Left + Math.Floor((-rect.Left) / tileW) * tileW);
                for (var x = startX; x < Math.Min(rect.Right, Bounds.Width); x += tileW)
                    dc.DrawImage(bitmap, source, new Rect(x, rect.Top + 17, tileW, tileH));
            }

            // Forma de onda do trecho usado (considera ponto de entrada e velocidade).
            if (track.Kind == TrackKind.Audio && item?.Peaks is { Count: > 0 } peaks)
                DrawWaveform(dc, vm, clip, peaks, rect);

            // Faixa do título.
            dc.DrawRectangle(Solid(110, 0, 0, 0), null, new Rect(rect.Left, rect.Top, rect.Width, 16));
            var label = asset?.Name ?? (clip.IsText ? Presentation.ViewModels.Inspector.ClipInspectorViewModel.TextTitle(clip.Text!)
                : clip.Shape is { } shapeStyle ? "◆ " + ShapePresets.Name(shapeStyle)
                : clip.IsAdjustment ? "☰ Camada de ajuste"
                : clip.IsCompound ? "⧉ " + (vm.Project?.Sequences.Find(s => s.Id == clip.NestedSequenceId)?.Name ?? "Clipe composto")
                : "Clipe");
            if (!clip.Enabled) label = "⊘ " + label;
            if (clip.Masks.Count > 0) label = "◐ " + label;
            if (clip.Stabilization is { Enabled: true }) label = "⌖ " + label;
            if (clip.MotionLink is not null) label = "⛓ " + label;
            if (clip.Speed != Rational.One)
                label += " · " + clip.Speed.Value.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + "×";
            if (clip.GroupId is not null) label = "▣ " + label;
            if (clip.FreezeAt is not null) label = "❄ " + label;
            if (clip.Reverse) label = "◀ " + label;
            if (!clip.Effects.IsEmpty) label = "fx " + label;
            if (asset is null && clip.MediaAssetId is not null) label = "⚠ Mídia removida";
            var ft = Text(label, 11, text, bold: true);
            ft.MaxTextWidth = Math.Max(1, rect.Width - 8);
            ft.MaxTextHeight = 16;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(ft, new Point(Math.Max(rect.Left, 0) + 4, rect.Top + 1));

            // Entrada/saída gradual: rampas no canto superior.
            var fadePen = MakePen(Solid(200, 255, 255, 255), 1);
            if (clip.FadeIn > MediaTime.Zero)
                dc.DrawLine(fadePen, new Point(rect.Left, rect.Bottom), new Point(rect.Left + clip.FadeIn.TotalSeconds * vm.PixelsPerSecond, rect.Top + 16));
            if (clip.FadeOut > MediaTime.Zero)
                dc.DrawLine(fadePen, new Point(rect.Right - clip.FadeOut.TotalSeconds * vm.PixelsPerSecond, rect.Top + 16), new Point(rect.Right, rect.Bottom));

            // Keyframes (losangos na base do clipe).
            if (!clip.Keyframes.IsEmpty)
            {
                var diamond = Solid(255, 0xFF, 0xD2, 0x4A);
                foreach (var offset in clip.Keyframes.Values.SelectMany(k => k).Select(k => k.Offset).Distinct())
                {
                    var kx = vm.TimeToX(clip.Start + offset);
                    var ky = rect.Bottom - 6;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(kx, ky - 4), true);
                        ctx.LineTo(new Point(kx + 4, ky));
                        ctx.LineTo(new Point(kx, ky + 4));
                        ctx.LineTo(new Point(kx - 4, ky));
                        ctx.EndFigure(true);
                    }
                    dc.DrawGeometry(diamond, MakePen(Brushes.Black, 1), g);
                }
            }
        }

        if (linked is not null) dc.DrawRectangle(null, linked, rect, 4, 4);
        if (selected is not null) dc.DrawRectangle(null, selected, rect, 4, 4);
    }

    private void DrawWaveform(DrawingContext dc, TimelineViewModel vm, Clip clip, Core.Media.WaveformData peaks, Rect rect)
    {
        var from = Math.Max(rect.Left, 0);
        var to = Math.Min(rect.Right, Bounds.Width);
        if (to <= from) return;
        var mid = rect.Top + 16 + (rect.Height - 16) / 2;
        var amp = (rect.Height - 18) / 2;
        var speed = clip.Speed.Value;
        var pps = vm.PixelsPerSecond;

        var top = new List<Point>();
        var bottom = new List<Point>();
        for (var x = from; x <= to; x += 1)
        {
            // Segundos de origem cobertos por esta coluna de pixel.
            var t0 = clip.SourceIn.TotalSeconds + (x - rect.Left) / pps * speed;
            var t1 = t0 + speed / pps;
            int i0 = (int)(t0 * peaks.PeaksPerSecond), i1 = Math.Max(i0 + 1, (int)(t1 * peaks.PeaksPerSecond));
            float max = 0, min = 0;
            for (var i = Math.Max(0, i0); i < i1 && i < peaks.Count; i++)
            {
                if (peaks.Max[i] > max) max = peaks.Max[i];
                if (peaks.Min[i] < min) min = peaks.Min[i];
            }
            top.Add(new Point(x, mid - Math.Clamp(max, -1f, 1f) * amp));
            bottom.Add(new Point(x, mid - Math.Clamp(min, -1f, 1f) * amp));
        }

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(from, mid), true);
            foreach (var p in top) c.LineTo(p, false);
            for (var i = bottom.Count - 1; i >= 0; i--) c.LineTo(bottom[i], false);
            c.EndFigure(true);
        }
        dc.DrawGeometry(Solid(190, 255, 255, 255), null, g);
    }

    // ---------------------------------------------------------------- acertos de posição

    private (Track Track, Clip Clip, bool NearStart, bool NearEnd)? HitClip(Point p)
    {
        if (ViewModel is not { Sequence: { } seq } vm || vm.TrackAt(p.Y) is not { } header) return null;
        var track = seq.Tracks.Find(t => t.Id == header.Id);
        if (track is null) return null;
        foreach (var c in track.Clips)
        {
            var x1 = vm.TimeToX(c.Start);
            var x2 = vm.TimeToX(c.End);
            if (p.X < x1 - 1 || p.X > x2 + 1) continue;
            var zone = Math.Min(EdgeZone, (x2 - x1) / 4);
            return (track, c, p.X - x1 <= zone, x2 - p.X <= zone);
        }
        return null;
    }

    /// <summary>Clipe e trilha sob o ponto (coordenadas do controle), para testes e menus de contexto.</summary>
    public (Guid TrackId, Guid ClipId, bool NearStart, bool NearEnd)? ClipAt(Point p) =>
        HitClip(p) is { } hit ? (hit.Track.Id, hit.Clip.Id, hit.NearStart, hit.NearEnd) : null;

    // ---------------------------------------------------------------- ponteiro

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (ViewModel is not { HasSession: true } vm) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            Focus();
            if (HitClip(e.GetPosition(this)) is { } clicked) vm.SelectOnlyIfNotSelected(clicked.Clip.Id);
            else vm.ClearSelection();
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        Focus();
        _down = e.GetPosition(this);
        _dragStarted = false;
        _snapGuide = null;
        var additive = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0;

        if (HitClip(_down) is { } hit)
        {
            _primary = hit.Clip;
            _primaryTrack = hit.Track;
            if (!hit.Track.IsLocked && (hit.NearStart || hit.NearEnd))
            {
                _mode = Mode.Trim;
                _edge = hit.NearStart ? ClipEdge.Start : ClipEdge.End;
                _trimTime = _edge == ClipEdge.Start ? hit.Clip.Start : hit.Clip.End;
                vm.SelectOnlyIfNotSelected(hit.Clip.Id);
            }
            else
            {
                if (additive) vm.Select(hit.Clip.Id, additive: true);
                else vm.SelectOnlyIfNotSelected(hit.Clip.Id);
                _mode = hit.Track.IsLocked || !vm.Selection.Contains(hit.Clip.Id) ? Mode.None : Mode.Move;
                _moving = vm.Sequence is { } seq ? TimelineOps.Expand(seq, vm.Selection) : new HashSet<Guid>();
                _moveDelta = MediaTime.Zero;
                _trackSteps = 0;
            }
        }
        else
        {
            if (!additive) vm.ClearSelection();
            _mode = Mode.Scrub;
            vm.SetPlayhead(vm.XToTime(_down.X), keepVisible: false);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is not { } vm) return;
        var p = e.GetPosition(this);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _mode == Mode.None)
        {
            Cursor = ControlResources.Cursor(HitClip(p) is { Track.IsLocked: false } hit && (hit.NearStart || hit.NearEnd)
                ? StandardCursorType.SizeWestEast : null);
            return;
        }

        if (!_dragStarted && Math.Abs(p.X - _down.X) < DragThreshold && Math.Abs(p.Y - _down.Y) < DragThreshold) return;
        _dragStarted = true;
        var delta = Signed(vm, p.X - _down.X);

        switch (_mode)
        {
            case Mode.Scrub:
                vm.SetPlayhead(vm.XToTime(p.X), keepVisible: false);
                break;

            case Mode.Move when _primary is { } primary:
            {
                var snap = vm.CreateSnap(_moving).SnapRange(primary.Start + delta, primary.Duration, vm.SnapTolerance, vm.SnapEnabled);
                // Não deixa nenhum clipe do conjunto começar antes de zero.
                var earliest = vm.Sequence!.Tracks.SelectMany(t => t.Clips).Where(c => _moving.Contains(c.Id)).Min(c => c.Start);
                var d = snap.Time - primary.Start;
                if (earliest + d < MediaTime.Zero) d = -earliest;
                _moveDelta = d;
                _snapGuide = snap.Snapped ? snap.Target : null;
                _trackSteps = TrackSteps(vm, p.Y);
                InvalidateVisual();
                break;
            }

            case Mode.Trim when _primary is { } primary:
            {
                var original = _edge == ClipEdge.Start ? primary.Start : primary.End;
                var exclude = vm.Sequence is { } seq ? TimelineOps.Expand(seq, [primary.Id], linked: true, groups: false) : null;
                var snap = vm.CreateSnap(exclude).Snap(original + delta, vm.SnapTolerance, vm.SnapEnabled);
                _trimTime = snap.Time;
                _snapGuide = snap.Snapped ? snap.Target : null;
                InvalidateVisual();
                break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var vm = ViewModel;
        var mode = _mode;
        var dragged = _dragStarted;
        var primary = _primary;
        _mode = Mode.None;
        _dragStarted = false;
        _snapGuide = null;
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        if (vm is null) return;

        if (mode == Mode.Move && primary is not null)
        {
            if (dragged && vm.Sequence is { } seq)
            {
                var moves = MovePreview(seq).Select(m => new ClipMove(m.Clip.Id, m.TrackId, m.Clip.Start)).ToList();
                vm.MoveClips(moves);
            }
            else if (!dragged && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) == 0)
            {
                vm.Select(primary.Id, additive: false); // clique simples num clipe já selecionado: seleciona só ele
            }
        }
        else if (mode == Mode.Trim && primary is not null && dragged)
        {
            vm.TrimClip(primary.Id, _edge, _trimTime);
        }

        _primary = null;
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_mode == Mode.None) return;
        _mode = Mode.None;
        _dragStarted = false;
        _snapGuide = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (ViewModel is not { } vm) return;
        // Delta do WPF: 120 por "clique" da roda; o do Avalonia: 1 por clique.
        var notches = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            vm.SetZoom(vm.PixelsPerSecond * (notches > 0 ? 1.25 : 0.8), e.GetPosition(this).X);
            e.Handled = true;
        }
        else if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            vm.HorizontalOffset = Math.Clamp(vm.HorizontalOffset - notches * 120, 0, vm.MaxHorizontalOffset);
            e.Handled = true;
        }
    }

    /// <summary>Converte deslocamento em pixels (com sinal) em tempo.</summary>
    private static MediaTime Signed(TimelineViewModel vm, double px) =>
        MediaTime.FromSecondsApprox(px / vm.PixelsPerSecond);

    // ---------------------------------------------------------------- mover

    /// <summary>Trilhas para as quais um clipe da trilha informada pode ir (mesma família, desbloqueadas).</summary>
    private static List<Track> CompatibleTracks(Sequence seq, Track from) =>
        [.. seq.Tracks.Where(t => t.Id == from.Id || (!t.IsLocked && TimelineOps.Compatible(from.Kind, t.Kind)))];

    private int TrackSteps(TimelineViewModel vm, double y)
    {
        if (vm.Sequence is not { } seq || _primaryTrack is null || vm.TrackAt(y) is not { } hover) return _trackSteps;
        var list = CompatibleTracks(seq, _primaryTrack);
        var target = list.FindIndex(t => t.Id == hover.Id);
        if (target < 0) return _trackSteps; // trilha incompatível: mantém a última válida
        return target - list.FindIndex(t => t.Id == _primaryTrack.Id);
    }

    /// <summary>Posições de destino dos clipes movidos (o deslocamento vertical vale para a família da trilha do clipe principal).</summary>
    private IEnumerable<(Clip Clip, Guid TrackId)> MovePreview(Sequence seq)
    {
        var primaryVisual = _primaryTrack?.IsVisual ?? true;
        foreach (var track in seq.Tracks)
        {
            foreach (var c in track.Clips.Where(c => _moving.Contains(c.Id)))
            {
                var targetId = track.Id;
                if (_trackSteps != 0 && track.IsVisual == primaryVisual)
                {
                    var list = CompatibleTracks(seq, track);
                    var i = list.FindIndex(t => t.Id == track.Id) + _trackSteps;
                    targetId = list[Math.Clamp(i, 0, list.Count - 1)].Id;
                }
                yield return (c with { Start = c.Start + _moveDelta }, targetId);
            }
        }
    }

    private Clip PreviewTrim(TimelineViewModel vm, Clip c)
    {
        var frame = vm.FrameRate.FrameDuration;
        if (_edge == ClipEdge.End)
        {
            var end = MediaTime.Max(c.Start + frame, _trimTime - (_primary!.End - c.End));
            return c with { Duration = end - c.Start };
        }
        var start = MediaTime.Max(MediaTime.Zero, MediaTime.Min(c.End - frame, _trimTime));
        return c with { Start = start, Duration = c.End - start };
    }

    // ---------------------------------------------------------------- soltar mídia da biblioteca

    private static bool HasMedia(DragEventArgs e) => e.DataTransfer.Contains(MediaDataFormat);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { HasSession: true } vm || !HasMedia(e))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        var p = e.GetPosition(this);
        var at = vm.CreateSnap().Snap(vm.XToTime(p.X), vm.SnapTolerance, vm.SnapEnabled).Time;
        _dropPreview = (vm.TrackAt(p.Y)?.Id, at);
        InvalidateVisual();
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _dropPreview = null;
        InvalidateVisual();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var preview = _dropPreview;
        _dropPreview = null;
        InvalidateVisual();
        if (ViewModel is not { } vm || preview is null || e.DataTransfer.TryGetValue(MediaDataFormat) is not { } raw || !Guid.TryParse(raw, out var id))
            return;
        Focus();
        vm.DropMedia(id, preview.Value.TrackId, preview.Value.At);
    }
}
