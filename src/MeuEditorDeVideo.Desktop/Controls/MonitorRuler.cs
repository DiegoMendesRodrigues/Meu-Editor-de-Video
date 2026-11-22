using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Réguas do monitor (Visualizar › Régua): faixas no alto e à esquerda da imagem, graduadas em pixels do vídeo
/// (resolução do projeto). O controle deve ter o mesmo tamanho e posição da imagem exibida.
/// </summary>
public sealed class MonitorRuler : Control
{
    public const double Band = 16;

    public static readonly StyledProperty<int> VideoWidthProperty = AvaloniaProperty.Register<MonitorRuler, int>(nameof(VideoWidth), 1920);

    public static readonly StyledProperty<int> VideoHeightProperty = AvaloniaProperty.Register<MonitorRuler, int>(nameof(VideoHeight), 1080);

    private static readonly ImmutableSolidColorBrush Back = new ImmutableSolidColorBrush(Color.FromArgb(170, 20, 20, 24));
    private static readonly ImmutablePen TickPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 1);

    static MonitorRuler() => AffectsRender<MonitorRuler>(VideoWidthProperty, VideoHeightProperty);

    public MonitorRuler() => IsHitTestVisible = false;

    public int VideoWidth
    {
        get => GetValue(VideoWidthProperty);
        set => SetValue(VideoWidthProperty, value);
    }

    public int VideoHeight
    {
        get => GetValue(VideoHeightProperty);
        set => SetValue(VideoHeightProperty, value);
    }

    /// <summary>Passo "redondo" (1, 2, 5 × 10ⁿ pixels do vídeo) com pelo menos <paramref name="minScreen"/> DIPs entre marcas.</summary>
    public static int Step(double scale, double minScreen)
    {
        foreach (var exp in new[] { 1, 10, 100, 1000, 10000 })
            foreach (var m in new[] { 1, 2, 5 })
                if (m * exp * scale >= minScreen) return m * exp;
        return 10000;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < Band * 2 || h < Band * 2 || VideoWidth <= 0 || VideoHeight <= 0) return;
        context.DrawRectangle(Back, null, new Rect(0, 0, w, Band));
        context.DrawRectangle(Back, null, new Rect(0, Band, Band, h - Band));

        var sx = w / VideoWidth;
        var step = Step(sx, 60);
        var minor = Math.Max(1, step / 5);
        for (var v = 0; v <= VideoWidth; v += minor)
        {
            var x = Math.Round(v * sx) + 0.5;
            var major = v % step == 0;
            context.DrawLine(TickPen, new Point(x, major ? 0 : Band * 0.6), new Point(x, Band));
            if (major && x + 2 < w - 20)
                context.DrawText(Text(v), new Point(x + 2, 1));
        }

        var sy = h / VideoHeight;
        step = Step(sy, 60);
        minor = Math.Max(1, step / 5);
        for (var v = 0; v <= VideoHeight; v += minor)
        {
            var y = Math.Round(v * sy) + 0.5;
            if (y < Band) continue;
            var major = v % step == 0;
            context.DrawLine(TickPen, new Point(major ? 0 : Band * 0.6, y), new Point(Band, y));
            if (major && y + 24 < h)
            {
                // Texto girado −90° em torno de (1, y + 2), terminando junto à marca.
                var t = Text(v);
                var pivot = new Point(1, y + 2);
                var matrix = Matrix.CreateTranslation(-pivot.X, -pivot.Y) * Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(pivot.X, pivot.Y);
                using (context.PushTransform(matrix))
                    context.DrawText(t, new Point(1 - t.Width, y + 2));
            }
        }
    }

    private static FormattedText Text(int value) =>
        new(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 9, Brushes.White);
}
