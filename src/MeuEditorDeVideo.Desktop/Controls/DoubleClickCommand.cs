// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Executa um comando no duplo clique com o botão esquerdo (o <c>MouseBinding Gesture="LeftDoubleClick"</c> do WPF):
/// <c>ctl:DoubleClickCommand.Command="{Binding ResetCommand}"</c>. Escuta o clique no túnel, inclusive já tratado
/// (o controle deslizante trata o próprio clique), e não o marca como tratado.
/// </summary>
public static class DoubleClickCommand
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Interactive, Interactive, ICommand?>("Command");

    static DoubleClickCommand() => CommandProperty.Changed.AddClassHandler<Interactive>(OnCommandChanged);

    public static ICommand? GetCommand(Interactive element) => element.GetValue(CommandProperty);

    public static void SetCommand(Interactive element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(Interactive element, AvaloniaPropertyChangedEventArgs e)
    {
        element.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        if (e.NewValue is not null)
            element.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Interactive element || e.ClickCount != 2 || !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed) return;
        if (GetCommand(element) is { } command && command.CanExecute(null)) command.Execute(null);
    }
}
