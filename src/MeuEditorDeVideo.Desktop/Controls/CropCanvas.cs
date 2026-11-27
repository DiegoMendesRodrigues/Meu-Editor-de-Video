// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Quadro do vídeo com a área de corte por cima (janela Cortar e ampliar): fora da área fica escurecido; arrastar dentro
/// move a área, as alças dos cantos e das bordas mudam o tamanho e arrastar fora dela desenha uma área nova. Com uma
/// proporção fixa, só os cantos mudam o tamanho. A área é guardada em frações da imagem (0 a 1).
/// </summary>
public sealed class CropCanvas : Control
{
    private const double Handle = 8;
    private const double MinSize = 0.03;

    private static readonly ImmutableSolidColorBrush Teal = new ImmutableSolidColorBrush(Color.FromRgb(0x2E, 0xD3, 0xB7));
    private static readonly ImmutablePen LinePen = new ImmutablePen(Teal, 1.5);
    private static readonly ImmutablePen GridPen = new ImmutablePen(Teal, 1, new ImmutableDashStyle([4, 2], 0));
    private static readonly ImmutableSolidColorBrush Shade = new ImmutableSolidColorBrush(Color.FromArgb(140, 0, 0, 0));
    private static readonly ImmutableSolidColorBrush Empty = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));

    private enum Part { None, Move, New, Left, Top, Right, Bottom, TopLeft, TopRight, BottomRight, BottomLeft }

    private Rect _crop = new(0, 0, 1, 1);
    private IImage? _frame;
    private (int Width, int Height) _mediaSize = (16, 9);
    private double? _ratio;
    private Part _drag;
    private Point _start;
    private Rect _startCrop;

    public CropCanvas()
    {
        Focusable = true;
        AutomationProperties.SetName(this, "Área de corte");
    }

    /// <summary>Largura e altura da mídia, em pixels (definem a proporção da imagem).</summary>
    public (int Width, int Height) MediaSize
    {
        get => _mediaSize;
        set
        {
            _mediaSize = value;
            InvalidateVisual();
        }
    }

    /// <summary>Quadro de referência do clipe (no WPF era um BitmapSource); null mostra um fundo escuro.</summary>
    public IImage? Frame
    {
        get => _frame;
        set
        {
            _frame = value;
            InvalidateVisual();
        }
    }

    /// <summary>Proporção largura/altura da área em pixels da mídia; nulo = livre.</summary>
    public double? Ratio
    {
        get => _ratio;
        set
        {
            _ratio = value;
            InvalidateVisual();
        }
    }

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

    /// <summary>Retângulo da imagem na tela (ajuste uniforme ao controle); falso quando o controle não tem tamanho.</summary>
    private bool TryImageRect(out Rect rect)
    {
        var (mw, mh) = MediaSize;
        var s = Math.Min(Bounds.Width / Math.Max(1, mw), Bounds.Height / Math.Max(1, mh));
        if (!double.IsFinite(s) || s <= 0)
        {
            rect = default;
            return false;
        }
        double w = mw * s, h = mh * s;
        rect = new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        return true;
    }

    private static Rect ToScreen(Rect r, Rect img) => new(img.X + r.X * img.Width, img.Y + r.Y * img.Height, r.Width * img.Width, r.Height * img.Height);

    private static Point ToImage(Point p, Rect img) => new((p.X - img.X) / img.Width, (p.Y - img.Y) / img.Height);

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (!TryImageRect(out var img)) return;
        if (Frame is { } frame) context.DrawImage(frame, new Rect(frame.Size), img);
        else context.DrawRectangle(Empty, null, img);

        var c = ToScreen(_crop, img);
        context.DrawRectangle(Shade, null, new Rect(img.Left, img.Top, img.Width, c.Top - img.Top));
        context.DrawRectangle(Shade, null, new Rect(img.Left, c.Bottom, img.Width, img.Bottom - c.Bottom));
        context.DrawRectangle(Shade, null, new Rect(img.Left, c.Top, c.Left - img.Left, c.Height));
        context.DrawRectangle(Shade, null, new Rect(c.Right, c.Top, img.Right - c.Right, c.Height));

        for (var i = 1; i <= 2; i++)
        {
            var x = c.Left + c.Width * i / 3;
            var y = c.Top + c.Height * i / 3;
            context.DrawLine(GridPen, new Point(x, c.Top), new Point(x, c.Bottom));
            context.DrawLine(GridPen, new Point(c.Left, y), new Point(c.Right, y));
        }
        context.DrawRectangle(null, LinePen, c);
        foreach (var (_, p) in Handles(c))
            context.DrawRectangle(Teal, null, new Rect(p.X - Handle / 2, p.Y - Handle / 2, Handle, Handle));
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
        if (!TryImageRect(out var img)) return Part.None;
        var c = ToScreen(_crop, img);
        foreach (var (part, at) in Handles(c))
            if (ControlResources.Distance(at, p) <= Handle + 3) return part;
        if (c.Contains(p)) return Part.Move;
        return img.Contains(p) ? Part.New : Part.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        _drag = HitPart(p);
        if (_drag == Part.None || !TryImageRect(out var img)) return;
        Focus();
        _start = ToImage(p, img);
        _startCrop = _crop;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
        _drag = Part.None;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag == Part.None || e.Pointer.Captured != this)
        {
            Cursor = ControlResources.Cursor(HitPart(p) switch
            {
                Part.Move => StandardCursorType.SizeAll,
                Part.TopLeft or Part.BottomRight => StandardCursorType.TopLeftCorner,
                Part.TopRight or Part.BottomLeft => StandardCursorType.TopRightCorner,
                Part.Left or Part.Right => StandardCursorType.SizeWestEast,
                Part.Top or Part.Bottom => StandardCursorType.SizeNorthSouth,
                Part.New => StandardCursorType.Cross,
                _ => (StandardCursorType?)null,
            });
            return;
        }
        if (!TryImageRect(out var img)) return;
        var now = ToImage(p, img);
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
        if (Ratio is not { } ratio) return ControlResources.Between(anchor, now);
        var nr = NormalizedRatio(ratio);
        double dx = now.X - anchor.X, dy = now.Y - anchor.Y;
        double sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
        var w = Math.Max(Math.Abs(dx), Math.Abs(dy) * nr);
        // Limites: até a borda da imagem na direção do arraste.
        var maxW = sx > 0 ? 1 - anchor.X : anchor.X;
        var maxH = sy > 0 ? 1 - anchor.Y : anchor.Y;
        w = Math.Max(MinSize, Math.Min(w, Math.Min(maxW, maxH * nr)));
        var h = w / nr;
        return ControlResources.Between(anchor, new Point(anchor.X + sx * w, anchor.Y + sy * h));
    }
}
