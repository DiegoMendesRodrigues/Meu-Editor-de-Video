using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela simples para pedir um texto (renomear, categoria…).</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _input;

    public PromptWindow(string title, string label, string initialValue)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.Background");

        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");

        _input = new TextBox { Text = initialValue };
        AutomationProperties.SetName(_input, label);

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Med.AccentButton");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(text);
        root.Children.Add(_input);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    public string Value => _input.Text;
}
