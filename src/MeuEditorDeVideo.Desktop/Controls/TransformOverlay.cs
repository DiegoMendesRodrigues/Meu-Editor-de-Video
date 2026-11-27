// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Avalonia.Threading;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;
using MeuEditorDeVideo.RenderingEngine;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Alças do clipe selecionado sobre o monitor: arrastar dentro da caixa move o clipe; a alça redonda acima gira
/// (Shift: passos de 15°); os cantos mudam o tamanho, mantendo a proporção (não em textos). Os valores vão para
/// posição, giro e escala do clipe no instante do cursor (com keyframes, gravam um keyframe), um passo de desfazer por gesto.
/// </summary>
public sealed class TransformOverlay : Control, ICustomHitTest
{
    private const double HandleSize = 9;
    private const double RotateDistance = 26;

    public static readonly StyledProperty<ClipInspectorViewModel?> InspectorProperty =
        AvaloniaProperty.Register<TransformOverlay, ClipInspectorViewModel?>(nameof(Inspector));

    private static readonly ImmutableSolidColorBrush Blue = new ImmutableSolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF));
    private static readonly ImmutablePen BoxPen = new ImmutablePen(Blue, 1.5);
    private static readonly ImmutablePen ShadowPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 3);
    private static readonly IBrush HandleFill = Brushes.White;

    private enum Drag { None, Move, Rotate, Scale }

    private Drag _drag;
    private string _gesture = "";
    private Point _startProject;
    private (double X, double Y, double Scale, double Rotation) _start;
    private LayerBox _startBox;

    static TransformOverlay()
    {
        AffectsRender<TransformOverlay>(InspectorProperty);
        InspectorProperty.Changed.AddClassHandler<TransformOverlay>((overlay, e) =>
        {
            if (e.OldValue is ClipInspectorViewModel old) old.Timeline.Invalidated -= overlay.OnInvalidated;
            if (e.NewValue is ClipInspectorViewModel vm) vm.Timeline.Invalidated += overlay.OnInvalidated;
        });
    }

    public TransformOverlay()
    {
        Focusable = false;
        AutomationProperties.SetName(this, "Mover, girar e redimensionar o clipe no monitor");
    }

    public ClipInspectorViewModel? Inspector
    {
        get => GetValue(InspectorProperty);
        set => SetValue(InspectorProperty, value);
    }

    private void OnInvalidated(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateVisual);

    private (int Width, int Height) ProjectSize()
    {
        var settings = Inspector?.Timeline.Project?.Settings;
        return (settings?.Width ?? 1920, settings?.Height ?? 1080);
    }

    /// <summary>Retângulo da imagem exibida (ajuste uniforme) e escala pixels de tela / pixels do projeto.</summary>
    private (Rect Rect, double Scale) Frame()
    {
        var (pw, ph) = ProjectSize();
        var scale = Math.Min(Bounds.Width / pw, Bounds.Height / ph);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var w = pw * scale;
        var h = ph * scale;
        return (new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h), scale);
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

    public override void Render(DrawingContext context)
    {
        if (Geometry() is not { } g) return;
        var outline = ControlResources.Polygon(g.Corners);
        context.DrawGeometry(null, ShadowPen, outline);
        context.DrawGeometry(null, BoxPen, outline);
        var topMid = new Point((g.Corners[0].X + g.Corners[1].X) / 2, (g.Corners[0].Y + g.Corners[1].Y) / 2);
        context.DrawLine(BoxPen, topMid, g.Rotate);
        context.DrawEllipse(HandleFill, BoxPen, g.Rotate, HandleSize / 2 + 1, HandleSize / 2 + 1);
        if (Inspector?.CanScaleOnMonitor == true)
            foreach (var c in g.Corners)
                context.DrawRectangle(HandleFill, BoxPen, new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize));
    }

    private Drag HitKind(Point p)
    {
        if (Geometry() is not { } g) return Drag.None;
        const double tolerance = HandleSize / 2 + 4;
        if (ControlResources.Distance(g.Rotate, p) <= tolerance) return Drag.Rotate;
        if (Inspector?.CanScaleOnMonitor == true && g.Corners.Any(c => ControlResources.Distance(c, p) <= tolerance)) return Drag.Scale;
        return ControlResources.Polygon(g.Corners).FillContains(p) ? Drag.Move : Drag.None;
    }

    /// <summary>Só a caixa, as alças e o giro recebem o ponteiro; o resto do monitor (duplo clique) passa direto.</summary>
    public bool HitTest(Point point) => HitKind(point) != Drag.None;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == Drag.None || e.Pointer.Captured != this)
        {
            Cursor = ControlResources.Cursor(HitKind(e.GetPosition(this)) switch
            {
                Drag.Move => StandardCursorType.SizeAll,
                Drag.Rotate => StandardCursorType.Hand,
                Drag.Scale => StandardCursorType.TopLeftCorner,
                _ => (StandardCursorType?)null,
            });
            return;
        }
        var vm = Inspector;
        if (vm is null) return;
        var now = ToProject(e.GetPosition(this));
        var center = new Point(_startBox.CenterX, _startBox.CenterY);
        switch (_drag)
        {
            case Drag.Move:
                vm.SetPositionFromMonitor(_start.X + now.X - _startProject.X, _start.Y + now.Y - _startProject.Y, _gesture);
                break;
            case Drag.Rotate:
                var a0 = Math.Atan2(_startProject.Y - center.Y, _startProject.X - center.X);
                var a1 = Math.Atan2(now.Y - center.Y, now.X - center.X);
                var rotation = _start.Rotation + (a1 - a0) * 180 / Math.PI;
                rotation = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Math.Round(rotation / 15) * 15 : Math.Round(rotation, 1);
                vm.SetFromMonitor(AnimatedProperty.Rotation, rotation, "Girar no monitor", _gesture);
                break;
            case Drag.Scale:
                var r0 = ControlResources.Distance(_startProject, center);
                var r1 = ControlResources.Distance(now, center);
                if (r0 < 1) break;
                vm.SetFromMonitor(AnimatedProperty.Scale, Math.Round(_start.Scale * r1 / r0, 3), "Redimensionar no monitor", _gesture);
                break;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
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
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        if (_drag != Drag.None) e.Handled = true;
        _drag = Drag.None;
    }
}
