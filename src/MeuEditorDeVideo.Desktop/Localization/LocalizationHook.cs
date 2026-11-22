using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;

namespace MeuEditorDeVideo.Desktop.Localization;

/// <summary>
/// Traduz os textos da interface no momento em que são atribuídos (no XAML, por estilos, recursos ou ligações):
/// TextBlock/Run, Content, Header, ToolTip, Title, marca d'água e os nomes de automação. O texto pt-BR é a chave
/// (<see cref="Translator"/>). Usa SetCurrentValue, que preserva as ligações. Equivale ao LocalizationHook do App
/// WPF, mas com manipuladores de classe por propriedade: no Avalonia, toda atribuição passa por eles, inclusive em
/// menus, abas e janelas criadas depois.
/// </summary>
public static class LocalizationHook
{
    private static bool _installed;
    [ThreadStatic] private static bool _busy;

    /// <summary>Propriedades de texto traduzidas (as de dono adicionado, como MenuItem.Header, têm o próprio Changed).</summary>
    public static IReadOnlyList<AvaloniaProperty> Properties { get; } =
    [
        TextBlock.TextProperty, Run.TextProperty, ContentControl.ContentProperty, HeaderedContentControl.HeaderProperty,
        HeaderedItemsControl.HeaderProperty, HeaderedSelectingItemsControl.HeaderProperty, MenuItem.HeaderProperty,
        TabItem.HeaderProperty, Expander.HeaderProperty, ToolTip.TipProperty, Window.TitleProperty,
        TextBox.PlaceholderTextProperty, AutomationProperties.NameProperty, AutomationProperties.HelpTextProperty,
    ];

    public static void Install()
    {
        if (_installed || !Translator.IsActive) return;
        _installed = true;
        foreach (var property in Properties.Distinct())
            property.Changed.Subscribe(new Observer(OnChanged));
    }

    private static void OnChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_busy || e.NewValue is not string s || s.Length == 0) return;
        // Run de uma só letra (ex.: separadores): nada a traduzir e evita trocar símbolos.
        if (e.Sender is Run && s.Trim().Length <= 1) return;
        var t = Translator.T(s);
        if (ReferenceEquals(t, s) || t == s) return;
        _busy = true;
        try { e.Sender.SetCurrentValue(e.Property, t); }
        finally { _busy = false; }
    }

    private sealed class Observer(Action<AvaloniaPropertyChangedEventArgs> onNext) : IObserver<AvaloniaPropertyChangedEventArgs>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(AvaloniaPropertyChangedEventArgs value) => onNext(value);
    }
}
