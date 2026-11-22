using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>Ajudantes dos controles desenhados à mão: recursos do tema, retângulos e cursores.</summary>
internal static class ControlResources
{
    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    /// <summary>Pincel do tema atual (<c>Med.*</c>), ou o reserva quando o recurso não existe.</summary>
    public static IBrush Brush(this Control control, string key, IBrush fallback) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    public static IBrush Solid(byte a, byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromArgb(a, r, g, b));

    /// <summary>Retângulo entre dois pontos quaisquer (o <c>Rect(Point, Point)</c> do WPF normalizava; o do Avalonia não).</summary>
    public static Rect Between(Point a, Point b) =>
        new(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    public static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Cursor padrão do sistema, criado uma vez só; null = cursor herdado.</summary>
    public static Cursor? Cursor(StandardCursorType? type)
    {
        if (type is not { } t) return null;
        if (!Cursors.TryGetValue(t, out var cursor)) Cursors[t] = cursor = new Cursor(t);
        return cursor;
    }

    /// <summary>Polígono fechado preenchível.</summary>
    public static StreamGeometry Polygon(IReadOnlyList<Point> points)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], true);
            for (var i = 1; i < points.Count; i++) ctx.LineTo(points[i]);
            ctx.EndFigure(true);
        }
        return g;
    }
}
