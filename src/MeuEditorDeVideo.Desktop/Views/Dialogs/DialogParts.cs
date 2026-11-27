// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Peças comuns das janelas montadas em código: fundo do tema, rótulos e botões de ação.</summary>
internal static class DialogParts
{
    /// <summary>Configura a janela como diálogo fixo de largura <paramref name="width"/> e altura do conteúdo.</summary>
    public static void Setup(Window window, string title, double width)
    {
        window.Title = title;
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.CanResize = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window[!TemplatedControl.BackgroundProperty] = window.GetResourceObservable("Med.WindowBackground").ToBinding();
    }

    /// <summary>Texto com a cor principal do tema.</summary>
    public static TextBlock Label(Control scope, string text, double top = 0, double bottom = 4)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, bottom) };
        t[!TextBlock.ForegroundProperty] = scope.GetResourceObservable("Med.Text").ToBinding();
        return t;
    }

    /// <summary>Botão de ação principal (em destaque).</summary>
    public static Button Accent(string text, double minWidth, bool isDefault = true)
    {
        var b = new Button { Content = text, IsDefault = isDefault, MinWidth = minWidth, Margin = new Thickness(0, 0, 8, 0) };
        b.Classes.Add("med-accent");
        AutomationProperties.SetName(b, text);
        return b;
    }

    /// <summary>Linha de botões alinhada à direita.</summary>
    public static StackPanel Buttons(params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }
}
