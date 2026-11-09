using System.Windows.Input;
using MeuEditorDeVideo.App.ViewModels.Timeline;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela não modal do mixer; libera o temporizador dos medidores ao fechar.</summary>
public partial class MixerWindow
{
    /// <param name="undo">Desfazer do editor (Ctrl+Z também funciona com o mixer em foco).</param>
    /// <param name="redo">Refazer do editor (Ctrl+Y / Ctrl+Shift+Z).</param>
    public MixerWindow(MixerViewModel viewModel, ICommand undo, ICommand redo)
    {
        InitializeComponent();
        DataContext = viewModel;
        InputBindings.Add(new KeyBinding(undo, Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(redo, Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Closed += (_, _) => viewModel.Dispose();
    }
}
