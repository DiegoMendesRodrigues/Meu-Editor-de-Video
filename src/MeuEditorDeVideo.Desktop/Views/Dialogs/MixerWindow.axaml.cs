using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Janela não modal do mixer; libera o temporizador dos medidores ao fechar.</summary>
public partial class MixerWindow : Window
{
    public MixerWindow() => InitializeComponent();

    /// <param name="undo">Desfazer do editor (Ctrl+Z, Cmd+Z no macOS, também funciona com o mixer em foco).</param>
    /// <param name="redo">Refazer do editor (Ctrl+Y / Ctrl+Shift+Z).</param>
    public MixerWindow(MixerViewModel viewModel, ICommand undo, ICommand redo) : this()
    {
        DataContext = viewModel;
        var ctrl = MedModifiers.Control.ToAvalonia();
        KeyBindings.Add(new KeyBinding { Command = undo, Gesture = new KeyGesture(Key.Z, ctrl) });
        KeyBindings.Add(new KeyBinding { Command = redo, Gesture = new KeyGesture(Key.Y, ctrl) });
        KeyBindings.Add(new KeyBinding { Command = redo, Gesture = new KeyGesture(Key.Z, ctrl | KeyModifiers.Shift) });
        Closed += (_, _) => viewModel.Dispose();
    }

    // Duplo clique no fader ou na panorâmica volta ao padrão (no WPF era um MouseBinding LeftDoubleClick).
    private void OnPanDoubleTapped(object? sender, TappedEventArgs e) => Reset((sender as Control)?.DataContext, strip => strip.ResetPanCommand);

    private void OnVolumeDoubleTapped(object? sender, TappedEventArgs e) => Reset((sender as Control)?.DataContext, strip => strip.ResetVolumeCommand);

    private static void Reset(object? context, Func<MixerStripViewModel, ICommand> command)
    {
        if (context is MixerStripViewModel strip && command(strip) is { } c && c.CanExecute(null)) c.Execute(null);
    }
}
