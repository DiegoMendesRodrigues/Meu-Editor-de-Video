using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.ViewModels.Inspector;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.RenderingEngine;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Alças do clipe selecionado sobre o monitor: arrastar dentro da caixa move o clipe; a alça redonda acima gira
/// (Shift: passos de 15°); os cantos mudam o tamanho, mantendo a proporção (não em textos). Os valores vão para
/// posição, giro e escala do clipe no instante do cursor (com keyframes, gravam um keyframe), um passo de desfazer por gesto.
/// </summary>
public sealed class TransformOverlay : FrameworkElement
{
    private const double HandleSize = 9;
    private const double RotateDistance = 26;

    public static readonly DependencyProperty InspectorProperty = DependencyProperty.Register(nameof(Inspector),
        typeof(ClipInspectorViewModel), typeof(TransformOverlay), new PropertyMetadata(null, OnInspectorChanged));

    private static readonly Pen BoxPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF)), 1.5));
    private static readonly Pen ShadowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 3));
    private static readonly Brush HandleFill = Frozen(new SolidColorBrush(Colors.White));

    private enum Drag { None, Move, Rotate, Scale }

    private Drag _drag;
    private string _gesture = "";
    private Point _startProject;
    private (double X, double Y, double Scale, double Rotation) _start;
    private LayerBox _startBox;

    public TransformOverlay()
    {
        Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "Mover, girar e redimensionar o clipe no monitor");
    }

    public ClipInspectorViewModel? Inspector
    {
        get => (ClipInspectorViewModel?)GetValue(InspectorProperty);
        set => SetValue(InspectorProperty, value);
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static void OnInspectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (TransformOverlay)d;
        if (e.OldValue is ClipInspectorViewModel old) old.Timeline.Invalidated -= overlay.OnInvalidated;
        if (e.NewValue is ClipInspectorViewModel vm) vm.Timeline.Invalidated += overlay.OnInvalidated;
        overlay.InvalidateVisual();
    }

    private void OnInvalidated(object? sender, EventArgs e) => Dispatcher.BeginInvoke(InvalidateVisual);

    private (int Width, int Height) ProjectSize()
    {
        var settings = Inspector?.Timeline.Project?.Settings;
        return (settings?.Width ?? 1920, settings?.Height ?? 1080);
    }

    /// <summary>Retângulo da imagem exibida (ajuste uniforme) e escala pixels de tela / pixels do projeto.</summary>
    private (Rect Rect, double Scale) Frame()
    {
        var (pw, ph) = ProjectSize();
        var scale = Math.Min(ActualWidth / pw, ActualHeight / ph);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var w = pw * scale;
        var h = ph * scale;
        return (new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h), scale);
    }

    private Point ToScreen(double x, double y)
    {
        var (r, s) = Frame();
        return new Point(r.Left + r.Width / 2 + x * s, r.Top + r.Height / 2 + y * s);
    }

    private Point ToProject(Point p)
    {
        var (r, s) = Frame();
        return new Point((p.X - r.Left - r.Width / 2) / s, (p.Y - r.Top - r.Height / 2) / s);
    }

    /// <summary>Cantos (sup. esq., sup. dir., inf. dir., inf. esq.) e a alça de giro, em coordenadas de tela.</summary>
    private (Point[] Corners, Point Rotate, Point Center)? Geometry()
    {
        if (Inspector?.MonitorBox() is not { } box) return null;
        var center = ToScreen(box.CenterX, box.CenterY);
        var (_, s) = Frame();
        var a = box.RotationDegrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(a), Math.Sin(a));
        Point At(double lx, double ly) => new(center.X + lx * cos - ly * sin, center.Y + lx * sin + ly * cos);
        double hw = box.Width * s / 2, hh = box.Height * s / 2;
        return ([At(-hw, -hh), At(hw, -hh), At(hw, hh), At(-hw, hh)], At(0, -hh - RotateDistance), center);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Geometry() is not { } g) return;
        var outline = Polygon(g.Corners);
        dc.DrawGeometry(null, ShadowPen, outline);
        dc.DrawGeometry(null, BoxPen, outline);
        var topMid = new Point((g.Corners[0].X + g.Corners[1].X) / 2, (g.Corners[0].Y + g.Corners[1].Y) / 2);
        dc.DrawLine(BoxPen, topMid, g.Rotate);
        dc.DrawEllipse(HandleFill, BoxPen, g.Rotate, HandleSize / 2 + 1, HandleSize / 2 + 1);
        if (Inspector?.CanScaleOnMonitor == true)
            foreach (var c in g.Corners)
                dc.DrawRectangle(HandleFill, BoxPen, new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize));
    }

    private static StreamGeometry Polygon(IReadOnlyList<Point> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    private Drag HitKind(Point p)
    {
        if (Geometry() is not { } g) return Drag.None;
        const double tolerance = HandleSize / 2 + 4;
        if ((g.Rotate - p).Length <= tolerance) return Drag.Rotate;
        if (Inspector?.CanScaleOnMonitor == true && g.Corners.Any(c => (c - p).Length <= tolerance)) return Drag.Scale;
        return Polygon(g.Corners).FillContains(p) ? Drag.Move : Drag.None;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hit) =>
        HitKind(hit.HitPoint) != Drag.None ? new PointHitTestResult(this, hit.HitPoint) : null;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            Cursor = HitKind(e.GetPosition(this)) switch
            {
                Drag.Move => Cursors.SizeAll,
                Drag.Rotate => Cursors.Hand,
                Drag.Scale => Cursors.SizeNWSE,
                _ => null,
            };
            return;
        }
        var vm = Inspector;
        if (vm is null) return;
        var now = ToProject(e.GetPosition(this));
        var center = new Point(_startBox.CenterX, _startBox.CenterY);
        switch (_drag)
        {
            case Drag.Move:
                var d = now - _startProject;
                vm.SetPositionFromMonitor(_start.X + d.X, _start.Y + d.Y, _gesture);
                break;
            case Drag.Rotate:
                var a0 = Math.Atan2(_startProject.Y - center.Y, _startProject.X - center.X);
                var a1 = Math.Atan2(now.Y - center.Y, now.X - center.X);
                var rotation = _start.Rotation + (a1 - a0) * 180 / Math.PI;
                rotation = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Math.Round(rotation / 15) * 15 : Math.Round(rotation, 1);
                vm.SetFromMonitor(AnimatedProperty.Rotation, rotation, "Girar no monitor", _gesture);
                break;
            case Drag.Scale:
                var r0 = (_startProject - center).Length;
                var r1 = (now - center).Length;
                if (r0 < 1) break;
                vm.SetFromMonitor(AnimatedProperty.Scale, Math.Round(_start.Scale * r1 / r0, 3), "Redimensionar no monitor", _gesture);
                break;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var vm = Inspector;
        var pos = e.GetPosition(this);
        var kind = HitKind(pos);
        if (vm is null || kind == Drag.None || vm.MonitorValues() is not { } values || vm.MonitorBox() is not { } box) return;
        e.Handled = true; // não abre a tela cheia (duplo clique no monitor)
        _drag = kind;
        _gesture = Guid.NewGuid().ToString("N");
        _startProject = ToProject(pos);
        _start = values;
        _startBox = box;
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_drag != Drag.None) e.Handled = true;
        _drag = Drag.None;
    }
}
