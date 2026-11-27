// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Presentation.ViewModels.Preview;

namespace MeuEditorDeVideo.Desktop.Views;

/// <summary>Pré-visualização em tela cheia (§6.5): mesmo quadro do monitor, fundo preto, Esc/F11/duplo clique para sair.</summary>
public sealed class FullScreenPreviewWindow : Window
{
    private readonly PreviewViewModel _preview;
    private readonly TextBlock _hint;

    public FullScreenPreviewWindow(PreviewViewModel preview)
    {
        _preview = preview;
        DataContext = preview;
        Title = "Pré-visualização em tela cheia";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        WindowState = WindowState.FullScreen;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        Topmost = true;

        var image = new FrameImage();
        image.Bind(FrameImage.FrameProperty, new Binding(nameof(PreviewViewModel.Image)));

        var hintText = Avalonia.Application.Current is { } app && app.TryGetResource("Editor.Monitor.SairTelaCheia", null, out var r) && r is string s
            ? s
            : "Esc ou F11 para sair · Espaço reproduz/pausa";
        _hint = new TextBlock
        {
            Text = hintText,
            Foreground = Brushes.White,
            Opacity = 0.7,
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var grid = new Grid();
        grid.Children.Add(image);
        grid.Children.Add(_hint);
        Content = grid;

        // A dica some após alguns segundos.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            _hint.IsVisible = false;
            timer.Stop();
        };
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape or Key.F11:
                Close();
                e.Handled = true;
                break;
            case Key.Space:
                if (_preview.PlayPauseCommand.CanExecute(null)) _preview.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _preview.Timeline.StepBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _preview.Timeline.StepForwardCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2) Close();
    }
}
