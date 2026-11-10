using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MeuEditorDeVideo.App.Services;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Pergunta se o projeto sai só da lista de recentes ou se o arquivo também deve ser excluído.</summary>
public sealed class RecentRemovalWindow : Window
{
    public RecentRemovalChoice Choice { get; private set; } = RecentRemovalChoice.Cancel;

    public RecentRemovalWindow(string projectName, string path, bool fileExists)
    {
        Title = "Remover projeto recente";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Med.Background");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Text($"O que deseja fazer com o projeto \"{projectName}\"?", "Med.Text", bold: true));
        root.Children.Add(Text(path, "Med.TextMuted", margin: new Thickness(0, 2, 0, 12)));
        root.Children.Add(Text("• Remover da lista: o projeto sai de \"Projetos recentes\", mas o arquivo continua no disco.", "Med.Text"));
        root.Children.Add(Text(fileExists
                ? "• Excluir também o arquivo: o arquivo do projeto (.med) vai para a Lixeira do Windows. As mídias originais usadas no projeto não são apagadas."
                : "• O arquivo do projeto não foi encontrado; só é possível removê-lo da lista.",
            "Med.Text", margin: new Thickness(0, 4, 0, 0)));

        var remove = Button("Remover da lista", RecentRemovalChoice.RemoveFromList);
        remove.IsDefault = true;
        remove.SetResourceReference(StyleProperty, "Med.AccentButton");
        var delete = Button("Excluir também o arquivo", RecentRemovalChoice.DeleteFiles);
        delete.IsEnabled = fileExists;
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 96, Padding = new Thickness(12, 4, 12, 4) };
        AutomationProperties.SetName(cancel, "Cancelar");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(remove);
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) => remove.Focus();
    }

    private Button Button(string label, RecentRemovalChoice choice)
    {
        var b = new Button { Content = label, MinWidth = 96, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(b, label);
        b.Click += (_, _) =>
        {
            Choice = choice;
            DialogResult = true;
        };
        return b;
    }

    private static TextBlock Text(string text, string brush, bool bold = false, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
