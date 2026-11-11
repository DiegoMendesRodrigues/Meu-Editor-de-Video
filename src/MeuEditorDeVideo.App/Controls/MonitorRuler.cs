using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Réguas do monitor (Visualizar › Régua): faixas no alto e à esquerda da imagem, graduadas em pixels do vídeo
/// (resolução do projeto). O controle deve ter o mesmo tamanho e posição da imagem exibida.
/// </summary>
public sealed class MonitorRuler : FrameworkElement
{
    public const double Band = 16;

    public static readonly DependencyProperty VideoWidthProperty = DependencyProperty.Register(nameof(VideoWidth), typeof(int), typeof(MonitorRuler),
        new FrameworkPropertyMetadata(1920, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VideoHeightProperty = DependencyProperty.Register(nameof(VideoHeight), typeof(int), typeof(MonitorRuler),
        new FrameworkPropertyMetadata(1080, FrameworkPropertyMetadataOptions.AffectsRender));

    public int VideoWidth
    {
        get => (int)GetValue(VideoWidthProperty);
        set => SetValue(VideoWidthProperty, value);
    }

    public int VideoHeight
    {
        get => (int)GetValue(VideoHeightProperty);
        set => SetValue(VideoHeightProperty, value);
    }

    public MonitorRuler()
    {
        IsHitTestVisible = false;
    }

    /// <summary>Passo "redondo" (1, 2, 5 × 10ⁿ pixels do vídeo) com pelo menos <paramref name="minScreen"/> DIPs entre marcas.</summary>
    public static int Step(double scale, double minScreen)
    {
        foreach (var exp in new[] { 1, 10, 100, 1000, 10000 })
            foreach (var m in new[] { 1, 2, 5 })
                if (m * exp * scale >= minScreen) return m * exp;
        return 10000;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < Band * 2 || h < Band * 2 || VideoWidth <= 0 || VideoHeight <= 0) return;
        var back = new SolidColorBrush(Color.FromArgb(170, 20, 20, 24));
        var ink = Brushes.White;
        var pen = new Pen(ink, 1);
        back.Freeze();
        pen.Freeze();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");

        dc.DrawRectangle(back, null, new Rect(0, 0, w, Band));
        dc.DrawRectangle(back, null, new Rect(0, Band, Band, h - Band));

        var sx = w / VideoWidth;
        var step = Step(sx, 60);
        var minor = Math.Max(1, step / 5);
        for (var v = 0; v <= VideoWidth; v += minor)
        {
            var x = Math.Round(v * sx) + 0.5;
            var major = v % step == 0;
            dc.DrawLine(pen, new Point(x, major ? 0 : Band * 0.6), new Point(x, Band));
            if (major && x + 2 < w - 20)
                dc.DrawText(Text(v, ink, typeface, dpi), new Point(x + 2, 1));
        }

        var sy = h / VideoHeight;
        step = Step(sy, 60);
        minor = Math.Max(1, step / 5);
        for (var v = 0; v <= VideoHeight; v += minor)
        {
            var y = Math.Round(v * sy) + 0.5;
            if (y < Band) continue;
            var major = v % step == 0;
            dc.DrawLine(pen, new Point(major ? 0 : Band * 0.6, y), new Point(Band, y));
            if (major && y + 24 < h)
            {
                dc.PushTransform(new RotateTransform(-90, 1, y + 2));
                var t = Text(v, ink, typeface, dpi);
                dc.DrawText(t, new Point(1 - t.Width, y + 2));
                dc.Pop();
            }
        }
    }

    private static FormattedText Text(int value, Brush ink, Typeface typeface, double dpi) =>
        new(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, ink, dpi);
}
