using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.ViewModels.Inspector;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Camada de edição sobre o monitor (F9): desenha a máscara ou a região de rastreamento do clipe selecionado e permite
/// arrastar os pontos (alças) ou a forma inteira. No contorno livre, Ctrl+clique insere um ponto e o clique com o botão
/// direito numa alça o remove. As coordenadas seguem a imagem exibida (ajuste uniforme do quadro do projeto).
/// </summary>
public sealed class CompositionOverlay : FrameworkElement
{
    private const double HandleRadius = 5;

    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(nameof(Editor),
        typeof(CompositionInspectorViewModel), typeof(CompositionOverlay), new PropertyMetadata(null, OnEditorChanged));

    private static readonly Pen OutlinePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF)), 1.5));
    private static readonly Pen ShadowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 3));
    private static readonly Pen TrackedPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x3D)), 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Brush HandleFill = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush Interior = Frozen(new SolidColorBrush(Color.FromArgb(24, 0x3D, 0x9B, 0xFF)));

    private int _dragHandle = -1;
    private bool _dragAll;
    private Point _dragStart;
    private List<Point> _dragOrigin = [];

    public CompositionOverlay()
    {
        Focusable = false;
        AutomationProperties_SetName();
    }

    private void AutomationProperties_SetName() =>
        System.Windows.Automation.AutomationProperties.SetName(this, "Edição de máscara no monitor");

    public CompositionInspectorViewModel? Editor
    {
        get => (CompositionInspectorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private static void OnEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (CompositionOverlay)d;
        if (e.OldValue is CompositionInspectorViewModel old) old.OverlayChanged -= overlay.OnOverlayChanged;
        if (e.NewValue is CompositionInspectorViewModel vm) vm.OverlayChanged += overlay.OnOverlayChanged;
        overlay.InvalidateVisual();
    }

    private void OnOverlayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(InvalidateVisual);

    /// <summary>Retângulo da imagem exibida (ajuste uniforme) e escala pixels de tela / pixels do projeto.</summary>
    private (Rect Rect, double Scale) Frame()
    {
        var vm = Editor;
        double pw = vm?.ProjectWidth ?? 1920, ph = vm?.ProjectHeight ?? 1080;
        var scale = Math.Min(ActualWidth / pw, ActualHeight / ph);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var w = pw * scale;
        var h = ph * scale;
        return (new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h), scale);
    }

    private Point ToScreen(Point p)
    {
        var (r, s) = Frame();
        return new Point(r.Left + r.Width / 2 + p.X * s, r.Top + r.Height / 2 + p.Y * s);
    }

    private Point ToProject(Point p)
    {
        var (r, s) = Frame();
        return new Point((p.X - r.Left - r.Width / 2) / s, (p.Y - r.Top - r.Height / 2) / s);
    }

    /// <summary>Alças editáveis: retângulo/elipse/região têm 4 cantos (derivados dos 2 pontos); o contorno livre, um por ponto.</summary>
    private List<Point> Handles()
    {
        var vm = Editor;
        if (vm is null || vm.Overlay == OverlayKind.None) return [];
        var p = vm.OverlayPoints;
        if (vm.Overlay == OverlayKind.MaskPath) return [.. p];
        if (p.Count < 2) return [];
        return [.. vm.OverlayCorners];
    }

    protected override void OnRender(DrawingContext dc)
    {
        var vm = Editor;
        if (vm is null || vm.Overlay == OverlayKind.None) return;

        if (vm.OverlayTracked.Count == 4)
            dc.DrawGeometry(null, TrackedPen, Polygon(vm.OverlayTracked.Select(ToScreen).ToList()));

        var outline = vm.OverlayOutline.Select(ToScreen).ToList();
        if (outline.Count >= 3)
        {
            var g = Polygon(outline);
            dc.DrawGeometry(null, ShadowPen, g);
            dc.DrawGeometry(Interior, OutlinePen, g);
        }
        foreach (var h in Handles().Select(ToScreen))
        {
            dc.DrawEllipse(HandleFill, ShadowPen, h, HandleRadius, HandleRadius);
            dc.DrawEllipse(HandleFill, OutlinePen, h, HandleRadius - 1, HandleRadius - 1);
        }
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

    protected override HitTestResult? HitTestCore(PointHitTestParameters hit)
    {
        var vm = Editor;
        if (vm is null || vm.Overlay == OverlayKind.None) return null;
        if (HandleAt(hit.HitPoint) >= 0) return new PointHitTestResult(this, hit.HitPoint);
        var outline = vm.OverlayOutline.Select(ToScreen).ToList();
        return outline.Count >= 3 && Polygon(outline).FillContains(hit.HitPoint) ? new PointHitTestResult(this, hit.HitPoint) : null;
    }

    private int HandleAt(Point screen)
    {
        var handles = Handles();
        for (var i = 0; i < handles.Count; i++)
            if ((ToScreen(handles[i]) - screen).Length <= HandleRadius + 3) return i;
        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var vm = Editor;
        if (vm is null) return;
        var pos = e.GetPosition(this);
        e.Handled = true; // não abre a tela cheia (duplo clique no monitor)

        if (vm.Overlay == OverlayKind.MaskPath && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            InsertPoint(vm, ToProject(pos));
            return;
        }
        _dragHandle = HandleAt(pos);
        _dragAll = _dragHandle < 0;
        _dragStart = ToProject(pos);
        _dragOrigin = Handles();
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var vm = Editor;
        if (vm is null || !IsMouseCaptured) return;
        var now = ToProject(e.GetPosition(this));
        var d = now - _dragStart;
        var handles = _dragOrigin.ToList();
        if (_dragAll)
        {
            for (var i = 0; i < handles.Count; i++) handles[i] += d;
        }
        else if (_dragHandle >= 0 && _dragHandle < handles.Count)
        {
            handles[_dragHandle] = _dragOrigin[_dragHandle] + d;
        }
        vm.SetOverlayPoints(FromHandles(vm, handles));
    }

    /// <summary>Alças → pontos do modelo: o canto arrastado e o oposto (diagonal) para retângulo/elipse/região; todos no contorno livre.</summary>
    private List<Point> FromHandles(CompositionInspectorViewModel vm, List<Point> handles)
    {
        if (vm.Overlay == OverlayKind.MaskPath || handles.Count < 4) return handles;
        var moved = _dragAll ? 0 : Math.Max(0, _dragHandle);
        return [handles[moved], handles[(moved + 2) % 4]];
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        _dragHandle = -1;
        _dragAll = false;
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var vm = Editor;
        if (vm is null || vm.Overlay != OverlayKind.MaskPath) return;
        var i = HandleAt(e.GetPosition(this));
        if (i < 0 || vm.OverlayPoints.Count <= 3) return;
        var points = vm.OverlayPoints.ToList();
        points.RemoveAt(i);
        vm.SetOverlayPoints(points);
        e.Handled = true;
    }

    /// <summary>Insere um ponto no segmento do contorno livre mais próximo do clique.</summary>
    private static void InsertPoint(CompositionInspectorViewModel vm, Point p)
    {
        var pts = vm.OverlayPoints.ToList();
        if (pts.Count < 2) return;
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var ab = b - a;
            var t = ab.LengthSquared < 1e-9 ? 0 : Math.Clamp(Vector.Multiply(p - a, ab) / ab.LengthSquared, 0, 1);
            var d = (a + ab * t - p).Length;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        pts.Insert(best + 1, p);
        vm.SetOverlayPoints(pts);
    }
}
