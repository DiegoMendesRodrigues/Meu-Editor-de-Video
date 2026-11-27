// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Barra de menus nativa do macOS (<c>NativeMenu</c>), montada a partir do <see cref="Menu"/> do editor: os mesmos itens,
/// comandos e atalhos, sem declarar a árvore duas vezes. Os itens que dependem de código (projetos recentes, qualidade)
/// são refeitos quando o menu abre: o item correspondente do <see cref="Menu"/> recebe <c>SubmenuOpened</c> e a cópia
/// nativa é reconstruída em seguida. O <see cref="Menu"/> da janela fica oculto no macOS.
/// </summary>
public static class NativeMenuBuilder
{
    /// <summary>Barra de menus nativa equivalente a <paramref name="menu"/>.</summary>
    public static NativeMenu FromMenu(Menu menu)
    {
        var root = new NativeMenu();
        foreach (var top in menu.Items.OfType<MenuItem>())
            root.Add(Convert(top));
        return root;
    }

    /// <summary>Menu do aplicativo no macOS (o primeiro da barra): Sobre, Configurações… (Cmd+,) e Sair.</summary>
    public static NativeMenu ApplicationMenu(Action about, Action settings, Action quit)
    {
        var menu = new NativeMenu();
        var aboutItem = new NativeMenuItem { Header = Translator.T("Sobre o Meu Editor de Vídeo") };
        aboutItem.Click += (_, _) => about();
        var settingsItem = new NativeMenuItem
        {
            Header = Translator.T("Configurações…"),
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        settingsItem.Click += (_, _) => settings();
        var quitItem = new NativeMenuItem { Header = Translator.T("Sair"), Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta) };
        quitItem.Click += (_, _) => quit();
        menu.Add(aboutItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(settingsItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);
        return menu;
    }

    /// <summary>Texto do cabeçalho sem a marca de tecla de acesso do Windows ("_Arquivo" → "Arquivo", "__" → "_").</summary>
    public static string PlainHeader(object? header)
    {
        var text = header?.ToString() ?? "";
        const string placeholder = "\u0001";
        return text.Replace("__", placeholder, StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal)
            .Replace(placeholder, "_", StringComparison.Ordinal);
    }

    private static NativeMenuItemBase Convert(Control control)
    {
        if (control is not MenuItem item) return new NativeMenuItemSeparator();
        var native = new NativeMenuItem
        {
            Header = PlainHeader(item.Header),
            IsEnabled = item.IsEnabled,
            Gesture = item.InputGesture,
            Command = item.Command,
            CommandParameter = item.CommandParameter,
            ToggleType = item.ToggleType,
            IsChecked = item.IsChecked,
        };
        var children = item.Items.OfType<Control>().ToList();
        if (children.Count > 0 || item.Command is null && item.Items.Count > 0)
        {
            native.Menu = new NativeMenu();
            foreach (var child in children) native.Menu.Add(Convert(child));
            // Itens dinâmicos: o Menu do editor atualiza o próprio submenu e a cópia nativa é refeita.
            native.Menu.Opening += (_, _) =>
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent) { Source = item });
                native.Menu.Items.Clear();
                foreach (var child in item.Items.OfType<Control>()) native.Menu.Add(Convert(child));
            };
        }
        else if (item.Command is null)
        {
            // Item com evento Click (sem comando): repassa o clique ao item do Menu do editor.
            native.Click += (_, _) =>
            {
                if (item.ToggleType == MenuItemToggleType.CheckBox) item.IsChecked = !item.IsChecked;
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent) { Source = item });
            };
        }
        return native;
    }
}
