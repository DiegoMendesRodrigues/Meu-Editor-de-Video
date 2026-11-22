using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using ICommand = System.Windows.Input.ICommand;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Janela não modal do editor de legendas; Ctrl+Z/Ctrl+Y (Cmd no macOS) desfazem/refazem no editor.</summary>
public partial class SubtitleEditorWindow : Window
{
    public SubtitleEditorWindow() { InitializeComponent(); }

    public SubtitleEditorWindow(SubtitleEditorViewModel viewModel, ICommand undo, ICommand redo)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Segundos com vírgula decimal (pt-BR), como no WPF.
        DeslocarBox.Bind(TextBox.TextProperty, new Binding(nameof(SubtitleEditorViewModel.ShiftSeconds)) { ConverterCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR") });
        var ctrl = MedModifiers.Control.ToAvalonia();
        KeyBindings.Add(new KeyBinding { Command = undo, Gesture = new KeyGesture(Key.Z, ctrl) });
        KeyBindings.Add(new KeyBinding { Command = redo, Gesture = new KeyGesture(Key.Y, ctrl) });
        KeyBindings.Add(new KeyBinding { Command = redo, Gesture = new KeyGesture(Key.Z, ctrl | KeyModifiers.Shift) });
        // Os campos de tempo de cada linha confirmam ao perder o foco ou com Enter; os eventos sobem até a janela.
        AddHandler(LostFocusEvent, Tempo_LostFocus, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, Tempo_KeyDown, RoutingStrategies.Bubble);
        Closed += (_, _) => viewModel.Dispose();
    }

    private static void Commit(object? sender)
    {
        if (sender is TextBox { DataContext: SubtitleRowViewModel row } box && box.Classes.Contains("time")) row.CommitTimes();
    }

    private void Tempo_LostFocus(object? sender, RoutedEventArgs e) => Commit(e.Source);

    private void Tempo_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is not TextBox { DataContext: SubtitleRowViewModel } box || !box.Classes.Contains("time")) return;
        Commit(box);
        e.Handled = true;
    }

    /// <summary>Duplo clique numa legenda leva o cursor da linha do tempo até ela.</summary>
    private void Lista_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: SubtitleRowViewModel row } && DataContext is SubtitleEditorViewModel vm)
            vm.GoToCommand.Execute(row);
    }
}
