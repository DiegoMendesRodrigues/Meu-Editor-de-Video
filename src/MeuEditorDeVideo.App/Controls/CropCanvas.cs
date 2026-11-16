using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Quadro do vídeo com a área de corte por cima (janela Cortar e ampliar): fora da área fica escurecido; arrastar dentro
/// move a área, as alças dos cantos e das bordas mudam o tamanho e arrastar fora dela desenha uma área nova. Com uma
/// proporção fixa, só os cantos mudam o tamanho. A área é guardada em frações da imagem (0 a 1).
/// </summary>
public sealed class CropCanvas : FrameworkElement
{
    private const double Handle = 8;
    private const double MinSize = 0.03;

    private static readonly Pen LinePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0xD3, 0xB7)), 1.5));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0xD3, 0xB7)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Brush HandleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0xD3, 0xB7)));
    private static readonly Brush Shade = Frozen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)));
    private static readonly Brush Empty = Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)));

    private enum Part { None, Move, New, Left, Top, Right, Bottom, TopLeft, TopRight, BottomRight, BottomLeft }

    private Rect _crop = new(0, 0, 1, 1);
    private Part _drag;
    private Point _start;
    private Rect _startCrop;

    public CropCanvas()
    {
        Focusable = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "Área de corte");
    }

    /// <summary>Largura e altura da mídia, em pixels (definem a proporção da imagem).</summary>
    public (int Width, int Height) MediaSize { get; set; } = (16, 9);

    public BitmapSource? Frame
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>Proporção largura/altura da área em pixels da mídia; nulo = livre.</summary>
    public double? Ratio { get; set; }

    /// <summary>Área de corte em frações da imagem.</summary>
    public Rect Crop
    {
        get => _crop;
        set
        {
            _crop = Clamp(value);
            InvalidateVisual();
            CropChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CropChanged;

    /// <summary>Maior área com a proporção <see cref="Ratio"/> que cabe na imagem, com <paramref name="size"/> dessa área, centralizada em <paramref name="center"/>.</summary>
    public Rect Centered(Point center, double size = 1)
    {
        if (Ratio is not { } ratio) return new Rect(0, 0, 1, 1);
        var nr = NormalizedRatio(ratio);
        double w = 1, h = 1 / nr;
        if (h > 1) (w, h) = (nr, 1);
        w *= size;
        h *= size;
        return Clamp(new Rect(center.X - w / 2, center.Y - h / 2, w, h));
    }

    /// <summary>Proporção em frações da imagem (largura normalizada / altura normalizada).</summary>
    private double NormalizedRatio(double ratio) => ratio * MediaSize.Height / Math.Max(1, MediaSize.Width);

    private static Rect Clamp(Rect r)
    {
        var w = Math.Clamp(r.Width, MinSize, 1);
        var h = Math.Clamp(r.Height, MinSize, 1);
        return new Rect(Math.Clamp(r.X, 0, 1 - w), Math.Clamp(r.Y, 0, 1 - h), w, h);
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    /// <summary>Retângulo da imagem na tela (ajuste uniforme ao controle).</summary>
    private Rect ImageRect()
    {
        var (mw, mh) = MediaSize;
        var s = Math.Min(ActualWidth / Math.Max(1, mw), ActualHeight / Math.Max(1, mh));
        if (!double.IsFinite(s) || s <= 0) return Rect.Empty;
        double w = mw * s, h = mh * s;
        return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
    }

    private Rect ToScreen(Rect r, Rect img) => new(img.X + r.X * img.Width, img.Y + r.Y * img.Height, r.Width * img.Width, r.Height * img.Height);

    private Point ToImage(Point p, Rect img) => new((p.X - img.X) / img.Width, (p.Y - img.Y) / img.Height);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var img = ImageRect();
        if (img.IsEmpty) return;
        if (Frame is { } frame) dc.DrawImage(frame, img);
        else dc.DrawRectangle(Empty, null, img);

        var c = ToScreen(_crop, img);
        dc.DrawRectangle(Shade, null, new Rect(img.Left, img.Top, img.Width, c.Top - img.Top));
        dc.DrawRectangle(Shade, null, new Rect(img.Left, c.Bottom, img.Width, img.Bottom - c.Bottom));
        dc.DrawRectangle(Shade, null, new Rect(img.Left, c.Top, c.Left - img.Left, c.Height));
        dc.DrawRectangle(Shade, null, new Rect(c.Right, c.Top, img.Right - c.Right, c.Height));

        for (var i = 1; i <= 2; i++)
        {
            var x = c.Left + c.Width * i / 3;
            var y = c.Top + c.Height * i / 3;
            dc.DrawLine(GridPen, new Point(x, c.Top), new Point(x, c.Bottom));
            dc.DrawLine(GridPen, new Point(c.Left, y), new Point(c.Right, y));
        }
        dc.DrawRectangle(null, LinePen, c);
        foreach (var (_, p) in Handles(c))
            dc.DrawRectangle(HandleBrush, null, new Rect(p.X - Handle / 2, p.Y - Handle / 2, Handle, Handle));
    }

    private IEnumerable<(Part Part, Point At)> Handles(Rect c)
    {
        yield return (Part.TopLeft, c.TopLeft);
        yield return (Part.TopRight, c.TopRight);
        yield return (Part.BottomRight, c.BottomRight);
        yield return (Part.BottomLeft, c.BottomLeft);
        if (Ratio is not null) yield break;
        double mx = (c.Left + c.Right) / 2, my = (c.Top + c.Bottom) / 2;
        yield return (Part.Top, new Point(mx, c.Top));
        yield return (Part.Bottom, new Point(mx, c.Bottom));
        yield return (Part.Left, new Point(c.Left, my));
        yield return (Part.Right, new Point(c.Right, my));
    }

    private Part HitPart(Point p)
    {
        var img = ImageRect();
        if (img.IsEmpty) return Part.None;
        var c = ToScreen(_crop, img);
        foreach (var (part, at) in Handles(c))
            if ((at - p).Length <= Handle + 3) return part;
        if (c.Contains(p)) return Part.Move;
        return img.Contains(p) ? Part.New : Part.None;
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hit) => new PointHitTestResult(this, hit.HitPoint);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        _drag = HitPart(p);
        if (_drag == Part.None) return;
        Focus();
        _start = ToImage(p, ImageRect());
        _startCrop = _crop;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        _drag = Part.None;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (!IsMouseCaptured)
        {
            Cursor = HitPart(p) switch
            {
                Part.Move => Cursors.SizeAll,
                Part.TopLeft or Part.BottomRight => Cursors.SizeNWSE,
                Part.TopRight or Part.BottomLeft => Cursors.SizeNESW,
                Part.Left or Part.Right => Cursors.SizeWE,
                Part.Top or Part.Bottom => Cursors.SizeNS,
                Part.New => Cursors.Cross,
                _ => null,
            };
            return;
        }
        var now = ToImage(p, ImageRect());
        now = new Point(Math.Clamp(now.X, 0, 1), Math.Clamp(now.Y, 0, 1));
        var s = _startCrop;
        Rect r;
        switch (_drag)
        {
            case Part.Move:
                r = new Rect(s.X + now.X - _start.X, s.Y + now.Y - _start.Y, s.Width, s.Height);
                break;
            case Part.New:
                r = FromCorners(_start, now);
                break;
            case Part.TopLeft:
                r = FromCorners(s.BottomRight, now);
                break;
            case Part.TopRight:
                r = FromCorners(s.BottomLeft, now);
                break;
            case Part.BottomRight:
                r = FromCorners(s.TopLeft, now);
                break;
            case Part.BottomLeft:
                r = FromCorners(s.TopRight, now);
                break;
            case Part.Left:
                r = new Rect(new Point(Math.Min(now.X, s.Right - MinSize), s.Top), s.BottomRight);
                break;
            case Part.Right:
                r = new Rect(s.TopLeft, new Point(Math.Max(now.X, s.Left + MinSize), s.Bottom));
                break;
            case Part.Top:
                r = new Rect(new Point(s.Left, Math.Min(now.Y, s.Bottom - MinSize)), s.BottomRight);
                break;
            case Part.Bottom:
                r = new Rect(s.TopLeft, new Point(s.Right, Math.Max(now.Y, s.Top + MinSize)));
                break;
            default:
                return;
        }
        Crop = r;
    }

    /// <summary>Retângulo entre o canto fixo e o ponteiro; com proporção fixa, a altura acompanha a largura e não sai da imagem.</summary>
    private Rect FromCorners(Point anchor, Point now)
    {
        if (Ratio is not { } ratio) return new Rect(anchor, now);
        var nr = NormalizedRatio(ratio);
        double dx = now.X - anchor.X, dy = now.Y - anchor.Y;
        double sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
        var w = Math.Max(Math.Abs(dx), Math.Abs(dy) * nr);
        // Limites: até a borda da imagem na direção do arraste.
        var maxW = sx > 0 ? 1 - anchor.X : anchor.X;
        var maxH = sy > 0 ? 1 - anchor.Y : anchor.Y;
        w = Math.Max(MinSize, Math.Min(w, Math.Min(maxW, maxH * nr)));
        var h = w / nr;
        return new Rect(anchor, new Point(anchor.X + sx * w, anchor.Y + sy * h));
    }
}
