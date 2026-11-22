using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Forma de onda do clipe de áudio selecionado (aba Áudio): mostra o trecho usado do áudio, com o cursor da linha do
/// tempo em vermelho. Clicar ou arrastar move o cursor para aquele momento do clipe.
/// </summary>
public sealed class ClipWaveform : Control
{
    public static readonly StyledProperty<AudioInspectorViewModel?> InspectorProperty =
        AvaloniaProperty.Register<ClipWaveform, AudioInspectorViewModel?>(nameof(Inspector));

    private static readonly ImmutableSolidColorBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0x9F, 0xDD, 0xC8));
    private static readonly ImmutableSolidColorBrush Wave = new ImmutableSolidColorBrush(Color.FromRgb(0x3E, 0xA8, 0x88));
    private static readonly ImmutablePen CursorPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), 2);
    private static readonly ImmutableSolidColorBrush Muted = new ImmutableSolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0x5C));
    private static readonly ImmutablePen FlatPen = new ImmutablePen(Wave, 1);

    private readonly DispatcherTimer _waitPeaks;
    private bool _seeking;

    static ClipWaveform()
    {
        AffectsRender<ClipWaveform>(InspectorProperty);
        InspectorProperty.Changed.AddClassHandler<ClipWaveform>((c, e) =>
        {
            if (e.OldValue is AudioInspectorViewModel old)
            {
                old.Timeline.Invalidated -= c.OnChanged;
                old.Timeline.PropertyChanged -= c.OnTimelineProperty;
            }
            if (e.NewValue is AudioInspectorViewModel vm)
            {
                vm.Timeline.Invalidated += c.OnChanged;
                vm.Timeline.PropertyChanged += c.OnTimelineProperty;
            }
        });
    }

    public ClipWaveform()
    {
        Height = 72;
        Cursor = ControlResources.Cursor(StandardCursorType.Ibeam);
        Focusable = false;
        ToolTip.SetTip(this, "Clique ou arraste para mudar o momento do áudio");
        AutomationProperties.SetName(this, "Forma de onda do áudio");
        _waitPeaks = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _waitPeaks.Tick += (_, _) => InvalidateVisual();
    }

    public AudioInspectorViewModel? Inspector
    {
        get => GetValue(InspectorProperty);
        set => SetValue(InspectorProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _waitPeaks.Stop();
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateVisual);

    private void OnTimelineProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineViewModel.Playhead) or nameof(TimelineViewModel.SelectionSummary))
            Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        // Área sensível ao ponteiro mesmo quando não há forma de onda para mostrar.
        context.DrawRectangle(Brushes.Transparent, null, rect);
        if (rect.Width < 4 || Inspector?.WaveformClip() is not { } target)
        {
            _waitPeaks.Stop();
            return;
        }
        context.DrawRectangle(Background, null, rect, 4, 4);
        var (clip, peaks) = target;
        var mid = rect.Height / 2;
        var amp = rect.Height / 2 - 4;

        if (peaks is not { Count: > 0 })
        {
            // A forma de onda ainda está sendo lida: tenta de novo em instantes.
            if (!_waitPeaks.IsEnabled) _waitPeaks.Start();
            context.DrawLine(FlatPen, new Point(4, mid), new Point(rect.Width - 4, mid));
        }
        else
        {
            _waitPeaks.Stop();
            var top = new List<Point>();
            var bottom = new List<Point>();
            var duration = clip.Duration.TotalSeconds;
            var step = duration / rect.Width;
            for (var x = 0.0; x <= rect.Width; x += 1)
            {
                // Trecho da origem coberto por esta coluna (considera velocidade e reprodução ao contrário).
                var a = clip.SourceTimeAt(clip.Start + MediaTime.FromSecondsApprox(x * step)).TotalSeconds;
                var b = clip.SourceTimeAt(clip.Start + MediaTime.FromSecondsApprox(Math.Min(duration, (x + 1) * step))).TotalSeconds;
                if (b < a) (a, b) = (b, a);
                int i0 = (int)(a * peaks.PeaksPerSecond), i1 = Math.Max(i0 + 1, (int)(b * peaks.PeaksPerSecond));
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
                c.BeginFigure(new Point(0, mid), true);
                foreach (var p in top) c.LineTo(p, false);
                for (var i = bottom.Count - 1; i >= 0; i--) c.LineTo(bottom[i], false);
                c.EndFigure(true);
            }
            using (context.PushClip(new RoundedRect(rect, 4)))
                context.DrawGeometry(clip.Enabled ? Wave : Muted, null, g);
        }

        // Cursor da linha do tempo, quando está dentro do clipe.
        var playhead = Inspector.Timeline.Playhead;
        if (playhead >= clip.Start && playhead <= clip.End && clip.Duration.Ticks > 0)
        {
            var px = (playhead - clip.Start).TotalSeconds / clip.Duration.TotalSeconds * rect.Width;
            context.DrawLine(CursorPen, new Point(px, 0), new Point(px, rect.Height));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (Seek(e.GetPosition(this)))
        {
            _seeking = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_seeking) Seek(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_seeking) return;
        _seeking = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _seeking = false;
    }

    /// <summary>Leva o cursor da linha do tempo ao momento do clipe sob o ponteiro.</summary>
    private bool Seek(Point p)
    {
        if (Inspector?.WaveformClip() is not { } target || Bounds.Width <= 0) return false;
        var clip = target.Clip;
        var f = Math.Clamp(p.X / Bounds.Width, 0, 1);
        var timeline = Inspector.Timeline;
        var at = clip.Start + MediaTime.FromSecondsApprox(f * clip.Duration.TotalSeconds);
        var last = clip.End - timeline.FrameRate.FrameDuration;
        timeline.PlayheadSeconds = (at > last ? last : at).TotalSeconds;
        InvalidateVisual();
        return true;
    }
}
