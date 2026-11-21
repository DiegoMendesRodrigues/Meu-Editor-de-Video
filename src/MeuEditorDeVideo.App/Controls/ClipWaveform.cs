using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Forma de onda do clipe de áudio selecionado (aba Áudio): mostra o trecho usado do áudio, com o cursor da linha do
/// tempo em vermelho. Clicar ou arrastar move o cursor para aquele momento do clipe.
/// </summary>
public sealed class ClipWaveform : FrameworkElement
{
    public static readonly DependencyProperty InspectorProperty = DependencyProperty.Register(nameof(Inspector),
        typeof(AudioInspectorViewModel), typeof(ClipWaveform), new PropertyMetadata(null, OnInspectorChanged));

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x9F, 0xDD, 0xC8)));
    private static readonly Brush Wave = Frozen(new SolidColorBrush(Color.FromRgb(0x3E, 0xA8, 0x88)));
    private static readonly Pen CursorPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), 2));
    private static readonly Brush Muted = Frozen(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0x5C)));

    private readonly DispatcherTimer _waitPeaks;

    public ClipWaveform()
    {
        Height = 72;
        Cursor = Cursors.IBeam;
        Focusable = false;
        ToolTip = "Clique ou arraste para mudar o momento do áudio";
        System.Windows.Automation.AutomationProperties.SetName(this, "Forma de onda do áudio");
        _waitPeaks = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _waitPeaks.Tick += (_, _) => InvalidateVisual();
        Unloaded += (_, _) => _waitPeaks.Stop();
    }

    public AudioInspectorViewModel? Inspector
    {
        get => (AudioInspectorViewModel?)GetValue(InspectorProperty);
        set => SetValue(InspectorProperty, value);
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static void OnInspectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ClipWaveform)d;
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
        c.InvalidateVisual();
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(InvalidateVisual);

    private void OnTimelineProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Presentation.ViewModels.Timeline.TimelineViewModel.Playhead) or nameof(Presentation.ViewModels.Timeline.TimelineViewModel.SelectionSummary)) Dispatcher.BeginInvoke(InvalidateVisual);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (rect.Width < 4 || Inspector?.WaveformClip() is not { } target)
        {
            _waitPeaks.Stop();
            return;
        }
        dc.DrawRoundedRectangle(Background, null, rect, 4, 4);
        var (clip, peaks) = target;
        var mid = rect.Height / 2;
        var amp = rect.Height / 2 - 4;

        if (peaks is not { Count: > 0 })
        {
            // A forma de onda ainda está sendo lida: tenta de novo em instantes.
            if (!_waitPeaks.IsEnabled) _waitPeaks.Start();
            dc.DrawLine(new Pen(Wave, 1), new Point(4, mid), new Point(rect.Width - 4, mid));
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
                c.BeginFigure(new Point(0, mid), true, true);
                c.PolyLineTo(top, false, false);
                bottom.Reverse();
                c.PolyLineTo(bottom, false, false);
            }
            g.Freeze();
            dc.PushClip(new RectangleGeometry(rect, 4, 4));
            dc.DrawGeometry(clip.Enabled ? Wave : Muted, null, g);
            dc.Pop();
        }

        // Cursor da linha do tempo, quando está dentro do clipe.
        var playhead = Inspector.Timeline.Playhead;
        if (playhead >= clip.Start && playhead <= clip.End && clip.Duration.Ticks > 0)
        {
            var px = (playhead - clip.Start).TotalSeconds / clip.Duration.TotalSeconds * rect.Width;
            dc.DrawLine(CursorPen, new Point(px, 0), new Point(px, rect.Height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Seek(e.GetPosition(this)))
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Seek(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <summary>Leva o cursor da linha do tempo ao momento do clipe sob o ponteiro.</summary>
    private bool Seek(Point p)
    {
        if (Inspector?.WaveformClip() is not { } target || ActualWidth <= 0) return false;
        var clip = target.Clip;
        var f = Math.Clamp(p.X / ActualWidth, 0, 1);
        var timeline = Inspector.Timeline;
        var at = clip.Start + MediaTime.FromSecondsApprox(f * clip.Duration.TotalSeconds);
        var last = clip.End - timeline.FrameRate.FrameDuration;
        timeline.PlayheadSeconds = (at > last ? last : at).TotalSeconds;
        InvalidateVisual();
        return true;
    }
}
