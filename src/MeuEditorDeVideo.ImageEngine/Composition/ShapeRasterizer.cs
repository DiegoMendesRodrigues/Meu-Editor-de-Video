// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Concurrent;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Text;
using SkiaSharp;

namespace MeuEditorDeVideo.ImageEngine.Composition;

/// <summary>
/// Desenha os elementos gráficos vetoriais (RF152) na resolução de saída — nítidos em qualquer escala — com SkiaSharp.
/// A forma fica centrada na imagem (o centro é a posição do clipe).
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
        var image = RenderCore(style, key.Item2);
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
        var rect = SKRect.Create((float)((width - w) / 2), (float)((height - h) / 2), (float)w, (float)h);

        var fill = TextRasterizer.ParseColor(style.FillColor, SKColors.White);
        var strokeColor = TextRasterizer.ParseColor(style.StrokeColor, SKColors.Black);
        var pixels = TextRasterizer.Draw(width, height, canvas =>
        {
            if (style.Kind == ShapeKind.Line)
            {
                // Linha: traço com a cor de preenchimento e espessura = altura.
                using var pen = TextRasterizer.Stroke(fill, (float)h, SKStrokeCap.Round);
                var y = rect.Top + (float)h / 2;
                canvas.DrawLine(rect.Left + (float)h / 2, y, rect.Right - (float)h / 2, y, pen);
                return;
            }
            using var path = Path(style, rect, s);
            if (fill.Alpha > 0)
            {
                using var paint = TextRasterizer.Paint(fill);
                canvas.DrawPath(path, paint);
            }
            if (stroke > 0 && strokeColor.Alpha > 0)
            {
                using var pen = TextRasterizer.Stroke(strokeColor, (float)stroke);
                canvas.DrawPath(path, pen);
            }
        });
        TextRasterizer.Unpremultiply(pixels);
        return new BgraImage(width, height, pixels);
    }

    /// <summary>Contorno da forma dentro do retângulo (pixels de saída).</summary>
    public static SKPath Path(ShapeStyle style, SKRect r, double scale)
    {
        using var path = new SKPathBuilder();
        var cx = r.MidX;
        var cy = r.MidY;
        switch (style.Kind)
        {
            case ShapeKind.Ellipse:
                path.AddOval(r);
                break;
            case ShapeKind.Arrow:
            {
                var head = Math.Min(r.Width * 0.45f, r.Height * 1.1f);
                var shaft = r.Height * 0.38f;
                path.AddPoly([
                    new(r.Left, cy - shaft / 2), new(r.Right - head, cy - shaft / 2), new(r.Right - head, r.Top),
                    new(r.Right, cy), new(r.Right - head, r.Bottom), new(r.Right - head, cy + shaft / 2), new(r.Left, cy + shaft / 2),
                ]);
                break;
            }
            case ShapeKind.Star:
            {
                var n = Math.Clamp(style.Sides, 3, 24);
                var pts = new SKPoint[n * 2];
                for (var i = 0; i < n * 2; i++)
                {
                    var a = -Math.PI / 2 + Math.PI * i / n;
                    var k = i % 2 == 0 ? 1 : 0.45;
                    pts[i] = new SKPoint((float)(cx + Math.Cos(a) * r.Width / 2 * k), (float)(cy + Math.Sin(a) * r.Height / 2 * k));
                }
                path.AddPoly(pts);
                break;
            }
            case ShapeKind.Polygon:
            {
                var n = Math.Clamp(style.Sides, 3, 24);
                var pts = new SKPoint[n];
                for (var i = 0; i < n; i++)
                {
                    var a = -Math.PI / 2 + 2 * Math.PI * i / n;
                    pts[i] = new SKPoint((float)(cx + Math.Cos(a) * r.Width / 2), (float)(cy + Math.Sin(a) * r.Height / 2));
                }
                path.AddPoly(pts);
                break;
            }
            case ShapeKind.SpeechBubble:
            {
                var body = SKRect.Create(r.Left, r.Top, r.Width, r.Height * 0.78f);
                var radius = (float)Math.Max(style.CornerRadius * scale, Math.Min(body.Width, body.Height) * 0.18);
                using var boxBuilder = new SKPathBuilder();
                boxBuilder.AddRoundRect(body, radius, radius);
                using var box = boxBuilder.Detach();
                using var tailBuilder = new SKPathBuilder();
                tailBuilder.AddPoly([
                    new(r.Left + r.Width * 0.2f, body.Bottom - 1), new(r.Left + r.Width * 0.14f, r.Bottom), new(r.Left + r.Width * 0.38f, body.Bottom - 1),
                ]);
                using var tail = tailBuilder.Detach();
                // União: o contorno passa por fora do balão e da ponta, sem a linha entre eles.
                using var union = box.Op(tail, SKPathOp.Union);
                path.AddPath(union ?? box);
                break;
            }
            case ShapeKind.Heart:
            {
                float X(double f) => (float)(r.Left + r.Width * f);
                float Y(double f) => (float)(r.Top + r.Height * f);
                path.MoveTo(cx, r.Bottom);
                path.CubicTo(X(0.1), Y(0.72), X(-0.08), Y(0.3), X(0.25), Y(0.05));
                path.CubicTo(X(0.4), Y(-0.05), X(0.5), Y(0.12), cx, Y(0.25));
                path.CubicTo(X(0.5), Y(0.12), X(0.6), Y(-0.05), X(0.75), Y(0.05));
                path.CubicTo(X(1.08), Y(0.3), X(0.9), Y(0.72), cx, r.Bottom);
                path.Close();
                break;
            }
            case ShapeKind.Line:
                path.AddRect(r);
                break;
            default:
            {
                var radius = (float)Math.Clamp(style.CornerRadius * scale, 0, Math.Min(r.Width, r.Height) / 2);
                path.AddRoundRect(r, radius, radius);
                break;
            }
        }
        return path.Detach();
    }
}
