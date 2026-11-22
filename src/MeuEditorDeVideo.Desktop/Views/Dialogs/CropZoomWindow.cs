using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Converters;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>
/// Cortar e ampliar: o usuário marca no quadro do clipe a área que quer manter; ao aplicar, essa área é ampliada para
/// ocupar o quadro do projeto. A barra de tempo percorre o clipe para escolher um quadro de referência.
/// </summary>
public sealed class CropZoomWindow : Window
{
    private readonly CropCanvas _canvas;
    private readonly Slider _time;
    private readonly TextBlock _now;
    private readonly TextBlock _size;
    private readonly ComboBox _ratio;
    private readonly Func<TimeSpan, BgraImage?> _frameAt;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _step;
    private readonly IReadOnlyList<(string Label, double? Ratio)> _ratios;
    private TimeSpan? _pending;
    private bool _loading;

    /// <param name="frameAt">Quadro do clipe no instante dado (relativo ao início do clipe), como imagem BGRA; chamado fora da thread da interface.</param>
    public CropZoomWindow(Func<TimeSpan, BgraImage?> frameAt, TimeSpan duration, TimeSpan frameDuration, TimeSpan start,
        (int Width, int Height) media, (int Width, int Height) project, (double Left, double Top, double Right, double Bottom) crop)
    {
        _frameAt = frameAt;
        _duration = duration;
        _step = frameDuration > TimeSpan.Zero ? frameDuration : TimeSpan.FromSeconds(1 / 30.0);
        Title = "Cortar e ampliar";
        Width = 1000;
        Height = 720;
        MinWidth = 640;
        MinHeight = 480;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = this.GetResourceObservable("Med.WindowBackground").ToBinding();

        TextBlock Text(string text)
        {
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            t[!TextBlock.ForegroundProperty] = this.GetResourceObservable("Med.Text").ToBinding();
            return t;
        }

        var projectRatio = (double)project.Width / project.Height;
        _ratios =
        [
            ("Livre", null),
            (Translator.F("Proporção do projeto ({0})", Simplify(project.Width, project.Height)), projectRatio),
            ("Proporção original", (double)media.Width / media.Height),
            ("16:9", 16 / 9.0),
            ("9:16", 9 / 16.0),
            ("1:1", 1),
            ("4:3", 4 / 3.0),
            ("3:4", 3 / 4.0),
            ("4:5", 4 / 5.0),
        ];

        _canvas = new CropCanvas { MediaSize = media, Margin = new Thickness(0, 0, 0, 8) };
        var hasCrop = crop.Left + crop.Top + crop.Right + crop.Bottom > 0;
        _canvas.Ratio = hasCrop ? null : projectRatio;
        _canvas.Crop = hasCrop
            ? new Rect(crop.Left, crop.Top, 1 - crop.Left - crop.Right, 1 - crop.Top - crop.Bottom)
            : _canvas.Centered(new Point(0.5, 0.5), 0.8);
        _canvas.CropChanged += (_, _) => UpdateSize();

        // Tempo
        var prev = IconButton("", "Quadro anterior", () => _time!.Value = Math.Max(0, _time.Value - _step.TotalSeconds));
        var next = IconButton("", "Próximo quadro", () => _time!.Value = Math.Min(_time.Maximum, _time.Value + _step.TotalSeconds));
        var maximum = Math.Max(0, (duration - _step).TotalSeconds);
        _time = new Slider
        {
            Minimum = 0,
            Maximum = maximum,
            Value = Math.Clamp(start.TotalSeconds, 0, maximum),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
        };
        AutomationProperties.SetName(_time, "Posição no clipe");
        _time.ValueChanged += (_, _) => LoadFrame();
        _now = Text("");
        var total = Text(Format(duration));
        var transport = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var e in new Control[] { prev, next, _now })
        {
            DockPanel.SetDock(e, Dock.Left);
            transport.Children.Add(e);
        }
        DockPanel.SetDock(total, Dock.Right);
        transport.Children.Add(total);
        transport.Children.Add(_time);
        _now.Margin = new Thickness(8, 0, 0, 0);

        // Proporção e tamanho
        _ratio = new ComboBox { ItemsSource = _ratios.Select(r => r.Label).ToList(), SelectedIndex = hasCrop ? 0 : 1, MinWidth = 220, Margin = new Thickness(8, 0, 16, 0) };
        AutomationProperties.SetName(_ratio, "Proporção");
        _ratio.SelectionChanged += (_, _) =>
        {
            if (_ratio.SelectedIndex < 0) return;
            _canvas.Ratio = _ratios[_ratio.SelectedIndex].Ratio;
            if (_canvas.Ratio is not null)
            {
                var c = _canvas.Crop;
                var fit = _canvas.Centered(new Point(c.X + c.Width / 2, c.Y + c.Height / 2));
                // Mantém a área do mesmo tamanho aproximado, dentro do maior retângulo possível.
                var k = Math.Min(1, Math.Sqrt(c.Width * c.Height / (fit.Width * fit.Height)));
                _canvas.Crop = _canvas.Centered(new Point(c.X + c.Width / 2, c.Y + c.Height / 2), k);
            }
            _canvas.InvalidateVisual();
        };
        _size = Text("");
        AutomationProperties.SetName(_size, "Tamanho da área");
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        options.Children.Add(Text("Proporção"));
        options.Children.Add(_ratio);
        options.Children.Add(Text("Tamanho"));
        _size.Margin = new Thickness(8, 0, 0, 0);
        options.Children.Add(_size);

        // Botões
        var reset = new Button { Content = "Redefinir", MinWidth = 96 };
        AutomationProperties.SetName(reset, "Redefinir");
        reset.Click += (_, _) =>
        {
            _ratio.SelectedIndex = 0;
            _canvas.Crop = new Rect(0, 0, 1, 1);
        };
        var ok = new Button { Content = "Aplicar", IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Classes = { "med-accent" } };
        AutomationProperties.SetName(ok, "Aplicar");
        ok.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        AutomationProperties.SetName(cancel, "Cancelar");
        cancel.Click += (_, _) => Close(false);
        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(reset, Dock.Left);
        buttons.Children.Add(reset);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var hint = Text("Arraste no quadro para marcar a área que fica no vídeo. Ao aplicar, ela é ampliada para ocupar a tela.");
        hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, 8);
        hint[!TextBlock.ForegroundProperty] = this.GetResourceObservable("Med.TextMuted").ToBinding();

        var root = new DockPanel { Margin = new Thickness(16) };
        foreach (var (e, dock) in new (Control, Dock)[] { (hint, Dock.Top), (buttons, Dock.Bottom), (options, Dock.Bottom), (transport, Dock.Bottom) })
        {
            DockPanel.SetDock(e, dock);
            root.Children.Add(e);
        }
        root.Children.Add(_canvas);
        Content = root;

        UpdateSize();
        Opened += (_, _) => LoadFrame();
    }

    /// <summary>Recorte escolhido, em frações da mídia (esquerda, topo, direita, base).</summary>
    public (double Left, double Top, double Right, double Bottom) Crop
    {
        get
        {
            var c = _canvas.Crop;
            static double R(double v) => Math.Round(Math.Clamp(v, 0, 1), 5);
            return (R(c.Left), R(c.Top), R(1 - c.Right), R(1 - c.Bottom));
        }
    }

    private static Button IconButton(string glyph, string name, Action click)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, Classes = { "med-icon" } },
            Margin = new Thickness(0, 0, 4, 0),
        };
        ToolTip.SetTip(b, name);
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => click();
        return b;
    }

    private void UpdateSize()
    {
        var c = _canvas.Crop;
        var (w, h) = _canvas.MediaSize;
        _size.Text = $"{Math.Round(c.Width * w)} × {Math.Round(c.Height * h)}";
    }

    /// <summary>Carrega o quadro do instante da barra; pedidos feitos durante a leitura ficam só com o último.</summary>
    private async void LoadFrame()
    {
        var at = TimeSpan.FromSeconds(_time.Value);
        _now.Text = Format(at);
        _pending = at;
        if (_loading) return;
        _loading = true;
        try
        {
            while (_pending is { } t)
            {
                _pending = null;
                var frame = await Task.Run(() => _frameAt(t));
                if (frame is not null) _canvas.Frame = BgraBitmaps.ToBitmap(frame);
            }
        }
        catch (Exception)
        {
            // Sem quadro de referência: a área continua podendo ser marcada.
        }
        finally
        {
            _loading = false;
        }
    }

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture);

    private static string Simplify(int w, int h)
    {
        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        var g = Math.Max(1, Gcd(w, h));
        return $"{w / g}:{h / g}";
    }
}
