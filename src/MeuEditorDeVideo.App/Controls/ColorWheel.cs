using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Roda de cor para gradação (RF100): o ponto indica a cor e a intensidade do deslocamento cromático
/// (X, Y em −1…1). Duplo clique volta ao centro. Setas movem o ponto em passos finos.
/// </summary>
public sealed class ColorWheel : FrameworkElement
{
    public static readonly DependencyProperty XProperty = DependencyProperty.Register(nameof(X), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty YProperty = DependencyProperty.Register(nameof(Y), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly BitmapSource Wheel = CreateWheel(160);
    private bool _dragging;

    public ColorWheel()
    {
        Focusable = true;
        AutomationProperties.SetName(this, "Roda de cor");
    }

    public double X
    {
        get => (double)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    private double Radius => Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 4);
    private Point Center => new(ActualWidth / 2, ActualHeight / 2);

    protected override Size MeasureOverride(Size availableSize) => new(110, 110);

    protected override void OnRender(DrawingContext dc)
    {
        var r = Radius;
        var c = Center;
        var rect = new Rect(c.X - r, c.Y - r, 2 * r, 2 * r);
        dc.PushClip(new EllipseGeometry(c, r, r));
        dc.DrawImage(Wheel, rect);
        dc.Pop();
        var border = TryFindResource("Med.Border") as Brush ?? Brushes.Gray;
        dc.DrawEllipse(null, new Pen(border, 1), c, r, r);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1), new Point(c.X - 4, c.Y), new Point(c.X + 4, c.Y));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1), new Point(c.X, c.Y - 4), new Point(c.X, c.Y + 4));

        // Y positivo para cima (convenção matemática), como o ângulo da matiz.
        var puck = new Point(c.X + X * r, c.Y - Y * r);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), puck, 6, 6);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.5), puck, 6, 6);
    }

    private void SetFrom(Point p)
    {
        var r = Radius;
        var x = (p.X - Center.X) / r;
        var y = -(p.Y - Center.Y) / r;
        var len = Math.Sqrt(x * x + y * y);
        if (len > 1) { x /= len; y /= len; }
        X = Math.Round(x, 3);
        Y = Math.Round(y, 3);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)
        {
            X = 0;
            Y = 0;
            e.Handled = true;
            return;
        }
        _dragging = true;
        CaptureMouse();
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFrom(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        const double step = 0.02;
        switch (e.Key)
        {
            case Key.Left: X = Math.Max(-1, X - step); break;
            case Key.Right: X = Math.Min(1, X + step); break;
            case Key.Up: Y = Math.Min(1, Y + step); break;
            case Key.Down: Y = Math.Max(-1, Y - step); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Matiz pelo ângulo (vermelho em 0°, como o processamento), saturação pelo raio.</summary>
    private static BitmapSource CreateWheel(int size)
    {
        var px = new byte[size * size * 4];
        var half = size / 2.0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5 - half) / half;
                var dy = -(y + 0.5 - half) / half;
                var sat = Math.Min(1, Math.Sqrt(dx * dx + dy * dy));
                var angle = Math.Atan2(dy, dx);
                // Mesmo mapeamento do processamento: R = cos(a), G = cos(a − 120°), B = cos(a + 120°).
                double r = 0.5 + 0.5 * sat * Math.Cos(angle);
                double g = 0.5 + 0.5 * sat * Math.Cos(angle - 2 * Math.PI / 3);
                double b = 0.5 + 0.5 * sat * Math.Cos(angle + 2 * Math.PI / 3);
                var i = (y * size + x) * 4;
                px[i] = (byte)(b * 255);
                px[i + 1] = (byte)(g * 255);
                px[i + 2] = (byte)(r * 255);
                px[i + 3] = 255;
            }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
        bmp.Freeze();
        return bmp;
    }
}
