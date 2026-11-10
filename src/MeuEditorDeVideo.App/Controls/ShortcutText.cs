using System.Windows;
using System.Windows.Controls;
using MeuEditorDeVideo.App.Services;

namespace MeuEditorDeVideo.App.Controls;

/// <summary>
/// Mostra no item de menu o atalho em vigor da ação (RF212): <c>c:ShortcutText.Action="projeto.salvar"</c>. Atualiza quando o
/// usuário altera os atalhos nas configurações.
/// </summary>
public static class ShortcutText
{
    public static readonly DependencyProperty ActionProperty = DependencyProperty.RegisterAttached(
        "Action", typeof(string), typeof(ShortcutText), new PropertyMetadata(null, OnActionChanged));

    public static string? GetAction(DependencyObject d) => (string?)d.GetValue(ActionProperty);

    public static void SetAction(DependencyObject d, string? value) => d.SetValue(ActionProperty, value);

    private static ShortcutService? Service =>
        System.Windows.Application.Current is App ? App.Services.GetService(typeof(ShortcutService)) as ShortcutService : null;

    private static void OnActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MenuItem item || Service is not { } service) return;
        Update(item, service);
        // Referência fraca: o item de menu não fica preso ao serviço.
        var weak = new WeakReference<MenuItem>(item);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (weak.TryGetTarget(out var target)) target.Dispatcher.BeginInvoke(() => Update(target, service));
            else service.Changed -= handler;
        };
        service.Changed += handler;
    }

    private static void Update(MenuItem item, ShortcutService service)
    {
        if (GetAction(item) is { } id) item.InputGestureText = service.Display(id);
    }
}
