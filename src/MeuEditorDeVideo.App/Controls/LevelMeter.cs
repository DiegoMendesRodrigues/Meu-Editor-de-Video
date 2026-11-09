using System.Windows;
using System.Windows.Media;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Medidor de nível vertical (0…1 da escala −60…+6 dB), com retenção de pico por ~1,5 s.
/// Verde até −12 dB, amarelo até −3 dB, vermelho acima (escala fixa, como em mesas de som).
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLevel));

    private static readonly Brush Back = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)));
    private static readonly Brush Fill = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0xE5, 0x48, 0x4D), 0.0),
        new(Color.FromRgb(0xE5, 0x48, 0x4D), 9.0 / 66),
        new(Color.FromRgb(0xF2, 0xC2, 0x3A), 9.0 / 66 + 0.001),
        new(Color.FromRgb(0xF2, 0xC2, 0x3A), 18.0 / 66),
        new(Color.FromRgb(0x3F, 0xB9, 0x50), 18.0 / 66 + 0.001),
        new(Color.FromRgb(0x3F, 0xB9, 0x50), 1.0),
    }, new Point(0, 0), new Point(0, 1)));

    private double _peak;
    private DateTime _peakAt;

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static void OnLevel(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (LevelMeter)d;
        var v = (double)e.NewValue;
        if (v >= m._peak || (DateTime.UtcNow - m._peakAt).TotalSeconds > 1.5)
        {
            m._peak = v;
            m._peakAt = DateTime.UtcNow;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        var level = Math.Clamp(Level, 0, 1);
        if (level > 0)
        {
            // O gradiente cobre a altura toda; o recorte mostra só a parte acesa.
            dc.PushClip(new RectangleGeometry(new Rect(0, h * (1 - level), w, h * level)));
            dc.DrawRectangle(Fill, null, new Rect(0, 0, w, h));
            dc.Pop();
        }
        if (_peak > 0.01)
        {
            var y = h * (1 - Math.Clamp(_peak, 0, 1));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, Math.Max(0, y - 1), w, 2));
        }
    }
}
