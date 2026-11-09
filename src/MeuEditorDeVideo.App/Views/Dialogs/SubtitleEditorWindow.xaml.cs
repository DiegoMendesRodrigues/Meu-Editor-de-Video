using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeuEditorDeVideo.App.ViewModels.Timeline;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela não modal do editor de legendas; Ctrl+Z/Ctrl+Y desfazem/refazem no editor.</summary>
public partial class SubtitleEditorWindow
{
    public SubtitleEditorWindow(SubtitleEditorViewModel viewModel, ICommand undo, ICommand redo)
    {
        InitializeComponent();
        DataContext = viewModel;
        InputBindings.Add(new KeyBinding(undo, Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(redo, Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Closed += (_, _) => viewModel.Dispose();
    }

    private static void Commit(object sender)
    {
        if (sender is FrameworkElement { DataContext: SubtitleRowViewModel row }) row.CommitTimes();
    }

    private void Tempo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit(sender);

    private void Tempo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Commit(sender);
        e.Handled = true;
    }

    private void Lista_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ItemsControl)sender, d) is ListBoxItem { DataContext: SubtitleRowViewModel row }
            && DataContext is SubtitleEditorViewModel vm)
            vm.GoToCommand.Execute(row);
    }
}
