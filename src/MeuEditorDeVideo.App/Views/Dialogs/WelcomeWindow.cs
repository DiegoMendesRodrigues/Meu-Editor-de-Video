using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MeuEditorDeVideo.App.Localization;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>
/// Boas-vindas: aparece ao abrir quando as preferências do registro faltam ou estão vazias
/// (<see cref="UserPreferences.IsComplete"/>). Todos os campos são obrigatórios e a janela só fecha por
/// "Salvar as preferências".
/// </summary>
public sealed class WelcomeWindow : Window
{
    private readonly TextBox _name;
    private readonly ComboBox _language;
    private readonly ComboBox _theme;
    private readonly ComboBox _check;
    private readonly Button _save;
    private bool _saved;

    /// <summary>Idiomas na ordem pedida, pelo nome em português (traduzido para o idioma da interface).</summary>
    internal static readonly IReadOnlyList<(string Label, string Code)> Languages =
    [
        ("Alemão", "de"), ("Espanhol", "es"), ("Francês", "fr"), ("Inglês", "en"),
        ("Português (Brasil)", "pt-BR"), ("Português (Portugal)", "pt-PT"),
    ];

    internal static readonly IReadOnlyList<(string Label, string Value)> Themes =
        [("Claro", "Light"), ("Escuro", "Dark"), ("Acompanhar o sistema operacional", "System")];

    public WelcomeWindow(UserPreferences current)
    {
        var title = Translator.T("Bem-vindo ao Meu Editor de Vídeos");
        Title = title;
        AutomationProperties.SetName(this, title);
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Med.WindowBackground");

        var intro = new TextBlock
        {
            Text = Translator.T("Para que você possa utilizar melhor a solução, será necessário que me ajude, preenchendo as suas preferências nos espaços abaixo."),
            TextWrapping = TextWrapping.Wrap,
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");

        _name = new TextBox { Text = current.Name?.Trim() ?? "" };
        // Valores do registro quando existem; senão, os padrões: idioma do Windows, tema Claro e verificação Sim.
        var language = SupportedLanguages.IsSupported(current.Language)
            ? current.Language
            : SupportedLanguages.FromCulture(System.Globalization.CultureInfo.InstalledUICulture.Name);
        var theme = UserPreferences.ThemeColors.Contains(current.ThemeColor) ? current.ThemeColor : "Light";
        _language = Combo(Languages.Select(l => new Choice<string>(l.Label, l.Code)), language);
        _theme = Combo(Themes.Select(t => new Choice<string>(t.Label, t.Value)), theme);
        _check = Combo([new Choice<string>("Sim", "1"), new Choice<string>("Não", "0")],
            current.CheckForNewVersions == false ? "0" : "1");

        var form = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(form, 0, "Nome Completo", _name);
        AddRow(form, 1, "Idioma do sistema", _language);
        AddRow(form, 2, "Tema", _theme);
        AddRow(form, 3, "Verificar mensalmente pelas novas atualizações", _check);

        var saveText = Translator.T("Salvar as preferências");
        _save = new Button { Content = saveText, IsDefault = true, MinWidth = 180 };
        _save.SetResourceReference(StyleProperty, "Med.AccentButton");
        AutomationProperties.SetName(_save, saveText);
        _save.Click += (_, _) => Save();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_save);

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new Border { Height = 8 }); // espaço vertical
        root.Children.Add(intro);
        root.Children.Add(form);
        root.Children.Add(new Border { Height = 24 }); // espaço vertical
        root.Children.Add(buttons);
        Content = root;

        _name.TextChanged += (_, _) => UpdateSave();
        foreach (var combo in new[] { _language, _theme, _check }) combo.SelectionChanged += (_, _) => UpdateSave();
        UpdateSave();
        Loaded += (_, _) => _name.Focus();
    }

    /// <summary>Preferências escolhidas (válidas depois de salvar).</summary>
    public UserPreferences Result { get; private set; } = new(null, null, null, null, null);

    private static ComboBox Combo(IEnumerable<Choice<string>> items, string? selected)
    {
        var list = items.ToList();
        return new ComboBox
        {
            ItemsSource = list,
            DisplayMemberPath = nameof(Choice<string>.Label),
            SelectedItem = list.FirstOrDefault(c => string.Equals(c.Value, selected, StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static void AddRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = Translator.T(label);
        var caption = new TextBlock { Text = text + " *", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 16, 6) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Med.Text");
        Grid.SetRow(caption, row);
        field.Margin = new Thickness(0, 6, 0, 6);
        AutomationProperties.SetName(field, text);
        AutomationProperties.SetIsRequiredForForm(field, true);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(caption);
        grid.Children.Add(field);
    }

    private bool IsFilled =>
        _name.Text.Trim().Length > 0 && _language.SelectedItem is not null && _theme.SelectedItem is not null && _check.SelectedItem is not null;

    private void UpdateSave() => _save.IsEnabled = IsFilled;

    private void Save()
    {
        if (!IsFilled) return;
        Result = new UserPreferences(_name.Text.Trim(), ((Choice<string>)_language.SelectedItem).Value,
            ((Choice<string>)_theme.SelectedItem).Value, ((Choice<string>)_check.SelectedItem).Value == "1", DateTimeOffset.Now);
        _saved = true;
        DialogResult = true;
    }

    /// <summary>Não fecha sem salvar (X, Alt+F4, Esc).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_saved) e.Cancel = true;
        base.OnClosing(e);
    }
}
