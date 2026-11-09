using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>Base comum: ligação ao <see cref="TimelineViewModel"/>, pincéis do tema e texto.</summary>
public abstract class TimelineSurface : FrameworkElement
{
    /// <summary>Formato de arrastar-e-soltar de mídias da biblioteca para a linha do tempo.</summary>
    public const string MediaDragFormat = "MeuEditorDeVideo.MediaAsset";

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TimelineViewModel), typeof(TimelineSurface),
        new FrameworkPropertyMetadata(null, (d, e) => ((TimelineSurface)d).OnViewModelChanged(e.OldValue as TimelineViewModel, e.NewValue as TimelineViewModel)));

    public TimelineViewModel? ViewModel
    {
        get => (TimelineViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    protected TimelineSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>Exposto à automação/leitores de tela como painel nomeado (AutomationProperties.Name).</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    private void OnViewModelChanged(TimelineViewModel? old, TimelineViewModel? now)
    {
        if (old is not null) old.Invalidated -= OnInvalidated;
        if (now is not null) now.Invalidated += OnInvalidated;
        InvalidateVisual();
    }

    private void OnInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>Atalhos da linha do tempo (RF049, RF058, RF060): só valem com o foco na régua ou nas trilhas.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { } vm) return;
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            vm.RequestPlayPause();
            e.Handled = true;
            return;
        }
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        System.Windows.Input.ICommand? command = (e.Key, ctrl, shift) switch
        {
            (Key.Left, false, false) => vm.StepBackCommand,
            (Key.Right, false, false) => vm.StepForwardCommand,
            (Key.Left, false, true) => vm.StepBackSecondCommand,
            (Key.Right, false, true) => vm.StepForwardSecondCommand,
            (Key.Home, _, _) => vm.GoToStartCommand,
            (Key.End, _, _) => vm.GoToEndCommand,
            (Key.Up, false, false) => vm.PreviousEditPointCommand,
            (Key.Down, false, false) => vm.NextEditPointCommand,
            (Key.S, false, false) or (Key.K, true, false) => vm.SplitCommand,
            (Key.Delete, false, false) or (Key.Back, false, false) => vm.DeleteCommand,
            (Key.Delete, false, true) => vm.RippleDeleteCommand,
            (Key.C, true, false) => vm.CopyCommand,
            (Key.X, true, false) => vm.CutCommand,
            (Key.V, true, false) => vm.PasteCommand,
            (Key.D, true, false) => vm.DuplicateCommand,
            (Key.A, true, false) => vm.SelectAllCommand,
            (Key.G, true, false) => vm.GroupCommand,
            (Key.G, true, true) => vm.UngroupCommand,
            (Key.M, false, false) => vm.AddMarkerCommand,
            (Key.N, false, false) => vm.ToggleSnapCommand,
            (Key.OemPlus or Key.Add, true, false) => vm.ZoomInCommand,
            (Key.OemMinus or Key.Subtract, true, false) => vm.ZoomOutCommand,
            (Key.Z, false, true) => vm.ZoomToFitCommand,
            _ => null,
        };
        if (command is null) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    protected Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected FormattedText Text(string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected static Pen MakePen(Brush brush, double thickness, DashStyle? dash = null)
    {
        var p = new Pen(brush, thickness) { DashStyle = dash ?? DashStyles.Solid };
        p.Freeze();
        return p;
    }
}

/// <summary>Régua de tempo (RF057): marcas adaptadas ao zoom, marcadores e cabeça do cursor. Clique/arraste posiciona o cursor.</summary>
public sealed class TimelineRuler : TimelineSurface
{
    private bool _scrubbing;
    private Guid? _draggingMarker;
    private Point _down;

    /// <summary>Marcador sob o último clique direito (para o menu de contexto).</summary>
    public Guid? ContextMarker { get; private set; }

    private static readonly double[] Steps = [1.0 / 30, 1.0 / 10, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var muted = Res("Med.TextMuted", Brushes.Gray);
        dc.DrawRectangle(Res("Med.Panel", Brushes.Black), null, new Rect(0, 0, w, h));
        dc.DrawLine(MakePen(Res("Med.Border", Brushes.DimGray), 1), new Point(0, h - 0.5), new Point(w, h - 0.5));
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
            dc.DrawLine(tickPen, new Point(x, h - (major ? 10 : 5)), new Point(x, h));
            if (major) dc.DrawText(Text(Label(sec, step, vm.FrameRate), 10.5, muted), new Point(x + 3, 3));
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
                    c.BeginFigure(new Point(x - 5, h - 14), true, true);
                    c.LineTo(new Point(x + 5, h - 14), false, false);
                    c.LineTo(new Point(x + 5, h - 6), false, false);
                    c.LineTo(new Point(x, h - 1), false, false);
                    c.LineTo(new Point(x - 5, h - 6), false, false);
                }
                dc.DrawGeometry(brush, null, g);
                if (!string.IsNullOrEmpty(m.Name)) dc.DrawText(Text(m.Name, 10, brush), new Point(x + 7, h - 15));
            }
        }

        var px = vm.TimeToX(vm.Playhead);
        var playhead = Res("Med.Playhead", Brushes.DodgerBlue);
        var head = new StreamGeometry();
        using (var c = head.Open())
        {
            c.BeginFigure(new Point(px - 6, 0), true, true);
            c.LineTo(new Point(px + 6, 0), false, false);
            c.LineTo(new Point(px + 6, h - 10), false, false);
            c.LineTo(new Point(px, h), false, false);
            c.LineTo(new Point(px - 6, h - 10), false, false);
        }
        dc.DrawGeometry(playhead, null, head);
    }

    internal static Brush MarkerBrush(Marker m)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(m.Color));
            b.Freeze();
            return b;
        }
        catch (FormatException)
        {
            return Brushes.DodgerBlue;
        }
    }

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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is not { HasSession: true } vm) return;
        Focus();
        _down = e.GetPosition(this);
        if (e.ClickCount == 2 && MarkerAt(_down.X) is { } dbl)
        {
            vm.RenameMarker(dbl);
            e.Handled = true;
            return;
        }
        _draggingMarker = _down.Y > ActualHeight - 16 ? MarkerAt(_down.X) : null;
        _scrubbing = _draggingMarker is null;
        if (_scrubbing) vm.SetPlayhead(vm.XToTime(_down.X), keepVisible: false);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (ViewModel is not { } vm || e.LeftButton != MouseButtonState.Pressed) return;
        var x = e.GetPosition(this).X;
        if (_scrubbing) vm.SetPlayhead(vm.XToTime(x), keepVisible: false);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
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
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => ContextMarker = MarkerAt(e.GetPosition(this).X);

    protected override void OnContextMenuOpening(System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (ContextMarker is null) e.Handled = true; // menu só sobre marcadores
        base.OnContextMenuOpening(e);
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
        AllowDrop = true;
        SizeChanged += (_, _) => { if (ViewModel is { } vm) vm.ViewportWidth = ActualWidth; };
    }

    // ---------------------------------------------------------------- desenho

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Res("Med.PanelAlt", Brushes.Black), null, new Rect(0, 0, w, h));
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
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 30, 123, 255)), null, new Rect(0, top, w, th));
            dc.DrawLine(border, new Point(0, top + th - 0.5), new Point(w, top + th - 0.5));

            foreach (var clip in track.Clips)
            {
                var isMoving = _dragStarted && _mode == Mode.Move && _moving.Contains(clip.Id);
                var isTrimming = _dragStarted && _mode == Mode.Trim && _primary is { } p && (p.Id == clip.Id || (p.LinkId is { } l && clip.LinkId == l));
                var shown = isTrimming ? PreviewTrim(vm, clip) : clip;
                DrawClip(dc, vm, track, shown, top, th, text, vm.Selection.Contains(clip.Id) ? selectedPen : null,
                    clip.LinkId is not null ? linkedPen : null, ghost: isMoving ? 0.35 : 1);
            }

            // Transições: faixa centrada no corte (RF084).
            foreach (var tr in track.Transitions)
            {
                var to = track.Clips.Find(c => c.Id == tr.ToClipId);
                if (to is null) continue;
                var half = tr.Duration.TotalSeconds * vm.PixelsPerSecond / 2;
                var cx = vm.TimeToX(to.Start);
                if (cx + half < 0 || cx - half > w) continue;
                var band = new Rect(cx - half, top + th / 2 - 7, Math.Max(4, half * 2), 14);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), MakePen(Brushes.Black, 1), band, 3, 3);
                dc.DrawLine(MakePen(Brushes.Black, 1), new Point(band.Left + 2, band.Bottom - 2), new Point(band.Right - 2, band.Top + 2));
            }

            if (track.IsLocked)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(0, top, w, th - 1));
            top += th;
        }

        // Clipes sendo movidos, na posição de destino.
        if (_dragStarted && _mode == Mode.Move)
        {
            foreach (var (clip, targetId) in MovePreview(vm, seq))
            {
                var tt = vm.TrackTop(targetId);
                var target = seq.Tracks.Find(t => t.Id == targetId);
                if (tt < 0 || target is null) continue;
                DrawClip(dc, vm, target, clip, tt, TimelineViewModel.HeightOf(target.Kind), text, selectedPen, null, ghost: 0.85);
            }
        }

        // Marcadores atravessando as trilhas.
        foreach (var m in seq.Markers)
        {
            var x = Math.Round(vm.TimeToX(m.Time)) + 0.5;
            if (x < 0 || x > w) continue;
            dc.DrawLine(MakePen(TimelineRuler.MarkerBrush(m), 1, DashStyles.Dash), new Point(x, 0), new Point(x, h));
        }

        if (_snapGuide is { } guide)
        {
            var x = Math.Round(vm.TimeToX(guide)) + 0.5;
            dc.DrawLine(MakePen(Brushes.Gold, 1), new Point(x, 0), new Point(x, h));
        }

        if (_dropPreview is { } drop)
        {
            var x = Math.Round(vm.TimeToX(drop.At)) + 0.5;
            dc.DrawLine(MakePen(accent, 2), new Point(x, 0), new Point(x, h));
        }

        var px = Math.Round(vm.TimeToX(vm.Playhead)) + 0.5;
        dc.DrawLine(MakePen(Res("Med.Playhead", Brushes.DodgerBlue), 2), new Point(px, 0), new Point(px, h));
    }

    private void DrawClip(DrawingContext dc, TimelineViewModel vm, Track track, Clip clip, double top, double th, Brush text,
        Pen? selected, Pen? linked, double ghost)
    {
        var x1 = vm.TimeToX(clip.Start);
        var x2 = vm.TimeToX(clip.End);
        if (x2 < -2 || x1 > ActualWidth + 2) return;
        var rect = new Rect(x1 + 0.5, top + 3, Math.Max(2, x2 - x1 - 1), th - 7);

        var asset = clip.MediaAssetId is { } id ? vm.Project?.FindMedia(id) : null;
        var item = asset is not null ? vm.Library.Find(asset.Id) : null;
        var fill = track.Kind switch
        {
            TrackKind.Audio => Res("Med.ClipAudio", Brushes.MediumPurple),
            TrackKind.Text or TrackKind.Subtitle => Res("Med.ClipText", Brushes.MediumPurple),
            _ when asset?.Kind is MediaKind.Image => Res("Med.ClipImage", Brushes.SeaGreen),
            _ => Res("Med.ClipVideo", Brushes.RoyalBlue),
        };

        dc.PushOpacity(ghost * (track.IsHidden || (track.IsMuted && track.Kind == TrackKind.Audio) ? 0.45 : 1));
        var shape = new RectangleGeometry(rect, 4, 4);
        dc.DrawGeometry(fill, null, shape);
        dc.PushClip(shape);

        // Miniaturas repetidas ao longo do clipe de vídeo/imagem.
        if (track.Kind != TrackKind.Audio && item?.Thumbnail is { } thumb && rect.Height > 24)
        {
            var tileH = rect.Height - 18;
            var tileW = Math.Max(8, tileH * thumb.Width / Math.Max(1, thumb.Height));
            var startX = Math.Max(rect.Left, rect.Left + Math.Floor((-rect.Left) / tileW) * tileW);
            for (var x = startX; x < Math.Min(rect.Right, ActualWidth); x += tileW)
                dc.DrawImage(thumb, new Rect(x, rect.Top + 17, tileW, tileH));
        }

        // Forma de onda do trecho usado (considera ponto de entrada e velocidade).
        if (track.Kind == TrackKind.Audio && item?.Peaks is { Count: > 0 } peaks)
            DrawWaveform(dc, vm, clip, peaks, rect);

        // Faixa do título.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, new Rect(rect.Left, rect.Top, rect.Width, 16));
        var label = asset?.Name ?? (clip.IsText ? ViewModels.Inspector.ClipInspectorViewModel.TextTitle(clip.Text!) : "Clipe");
        if (clip.Speed != Rational.One)
            label += " · " + clip.Speed.Value.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + "×";
        if (clip.GroupId is not null) label = "▣ " + label;
        if (clip.FreezeAt is not null) label = "❄ " + label;
        if (!clip.Effects.IsEmpty) label = "fx " + label;
        if (asset is null && clip.MediaAssetId is not null) label = "⚠ Mídia removida";
        var ft = Text(label, 11, text, bold: true);
        ft.MaxTextWidth = Math.Max(1, rect.Width - 8);
        ft.MaxLineCount = 1;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(ft, new Point(Math.Max(rect.Left, 0) + 4, rect.Top + 1));

        // Entrada/saída gradual: rampas no canto superior.
        var fadePen = MakePen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);
        if (clip.FadeIn > MediaTime.Zero)
            dc.DrawLine(fadePen, new Point(rect.Left, rect.Bottom), new Point(rect.Left + clip.FadeIn.TotalSeconds * vm.PixelsPerSecond, rect.Top + 16));
        if (clip.FadeOut > MediaTime.Zero)
            dc.DrawLine(fadePen, new Point(rect.Right - clip.FadeOut.TotalSeconds * vm.PixelsPerSecond, rect.Top + 16), new Point(rect.Right, rect.Bottom));

        // Keyframes (losangos na base do clipe).
        if (!clip.Keyframes.IsEmpty)
        {
            var diamond = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A));
            foreach (var offset in clip.Keyframes.Values.SelectMany(k => k).Select(k => k.Offset).Distinct())
            {
                var kx = vm.TimeToX(clip.Start + offset);
                var ky = rect.Bottom - 6;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(kx, ky - 4), true, true);
                    ctx.LineTo(new Point(kx + 4, ky), true, false);
                    ctx.LineTo(new Point(kx, ky + 4), true, false);
                    ctx.LineTo(new Point(kx - 4, ky), true, false);
                }
                dc.DrawGeometry(diamond, MakePen(Brushes.Black, 1), g);
            }
        }
        dc.Pop();

        if (linked is not null) dc.DrawGeometry(null, linked, shape);
        if (selected is not null) dc.DrawGeometry(null, selected, shape);
        dc.Pop();
    }

    private void DrawWaveform(DrawingContext dc, TimelineViewModel vm, Clip clip, Core.Media.WaveformData peaks, Rect rect)
    {
        var from = Math.Max(rect.Left, 0);
        var to = Math.Min(rect.Right, ActualWidth);
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
            c.BeginFigure(new Point(from, mid), true, true);
            c.PolyLineTo(top, false, false);
            bottom.Reverse();
            c.PolyLineTo(bottom, false, false);
        }
        g.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), null, g);
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

    // ---------------------------------------------------------------- mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is not { HasSession: true } vm) return;
        Focus();
        _down = e.GetPosition(this);
        _dragStarted = false;
        _snapGuide = null;
        var additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

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

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var p = e.GetPosition(this);

        if (e.LeftButton != MouseButtonState.Pressed || _mode == Mode.None)
        {
            Cursor = HitClip(p) is { Track.IsLocked: false } hit && (hit.NearStart || hit.NearEnd) ? Cursors.SizeWE : null;
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

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var vm = ViewModel;
        var mode = _mode;
        var dragged = _dragStarted;
        var primary = _primary;
        _mode = Mode.None;
        _dragStarted = false;
        _snapGuide = null;
        ReleaseMouseCapture();
        if (vm is null) return;

        if (mode == Mode.Move && primary is not null)
        {
            if (dragged && vm.Sequence is { } seq)
            {
                var moves = MovePreview(vm, seq).Select(m => new ClipMove(m.Clip.Id, m.TrackId, m.Clip.Start)).ToList();
                vm.MoveClips(moves);
            }
            else if (!dragged && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
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

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mode == Mode.None) return;
        _mode = Mode.None;
        _dragStarted = false;
        _snapGuide = null;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (ViewModel is not { HasSession: true } vm) return;
        Focus();
        if (HitClip(e.GetPosition(this)) is { } hit) vm.SelectOnlyIfNotSelected(hit.Clip.Id);
        else vm.ClearSelection();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            vm.SetZoom(vm.PixelsPerSecond * (e.Delta > 0 ? 1.25 : 0.8), e.GetPosition(this).X);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            vm.HorizontalOffset = Math.Clamp(vm.HorizontalOffset - e.Delta, 0, vm.MaxHorizontalOffset);
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
    private IEnumerable<(Clip Clip, Guid TrackId)> MovePreview(TimelineViewModel vm, Sequence seq)
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

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { HasSession: true } vm || !e.Data.GetDataPresent(MediaDragFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        var p = e.GetPosition(this);
        var at = vm.CreateSnap().Snap(vm.XToTime(p.X), vm.SnapTolerance, vm.SnapEnabled).Time;
        _dropPreview = (vm.TrackAt(p.Y)?.Id, at);
        InvalidateVisual();
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        _dropPreview = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        var preview = _dropPreview;
        _dropPreview = null;
        InvalidateVisual();
        if (ViewModel is not { } vm || preview is null || e.Data.GetData(MediaDragFormat) is not string raw || !Guid.TryParse(raw, out var id))
            return;
        Focus();
        vm.DropMedia(id, preview.Value.TrackId, preview.Value.At);
    }
}
