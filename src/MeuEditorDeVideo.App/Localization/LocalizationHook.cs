using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MeuEditorDeVideo.App.Localization;

/// <summary>
/// Traduz os textos dos elementos WPF quando eles são carregados e sempre que mudam (inclusive valores vindos de
/// ligações): TextBlock/Run, Content, Header, ToolTip, Title e os nomes de automação. Usa SetCurrentValue, que
/// preserva as ligações.
/// </summary>
public static class LocalizationHook
{
    private static readonly DependencyProperty[] Properties =
    [
        TextBlock.TextProperty, ContentControl.ContentProperty, HeaderedContentControl.HeaderProperty,
        HeaderedItemsControl.HeaderProperty, FrameworkElement.ToolTipProperty, Window.TitleProperty,
        AutomationProperties.NameProperty, AutomationProperties.HelpTextProperty,
    ];

    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached("Attached", typeof(bool), typeof(LocalizationHook));

    [ThreadStatic] private static bool _busy;

    public static void Install()
    {
        if (!Translator.IsActive) return;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnUnloaded), true);
        // Conteúdo criado depois (submenus, menus de contexto, abas): traduz a árvore quando aparece.
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent,
            new RoutedEventHandler((s, _) => Defer(s as MenuItem, TranslateItems)), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((s, _) => Defer(s as DependencyObject, TranslateTree)), true);
        EventManager.RegisterClassHandler(typeof(TabControl), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler((s, e) => { if (ReferenceEquals(e.OriginalSource, s)) Defer(s as DependencyObject, TranslateTree); }), true);
    }

    private static void Defer<T>(T? root, Action<T> action) where T : DependencyObject
    {
        if (root is null) return;
        root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => action(root));
    }

    /// <summary>Itens de um submenu ficam num Popup, fora da árvore visual do MenuItem.</summary>
    private static void TranslateItems(MenuItem menu)
    {
        foreach (var item in menu.Items)
            if (menu.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container)
                TranslateTree(container);
    }

    /// <summary>
    /// Traduz <paramref name="root"/> e todos os descendentes visuais. Necessário porque o WPF só transmite
    /// <c>Loaded</c> às subárvores que têm manipuladores de instância: o manipulador de classe sozinho deixa
    /// elementos sem tradução (menus, conteúdo de abas, diálogos).
    /// </summary>
    public static void TranslateTree(DependencyObject root)
    {
        if (!Translator.IsActive) return;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is FrameworkElement element) Attach(element);
            else if (current is FrameworkContentElement content) Attach(content);
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                var count = VisualTreeHelper.GetChildrenCount(current);
                for (var i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(current, i));
            }
        }
    }

    /// <summary>Traduz os valores de texto de um dicionário de recursos (sys:String).</summary>
    public static void TranslateResources(ResourceDictionary dictionary)
    {
        if (!Translator.IsActive) return;
        foreach (var key in dictionary.Keys.Cast<object>().ToList())
            if (dictionary[key] is string s) dictionary[key] = Translator.T(s);
        foreach (var merged in dictionary.MergedDictionaries) TranslateResources(merged);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || (bool)element.GetValue(AttachedProperty)) return;
        TranslateTree(element);
    }

    private static void Attach(FrameworkContentElement element)
    {
        if (element is not TextElement || (bool)element.GetValue(AttachedProperty)) return;
        element.SetValue(AttachedProperty, true);
        if (element is Run run && !string.IsNullOrEmpty(run.Text) && run.Text.Trim().Length > 1)
        {
            var t = Translator.T(run.Text);
            if (t != run.Text) run.SetCurrentValue(Run.TextProperty, t);
        }
    }

    private static void Attach(FrameworkElement element)
    {
        if ((bool)element.GetValue(AttachedProperty)) return;
        element.SetValue(AttachedProperty, true);
        var textBefore = (element as TextBlock)?.Text;
        foreach (var dp in Properties)
        {
            if (!Applies(element, dp)) continue;
            Translate(element, dp);
            DependencyPropertyDescriptor.FromProperty(dp, element.GetType())?.AddValueChanged(element, OnChanged);
        }
        // Se o Text já foi traduzido, os Runs têm o texto traduzido: traduzir de novo estragaria ("D{0}" → "R{0}").
        if (element is TextBlock tb && tb.Text == textBefore) TranslateInlines(tb.Inlines);
        if (element is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string h) column.Header = Translator.T(h);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !(bool)element.GetValue(AttachedProperty)) return;
        element.SetValue(AttachedProperty, false);
        foreach (var dp in Properties)
            if (Applies(element, dp))
                DependencyPropertyDescriptor.FromProperty(dp, element.GetType())?.RemoveValueChanged(element, OnChanged);
    }

    private static bool Applies(FrameworkElement element, DependencyProperty dp) =>
        dp.OwnerType.IsInstanceOfType(element) || dp == FrameworkElement.ToolTipProperty
        || dp == AutomationProperties.NameProperty || dp == AutomationProperties.HelpTextProperty;

    private static void OnChanged(object? sender, EventArgs e)
    {
        if (_busy || sender is not FrameworkElement element) return;
        foreach (var dp in Properties)
            if (Applies(element, dp)) Translate(element, dp);
    }

    private static void Translate(DependencyObject d, DependencyProperty dp)
    {
        if (d.GetValue(dp) is not string s || s.Length == 0) return;
        var t = Translator.T(s);
        if (ReferenceEquals(t, s) || t == s) return;
        _busy = true;
        try { d.SetCurrentValue(dp, t); }
        finally { _busy = false; }
    }

    private static void TranslateInlines(InlineCollection inlines)
    {
        // Cópia: mudar o texto de um Run pode recriar a coleção (TextBlock com Text).
        foreach (var inline in inlines.ToList())
        {
            if (inline is Run run && !string.IsNullOrEmpty(run.Text) && run.Text.Trim().Length > 1)
            {
                var t = Translator.T(run.Text);
                if (t != run.Text) run.SetCurrentValue(Run.TextProperty, t);
            }
            else if (inline is Span span) TranslateInlines(span.Inlines);
        }
    }
}
