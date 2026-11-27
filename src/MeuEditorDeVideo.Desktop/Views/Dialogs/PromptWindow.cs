// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Janela simples para pedir um texto (renomear, categoria…).</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _input;

    public PromptWindow(string title, string label, string initialValue)
    {
        DialogParts.Setup(this, title, 440);

        _input = new TextBox { Text = initialValue };
        AutomationProperties.SetName(_input, label);

        var ok = DialogParts.Accent("OK", 96);
        ok.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96 };
        cancel.Click += (_, _) => Close(false);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(DialogParts.Label(this, label, 0, 8));
        root.Children.Add(_input);
        root.Children.Add(DialogParts.Buttons(ok, cancel));
        Content = root;

        Opened += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    public string Value => _input.Text ?? "";
}
