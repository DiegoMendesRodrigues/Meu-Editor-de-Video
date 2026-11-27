// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Desktop.Controls;

/// <summary>
/// Mostra no item de menu o atalho em vigor da ação (RF212): <c>ctl:ShortcutText.Action="projeto.salvar"</c>. Atualiza
/// quando o usuário altera os atalhos nas configurações. No macOS mostra Cmd/Option (<see cref="KeyChord.Display"/>).
/// O atalho é tratado pela janela do editor (<see cref="ShortcutService.Find"/>); aqui é só o texto do menu.
/// </summary>
public sealed class ShortcutText : AvaloniaObject
{
    public static readonly AttachedProperty<string?> ActionProperty =
        AvaloniaProperty.RegisterAttached<ShortcutText, MenuItem, string?>("Action");

    static ShortcutText() => ActionProperty.Changed.AddClassHandler<MenuItem>(OnActionChanged);

    public static string? GetAction(MenuItem item) => item.GetValue(ActionProperty);

    public static void SetAction(MenuItem item, string? value) => item.SetValue(ActionProperty, value);

    private static ShortcutService? Service => App.ServicesOrNull?.GetService<ShortcutService>();

    private static void OnActionChanged(MenuItem item, AvaloniaPropertyChangedEventArgs e)
    {
        if (Service is not { } service) return;
        Update(item, service);
        // Referência fraca: o item de menu não fica preso ao serviço.
        var weak = new WeakReference<MenuItem>(item);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (weak.TryGetTarget(out var target)) Dispatcher.UIThread.Post(() => Update(target, service));
            else service.Changed -= handler;
        };
        service.Changed += handler;
    }

    private static void Update(MenuItem item, ShortcutService service)
    {
        if (GetAction(item) is not { } id) return;
        // InputGesture só desenha o texto; o atalho em si é tratado pelo editor. Texto livre (Display) não é um gesto
        // válido do Avalonia, então o primeiro atalho é convertido.
        var chord = service.ChordsFor(id).FirstOrDefault();
        item.InputGesture = chord == default ? null : new KeyGesture(chord.Key.ToAvalonia(), chord.Modifiers.ToAvalonia());
    }
}
