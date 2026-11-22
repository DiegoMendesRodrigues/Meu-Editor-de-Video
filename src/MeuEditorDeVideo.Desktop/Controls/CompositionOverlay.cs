using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using Avalonia.Threading;
using MeuEditorDeVideo.Presentation.Media;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Camada de edição sobre o monitor (F9): desenha a máscara ou a região de rastreamento do clipe selecionado e permite
/// arrastar os pontos (alças) ou a forma inteira. No contorno livre, Ctrl+clique (Cmd no macOS) insere um ponto e o clique com o botão
/// direito numa alça o remove. As coordenadas seguem a imagem exibida (ajuste uniforme do quadro do projeto).
/// </summary>
public sealed class CompositionOverlay : Control, ICustomHitTest
{
    private const double HandleRadius = 5;

    public static readonly StyledProperty<CompositionInspectorViewModel?> EditorProperty =
        AvaloniaProperty.Register<CompositionOverlay, CompositionInspectorViewModel?>(nameof(Editor));

    private static readonly ImmutableSolidColorBrush Blue = new ImmutableSolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF));
    private static readonly ImmutablePen OutlinePen = new ImmutablePen(Blue, 1.5);
    private static readonly ImmutablePen ShadowPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 3);
    private static readonly ImmutablePen TrackedPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x3D)), 1.5, new ImmutableDashStyle([4, 2], 0));
    private static readonly IBrush HandleFill = Brushes.White;
    private static readonly ImmutableSolidColorBrush Interior = new ImmutableSolidColorBrush(Color.FromArgb(24, 0x3D, 0x9B, 0xFF));

    private int _dragHandle = -1;
    private bool _dragAll;
    private Point _dragStart;
    private List<Point> _dragOrigin = [];

    static CompositionOverlay()
    {
        AffectsRender<CompositionOverlay>(EditorProperty);
        EditorProperty.Changed.AddClassHandler<CompositionOverlay>((overlay, e) =>
        {
            if (e.OldValue is CompositionInspectorViewModel old) old.OverlayChanged -= overlay.OnOverlayChanged;
            if (e.NewValue is CompositionInspectorViewModel vm) vm.OverlayChanged += overlay.OnOverlayChanged;
        });
    }

    public CompositionOverlay()
    {
        Focusable = false;
        AutomationProperties.SetName(this, "Edição de máscara no monitor");
    }

    public CompositionInspectorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private void OnOverlayChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateVisual);

    /// <summary>Retângulo da imagem exibida (ajuste uniforme) e escala pixels de tela / pixels do projeto.</summary>
    private (Rect Rect, double Scale) Frame()
    {
        var vm = Editor;
        double pw = vm?.ProjectWidth ?? 1920, ph = vm?.ProjectHeight ?? 1080;
        var scale = Math.Min(Bounds.Width / pw, Bounds.Height / ph);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var w = pw * scale;
        var h = ph * scale;
        return (new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h), scale);
    }

    private static Point W(PointD p) => new(p.X, p.Y);

    private static PointD M(Point p) => new(p.X, p.Y);

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
        if (vm.Overlay == OverlayKind.MaskPath) return [.. p.Select(W)];
        if (p.Count < 2) return [];
        return [.. vm.OverlayCorners.Select(W)];
    }

    public override void Render(DrawingContext context)
    {
        var vm = Editor;
        if (vm is null || vm.Overlay == OverlayKind.None) return;

        if (vm.OverlayTracked.Count == 4)
            context.DrawGeometry(null, TrackedPen, ControlResources.Polygon(vm.OverlayTracked.Select(W).Select(ToScreen).ToList()));

        var outline = vm.OverlayOutline.Select(W).Select(ToScreen).ToList();
        if (outline.Count >= 3)
        {
            var g = ControlResources.Polygon(outline);
            context.DrawGeometry(null, ShadowPen, g);
            context.DrawGeometry(Interior, OutlinePen, g);
        }
        foreach (var h in Handles().Select(ToScreen))
        {
            context.DrawEllipse(HandleFill, ShadowPen, h, HandleRadius, HandleRadius);
            context.DrawEllipse(HandleFill, OutlinePen, h, HandleRadius - 1, HandleRadius - 1);
        }
    }

    /// <summary>Só as alças e o interior do contorno recebem o ponteiro; o resto do monitor (duplo clique) passa direto.</summary>
    public bool HitTest(Point point)
    {
        var vm = Editor;
        if (vm is null || vm.Overlay == OverlayKind.None) return false;
        if (HandleAt(point) >= 0) return true;
        var outline = vm.OverlayOutline.Select(W).Select(ToScreen).ToList();
        return outline.Count >= 3 && ControlResources.Polygon(outline).FillContains(point);
    }

    private int HandleAt(Point screen)
    {
        var handles = Handles();
        for (var i = 0; i < handles.Count; i++)
            if (ControlResources.Distance(ToScreen(handles[i]), screen) <= HandleRadius + 3) return i;
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = Editor;
        if (vm is null) return;
        var props = e.GetCurrentPoint(this).Properties;
        var pos = e.GetPosition(this);
        if (props.IsRightButtonPressed)
        {
            if (vm.Overlay != OverlayKind.MaskPath) return;
            var i = HandleAt(pos);
            if (i < 0 || vm.OverlayPoints.Count <= 3) return;
            var points = vm.OverlayPoints.ToList();
            points.RemoveAt(i);
            vm.SetOverlayPoints(points);
            e.Handled = true;
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        e.Handled = true; // não abre a tela cheia (duplo clique no monitor)

        if (vm.Overlay == OverlayKind.MaskPath && e.KeyModifiers.ToMed().HasFlag(MedModifiers.Control))
        {
            InsertPoint(vm, ToProject(pos));
            return;
        }
        _dragHandle = HandleAt(pos);
        _dragAll = _dragHandle < 0;
        _dragStart = ToProject(pos);
        _dragOrigin = Handles();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var vm = Editor;
        if (vm is null || e.Pointer.Captured != this) return;
        var now = ToProject(e.GetPosition(this));
        double dx = now.X - _dragStart.X, dy = now.Y - _dragStart.Y;
        var handles = _dragOrigin.ToList();
        if (_dragAll)
        {
            for (var i = 0; i < handles.Count; i++) handles[i] = new Point(handles[i].X + dx, handles[i].Y + dy);
        }
        else if (_dragHandle >= 0 && _dragHandle < handles.Count)
        {
            handles[_dragHandle] = new Point(_dragOrigin[_dragHandle].X + dx, _dragOrigin[_dragHandle].Y + dy);
        }
        vm.SetOverlayPoints([.. FromHandles(vm, handles).Select(M)]);
    }

    /// <summary>Alças → pontos do modelo: o canto arrastado e o oposto (diagonal) para retângulo/elipse/região; todos no contorno livre.</summary>
    private List<Point> FromHandles(CompositionInspectorViewModel vm, List<Point> handles)
    {
        if (vm.Overlay == OverlayKind.MaskPath || handles.Count < 4) return handles;
        var moved = _dragAll ? 0 : Math.Max(0, _dragHandle);
        return [handles[moved], handles[(moved + 2) % 4]];
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        _dragHandle = -1;
        _dragAll = false;
        e.Handled = true;
    }

    /// <summary>Insere um ponto no segmento do contorno livre mais próximo do clique.</summary>
    private static void InsertPoint(CompositionInspectorViewModel vm, Point p)
    {
        var pts = vm.OverlayPoints.Select(W).ToList();
        if (pts.Count < 2) return;
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            double abx = b.X - a.X, aby = b.Y - a.Y;
            var lengthSquared = abx * abx + aby * aby;
            var t = lengthSquared < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lengthSquared, 0, 1);
            var d = ControlResources.Distance(new Point(a.X + abx * t, a.Y + aby * t), p);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        pts.Insert(best + 1, p);
        vm.SetOverlayPoints([.. pts.Select(M)]);
    }
}
