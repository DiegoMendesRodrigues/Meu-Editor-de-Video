using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.ViewModels.Preview;

namespace MeuEditorDeVideo.App.Views;

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
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Background = Brushes.Black;
        ShowInTaskbar = false;
        Topmost = true;

        var image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(PreviewViewModel.Image)));

        _hint = new TextBlock
        {
            Text = (string)System.Windows.Application.Current.FindResource("Editor.Monitor.SairTelaCheia"),
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
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { _hint.Visibility = Visibility.Collapsed; timer.Stop(); };
        Loaded += (_, _) => timer.Start();
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) Close();
    }
}
