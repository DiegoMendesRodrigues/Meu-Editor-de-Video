using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Text;

namespace MeuEditorDeVideo.ImageEngine.Composition;

/// <summary>
/// Desenha os elementos gráficos vetoriais (RF152) na resolução de saída — nítidos em qualquer escala — com a pilha
/// vetorial do WPF, na mesma thread STA dos textos. A forma fica centrada na imagem (o centro é a posição do clipe).
/// </summary>
public static class ShapeRasterizer
{
    private const int MaxSide = 16384;
    private static readonly ConcurrentDictionary<(ShapeStyle Style, double Scale), BgraImage> Cache = new();

    /// <param name="scale">Pixels de saída por pixel do projeto.</param>
    public static BgraImage Render(ShapeStyle style, double scale)
    {
        var key = (style, Math.Round(scale, 4));
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var image = TextRasterizer.OnWorker(() => RenderCore(style, key.Item2));
        if (Cache.Count >= 64) Cache.Clear();
        Cache[key] = image;
        return image;
    }

    private static BgraImage RenderCore(ShapeStyle style, double s)
    {
        var w = Math.Max(1, style.Width * s);
        var h = Math.Max(1, style.Height * s);
        var stroke = Math.Max(0, style.StrokeWidth * s);
        var margin = Math.Ceiling(stroke / 2 + 2);
        var width = (int)Math.Min(MaxSide, Math.Ceiling(w + 2 * margin));
        var height = (int)Math.Min(MaxSide, Math.Ceiling(h + 2 * margin));
        var rect = new Rect((width - w) / 2, (height - h) / 2, w, h);

        var fill = TextRasterizer.ParseColor(style.FillColor, Colors.White);
        var strokeColor = TextRasterizer.ParseColor(style.StrokeColor, Colors.Black);
        var geometry = Geometry(style, rect, s);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (style.Kind == ShapeKind.Line)
            {
                // Linha: traço com a cor de preenchimento e espessura = altura.
                var pen = new Pen(TextRasterizer.Brush(fill), h) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                var y = rect.Top + h / 2;
                dc.DrawLine(pen, new Point(rect.Left + h / 2, y), new Point(rect.Right - h / 2, y));
            }
            else
            {
                Pen? pen = null;
                if (stroke > 0 && strokeColor.A > 0)
                {
                    pen = new Pen(TextRasterizer.Brush(strokeColor), stroke) { LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                }
                dc.DrawGeometry(fill.A > 0 ? TextRasterizer.Brush(fill) : null, pen, geometry);
            }
        }
        var pixels = TextRasterizer.Rasterize(visual, width, height);
        TextRasterizer.Unpremultiply(pixels);
        return new BgraImage(width, height, pixels);
    }

    /// <summary>Geometria da forma dentro do retângulo (pixels de saída).</summary>
    public static Geometry Geometry(ShapeStyle style, Rect r, double scale)
    {
        Geometry g;
        var cx = r.Left + r.Width / 2;
        var cy = r.Top + r.Height / 2;
        switch (style.Kind)
        {
            case ShapeKind.Ellipse:
                g = new EllipseGeometry(r);
                break;
            case ShapeKind.Arrow:
            {
                var head = Math.Min(r.Width * 0.45, r.Height * 1.1);
                var shaft = r.Height * 0.38;
                g = Polygon([
                    new(r.Left, cy - shaft / 2), new(r.Right - head, cy - shaft / 2), new(r.Right - head, r.Top),
                    new(r.Right, cy), new(r.Right - head, r.Bottom), new(r.Right - head, cy + shaft / 2), new(r.Left, cy + shaft / 2),
                ]);
                break;
            }
            case ShapeKind.Star:
            {
                var n = Math.Clamp(style.Sides, 3, 24);
                var pts = new Point[n * 2];
                for (var i = 0; i < n * 2; i++)
                {
                    var a = -Math.PI / 2 + Math.PI * i / n;
                    var k = i % 2 == 0 ? 1 : 0.45;
                    pts[i] = new Point(cx + Math.Cos(a) * r.Width / 2 * k, cy + Math.Sin(a) * r.Height / 2 * k);
                }
                g = Polygon(pts);
                break;
            }
            case ShapeKind.Polygon:
            {
                var n = Math.Clamp(style.Sides, 3, 24);
                var pts = new Point[n];
                for (var i = 0; i < n; i++)
                {
                    var a = -Math.PI / 2 + 2 * Math.PI * i / n;
                    pts[i] = new Point(cx + Math.Cos(a) * r.Width / 2, cy + Math.Sin(a) * r.Height / 2);
                }
                g = Polygon(pts);
                break;
            }
            case ShapeKind.SpeechBubble:
            {
                var body = new Rect(r.Left, r.Top, r.Width, r.Height * 0.78);
                var radius = Math.Max(style.CornerRadius * scale, Math.Min(body.Width, body.Height) * 0.18);
                var tail = Polygon([
                    new(r.Left + r.Width * 0.2, body.Bottom - 1), new(r.Left + r.Width * 0.14, r.Bottom), new(r.Left + r.Width * 0.38, body.Bottom - 1),
                ]);
                g = new CombinedGeometry(GeometryCombineMode.Union, new RectangleGeometry(body, radius, radius), tail);
                break;
            }
            case ShapeKind.Heart:
            {
                var fig = new PathFigure { StartPoint = new Point(cx, r.Bottom), IsClosed = true };
                double X(double f) => r.Left + r.Width * f;
                double Y(double f) => r.Top + r.Height * f;
                fig.Segments.Add(new BezierSegment(new Point(X(0.1), Y(0.72)), new Point(X(-0.08), Y(0.3)), new Point(X(0.25), Y(0.05)), true));
                fig.Segments.Add(new BezierSegment(new Point(X(0.4), Y(-0.05)), new Point(X(0.5), Y(0.12)), new Point(cx, Y(0.25)), true));
                fig.Segments.Add(new BezierSegment(new Point(X(0.5), Y(0.12)), new Point(X(0.6), Y(-0.05)), new Point(X(0.75), Y(0.05)), true));
                fig.Segments.Add(new BezierSegment(new Point(X(1.08), Y(0.3)), new Point(X(0.9), Y(0.72)), new Point(cx, r.Bottom), true));
                g = new PathGeometry([fig]);
                break;
            }
            case ShapeKind.Line:
                g = new RectangleGeometry(r);
                break;
            default:
            {
                var radius = Math.Clamp(style.CornerRadius * scale, 0, Math.Min(r.Width, r.Height) / 2);
                g = new RectangleGeometry(r, radius, radius);
                break;
            }
        }
        g.Freeze();
        return g;
    }

    private static PathGeometry Polygon(Point[] points)
    {
        var fig = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        fig.Segments.Add(new PolyLineSegment(points.Skip(1), true));
        return new PathGeometry([fig]);
    }
}
