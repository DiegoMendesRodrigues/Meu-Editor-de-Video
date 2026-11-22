using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MeuEditorDeVideo.ImageEngine.Effects;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Editor gráfico de curva de tons (RF098): arraste os pontos, clique numa área vazia para criar um ponto,
/// clique com o botão direito para removê-lo. As extremidades podem subir/descer mas não sair das bordas.
/// </summary>
public sealed class CurveEditor : Control
{
    public static readonly StyledProperty<CurveParameterViewModel?> CurveProperty =
        AvaloniaProperty.Register<CurveEditor, CurveParameterViewModel?>(nameof(Curve));

    private const double Pad = 6;
    private const double HitRadius = 8;
    private List<(double X, double Y)> _points = [(0, 0), (1, 1)];
    private int _dragging = -1;

    static CurveEditor()
    {
        AffectsRender<CurveEditor>(CurveProperty);
        CurveProperty.Changed.AddClassHandler<CurveEditor>((editor, e) =>
        {
            if (e.OldValue is CurveParameterViewModel old) old.PropertyChanged -= editor.OnCurvePropertyChanged;
            if (e.NewValue is CurveParameterViewModel vm)
            {
                vm.PropertyChanged += editor.OnCurvePropertyChanged;
                editor.LoadPoints();
            }
        });
    }

    public CurveEditor()
    {
        Focusable = true;
        // As cores Med.* vêm do tema: ao trocar claro/escuro o desenho é refeito.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        MinHeight = 140;
        AutomationProperties.SetName(this, "Editor de curva");
    }

    public CurveParameterViewModel? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    private void OnCurvePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurveParameterViewModel.Points) && _dragging < 0)
        {
            LoadPoints();
            InvalidateVisual();
        }
    }

    private void LoadPoints() => _points = Curve?.Points.OrderBy(p => p.X).ToList() ?? [(0, 0), (1, 1)];

    private Rect Area => new(Pad, Pad, Math.Max(1, Bounds.Width - 2 * Pad), Math.Max(1, Bounds.Height - 2 * Pad));

    private Point ToScreen((double X, double Y) p)
    {
        var a = Area;
        return new Point(a.Left + p.X * a.Width, a.Bottom - p.Y * a.Height);
    }

    private (double X, double Y) ToCurve(Point p)
    {
        var a = Area;
        return (Math.Clamp((p.X - a.Left) / a.Width, 0, 1), Math.Clamp((a.Bottom - p.Y) / a.Height, 0, 1));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 160);

    public override void Render(DrawingContext context)
    {
        var area = Area;
        var bg = this.Brush("Med.PanelAlt", Brushes.Black);
        var grid = this.Brush("Med.Border", Brushes.Gray);
        context.DrawRectangle(bg, null, new Rect(Bounds.Size), 4, 4);
        var gridPen = new Pen(grid, 1);
        for (var i = 1; i < 4; i++)
        {
            var x = area.Left + area.Width * i / 4;
            var y = area.Top + area.Height * i / 4;
            context.DrawLine(gridPen, new Point(x, area.Top), new Point(x, area.Bottom));
            context.DrawLine(gridPen, new Point(area.Left, y), new Point(area.Right, y));
        }
        context.DrawLine(new Pen(grid, 1, DashStyle.Dash), new Point(area.Left, area.Bottom), new Point(area.Right, area.Top));

        var color = Curve?.Stroke is { } s ? Color.FromArgb(s.A, s.R, s.G, s.B) : Colors.White;
        var brush = new ImmutableSolidColorBrush(color);
        var table = ToneCurve.Table(_points);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < 256; i++)
            {
                var p = ToScreen((i / 255.0, table[i]));
                if (i == 0) ctx.BeginFigure(p, false);
                else ctx.LineTo(p);
            }
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, 2), geometry);

        var ring = new Pen(bg, 1.5);
        foreach (var p in _points)
            context.DrawEllipse(brush, ring, ToScreen(p), 5, 5);
    }

    private int HitTest(Point screen)
    {
        for (var i = 0; i < _points.Count; i++)
            if (ControlResources.Distance(ToScreen(_points[i]), screen) <= HitRadius) return i;
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        var pos = e.GetPosition(this);
        if (props.IsRightButtonPressed)
        {
            var remove = HitTest(pos);
            if (remove > 0 && remove < _points.Count - 1)
            {
                _points.RemoveAt(remove);
                Push();
            }
            e.Handled = true;
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        Focus();
        var hit = HitTest(pos);
        if (hit < 0)
        {
            var c = ToCurve(pos);
            if (_points.Exists(p => Math.Abs(p.X - c.X) < 0.02)) return;
            _points.Add(c);
            _points.Sort((a, b) => a.X.CompareTo(b.X));
            hit = _points.IndexOf(c);
        }
        _dragging = hit;
        e.Pointer.Capture(this);
        Push();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging < 0) return;
        var c = ToCurve(e.GetPosition(this));
        // Pontos não ultrapassam os vizinhos; as extremidades ficam presas às bordas horizontais.
        var minX = _dragging == 0 ? 0 : _points[_dragging - 1].X + 0.01;
        var maxX = _dragging == _points.Count - 1 ? 1 : _points[_dragging + 1].X - 0.01;
        if (_dragging == 0 && _points.Count > 1 && _points[0].X <= 0) maxX = 0;
        if (_dragging == _points.Count - 1 && _points[^1].X >= 1) minX = 1;
        _points[_dragging] = (Math.Clamp(c.X, minX, Math.Max(minX, maxX)), c.Y);
        Push();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging < 0) return;
        _dragging = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = -1;
    }

    private void Push()
    {
        InvalidateVisual();
        Curve?.SetPoints([.. _points]);
    }
}
