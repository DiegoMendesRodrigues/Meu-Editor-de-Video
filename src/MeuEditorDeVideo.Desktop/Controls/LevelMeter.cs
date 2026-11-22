using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Medidor de nível vertical (0…1 da escala −60…+6 dB), com retenção de pico por ~1,5 s.
/// Verde até −12 dB, amarelo até −3 dB, vermelho acima (escala fixa, como em mesas de som).
/// </summary>
public sealed class LevelMeter : Control
{
    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(Level));

    private static readonly ImmutableSolidColorBrush Back = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));

    private static readonly IBrush Fill = new ImmutableLinearGradientBrush(
    [
        new ImmutableGradientStop(0.0, Color.FromRgb(0xE5, 0x48, 0x4D)),
        new ImmutableGradientStop(9.0 / 66, Color.FromRgb(0xE5, 0x48, 0x4D)),
        new ImmutableGradientStop(9.0 / 66 + 0.001, Color.FromRgb(0xF2, 0xC2, 0x3A)),
        new ImmutableGradientStop(18.0 / 66, Color.FromRgb(0xF2, 0xC2, 0x3A)),
        new ImmutableGradientStop(18.0 / 66 + 0.001, Color.FromRgb(0x3F, 0xB9, 0x50)),
        new ImmutableGradientStop(1.0, Color.FromRgb(0x3F, 0xB9, 0x50)),
    ], startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    private double _peak;
    private DateTime _peakAt;

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(LevelProperty);
        LevelProperty.Changed.AddClassHandler<LevelMeter>((m, e) =>
        {
            var v = (double)e.NewValue!;
            if (v >= m._peak || (DateTime.UtcNow - m._peakAt).TotalSeconds > 1.5)
            {
                m._peak = v;
                m._peakAt = DateTime.UtcNow;
            }
        });
    }

    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        context.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        var level = Math.Clamp(Level, 0, 1);
        if (level > 0)
        {
            // O gradiente cobre a altura toda; o recorte mostra só a parte acesa.
            using (context.PushClip(new Rect(0, h * (1 - level), w, h * level)))
                context.DrawRectangle(Fill, null, new Rect(0, 0, w, h));
        }
        if (_peak > 0.01)
        {
            var y = h * (1 - Math.Clamp(_peak, 0, 1));
            context.DrawRectangle(Brushes.White, null, new Rect(0, Math.Max(0, y - 1), w, 2));
        }
    }
}
