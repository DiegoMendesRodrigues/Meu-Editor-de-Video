using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Roda de cor para gradação (RF100): o ponto indica a cor e a intensidade do deslocamento cromático
/// (X, Y em −1…1). Duplo clique volta ao centro. Setas movem o ponto em passos finos.
/// </summary>
public sealed class ColorWheel : Control
{
    public static readonly StyledProperty<double> XProperty =
        AvaloniaProperty.Register<ColorWheel, double>(nameof(X), 0.0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> YProperty =
        AvaloniaProperty.Register<ColorWheel, double>(nameof(Y), 0.0, defaultBindingMode: BindingMode.TwoWay);

    private static readonly Lazy<Bitmap> Wheel = new(() => CreateWheel(160));
    private static readonly ImmutablePen CrossPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);
    private static readonly ImmutablePen PuckShadow = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Black), 3);
    private static readonly ImmutablePen PuckRing = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 1.5);
    private bool _dragging;

    static ColorWheel()
    {
        AffectsRender<ColorWheel>(XProperty, YProperty);
    }

    public ColorWheel()
    {
        Focusable = true;
        // As cores Med.* vêm do tema: ao trocar claro/escuro o desenho é refeito.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        AutomationProperties.SetName(this, "Roda de cor");
    }

    public double X
    {
        get => GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    private double Radius => Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 4);
    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    protected override Size MeasureOverride(Size availableSize) => new(110, 110);

    public override void Render(DrawingContext context)
    {
        var r = Radius;
        var c = Center;
        var rect = new Rect(c.X - r, c.Y - r, 2 * r, 2 * r);
        // Fundo transparente para o clique acertar também fora do desenho da roda.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        using (context.PushGeometryClip(new EllipseGeometry(rect)))
            context.DrawImage(Wheel.Value, new Rect(Wheel.Value.Size), rect);
        context.DrawEllipse(null, new Pen(this.Brush("Med.Border", Brushes.Gray), 1), c, r, r);
        context.DrawLine(CrossPen, new Point(c.X - 4, c.Y), new Point(c.X + 4, c.Y));
        context.DrawLine(CrossPen, new Point(c.X, c.Y - 4), new Point(c.X, c.Y + 4));

        // Y positivo para cima (convenção matemática), como o ângulo da matiz.
        var puck = new Point(c.X + X * r, c.Y - Y * r);
        context.DrawEllipse(null, PuckShadow, puck, 6, 6);
        context.DrawEllipse(null, PuckRing, puck, 6, 6);
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        if (e.ClickCount == 2)
        {
            X = 0;
            Y = 0;
            e.Handled = true;
            return;
        }
        _dragging = true;
        e.Pointer.Capture(this);
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFrom(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
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
    private static Bitmap CreateWheel(int size)
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
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, handle.AddrOfPinnedObject(), new PixelSize(size, size), new Vector(96, 96), size * 4);
        }
        finally
        {
            handle.Free();
        }
    }
}
