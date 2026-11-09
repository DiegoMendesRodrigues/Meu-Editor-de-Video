using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.ViewModels.Inspector;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Editor gráfico de curva de tons (RF098): arraste os pontos, clique numa área vazia para criar um ponto,
/// clique com o botão direito para removê-lo. As extremidades podem subir/descer mas não sair das bordas.
/// </summary>
public sealed class CurveEditor : FrameworkElement
{
    public static readonly DependencyProperty CurveProperty = DependencyProperty.Register(
        nameof(Curve), typeof(CurveParameterViewModel), typeof(CurveEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCurveChanged));

    private const double Pad = 6;
    private const double HitRadius = 8;
    private List<(double X, double Y)> _points = [(0, 0), (1, 1)];
    private int _dragging = -1;

    public CurveEditor()
    {
        Focusable = true;
        MinHeight = 140;
        AutomationProperties.SetName(this, "Editor de curva");
    }

    public CurveParameterViewModel? Curve
    {
        get => (CurveParameterViewModel?)GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    private static void OnCurveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (CurveEditor)d;
        if (e.OldValue is CurveParameterViewModel old) old.PropertyChanged -= editor.OnCurvePropertyChanged;
        if (e.NewValue is CurveParameterViewModel vm)
        {
            vm.PropertyChanged += editor.OnCurvePropertyChanged;
            editor.LoadPoints();
        }
    }

    private void OnCurvePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurveParameterViewModel.Points) && _dragging < 0)
        {
            LoadPoints();
            InvalidateVisual();
        }
    }

    private void LoadPoints() => _points = Curve?.Points.OrderBy(p => p.X).ToList() ?? [(0, 0), (1, 1)];

    private Rect Area => new(Pad, Pad, Math.Max(1, ActualWidth - 2 * Pad), Math.Max(1, ActualHeight - 2 * Pad));

    private Point ToScreen((double X, double Y) p) => new(Area.Left + p.X * Area.Width, Area.Bottom - p.Y * Area.Height);

    private (double X, double Y) ToCurve(Point p) =>
        (Math.Clamp((p.X - Area.Left) / Area.Width, 0, 1), Math.Clamp((Area.Bottom - p.Y) / Area.Height, 0, 1));

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 160);

    protected override void OnRender(DrawingContext dc)
    {
        var area = Area;
        var bg = TryFindResource("Med.PanelAlt") as Brush ?? Brushes.Black;
        var grid = TryFindResource("Med.Border") as Brush ?? Brushes.Gray;
        dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight), 4, 4);
        var gridPen = new Pen(grid, 1);
        for (var i = 1; i < 4; i++)
        {
            var x = area.Left + area.Width * i / 4;
            var y = area.Top + area.Height * i / 4;
            dc.DrawLine(gridPen, new Point(x, area.Top), new Point(x, area.Bottom));
            dc.DrawLine(gridPen, new Point(area.Left, y), new Point(area.Right, y));
        }
        dc.DrawLine(new Pen(grid, 1) { DashStyle = DashStyles.Dash }, new Point(area.Left, area.Bottom), new Point(area.Right, area.Top));

        var color = Curve?.Stroke ?? Colors.White;
        var stroke = new Pen(new SolidColorBrush(color), 2);
        var table = ToneCurve.Table(_points);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < 256; i++)
            {
                var p = ToScreen((i / 255.0, table[i]));
                if (i == 0) ctx.BeginFigure(p, false, false);
                else ctx.LineTo(p, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, stroke, geometry);

        var fill = new SolidColorBrush(color);
        foreach (var p in _points)
            dc.DrawEllipse(fill, new Pen(bg, 1.5), ToScreen(p), 5, 5);
    }

    private int HitTest(Point screen)
    {
        for (var i = 0; i < _points.Count; i++)
            if ((ToScreen(_points[i]) - screen).Length <= HitRadius) return i;
        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
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
        CaptureMouse();
        Push();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
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

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging < 0) return;
        _dragging = -1;
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var hit = HitTest(e.GetPosition(this));
        if (hit > 0 && hit < _points.Count - 1)
        {
            _points.RemoveAt(hit);
            Push();
        }
        e.Handled = true;
    }

    private void Push()
    {
        InvalidateVisual();
        Curve?.SetPoints([.. _points]);
    }
}
