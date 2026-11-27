// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>
/// Boas-vindas: aparece ao abrir quando as preferências do registro faltam ou estão vazias
/// (<see cref="UserPreferences.IsComplete"/>). Todos os campos são obrigatórios e a janela só fecha por
/// "Salvar as preferências". É mostrada com Show() (sem janela principal ainda); quem chamou espera o evento Closed.
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
        DialogParts.Setup(this, title, 560);
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetName(this, title);

        var intro = DialogParts.Label(this, Translator.T("Para que você possa utilizar melhor a solução, será necessário que me ajude, preenchendo as suas preferências nos espaços abaixo."), 0, 0);

        _name = new TextBox { Text = current.Name?.Trim() ?? "" };
        // Valores do registro quando existem; senão, os padrões: idioma do sistema, tema Claro e verificação Sim.
        var language = SupportedLanguages.IsSupported(current.Language)
            ? current.Language
            : SupportedLanguages.FromCulture(System.Globalization.CultureInfo.InstalledUICulture.Name);
        var theme = UserPreferences.ThemeColors.Contains(current.ThemeColor) ? current.ThemeColor : "Light";
        _language = Combo(Languages.Select(l => new Choice<string>(l.Label, l.Code)), language);
        _theme = Combo(Themes.Select(t => new Choice<string>(t.Label, t.Value)), theme);
        _check = Combo([new Choice<string>("Sim", "1"), new Choice<string>("Não", "0")],
            current.CheckForNewVersions == false ? "0" : "1");

        var form = new Grid { Margin = new Thickness(0, 20, 0, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        AddRow(form, 0, "Nome Completo", _name);
        AddRow(form, 1, "Idioma do sistema", _language);
        AddRow(form, 2, "Tema", _theme);
        AddRow(form, 3, "Verificar mensalmente pelas novas atualizações", _check);

        var saveText = Translator.T("Salvar as preferências");
        _save = DialogParts.Accent(saveText, 180);
        _save.Margin = new Thickness(0);
        _save.Click += (_, _) => Save();

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new Border { Height = 8 }); // espaço vertical
        root.Children.Add(intro);
        root.Children.Add(form);
        var buttons = DialogParts.Buttons(_save);
        buttons.Margin = new Thickness(0, 24, 0, 0);
        root.Children.Add(buttons);
        Content = root;

        _name.TextChanged += (_, _) => UpdateSave();
        foreach (var combo in new[] { _language, _theme, _check }) combo.SelectionChanged += (_, _) => UpdateSave();
        UpdateSave();
        Opened += (_, _) => _name.Focus();
    }

    /// <summary>Preferências escolhidas (válidas depois de salvar).</summary>
    public UserPreferences Result { get; private set; } = new(null, null, null, null, null);

    private static ComboBox Combo(IEnumerable<Choice<string>> items, string? selected)
    {
        var list = items.ToList();
        return new ComboBox
        {
            ItemsSource = list,
            ItemTemplate = new FuncDataTemplate<Choice<string>>((c, _) => new TextBlock { Text = c?.Label }),
            SelectedItem = list.FirstOrDefault(c => string.Equals(c.Value, selected, StringComparison.OrdinalIgnoreCase)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private void AddRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var text = Translator.T(label);
        var caption = DialogParts.Label(this, text + " *", 0, 0);
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.Margin = new Thickness(0, 6, 16, 6);
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
        (_name.Text ?? "").Trim().Length > 0 && _language.SelectedItem is not null && _theme.SelectedItem is not null && _check.SelectedItem is not null;

    private void UpdateSave() => _save.IsEnabled = IsFilled;

    private void Save()
    {
        if (!IsFilled) return;
        Result = new UserPreferences((_name.Text ?? "").Trim(), ((Choice<string>)_language.SelectedItem!).Value,
            ((Choice<string>)_theme.SelectedItem!).Value, ((Choice<string>)_check.SelectedItem!).Value == "1", DateTimeOffset.Now);
        _saved = true;
        Close(true);
    }

    /// <summary>Não fecha sem salvar (X, Alt+F4, Esc).</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_saved) e.Cancel = true;
        base.OnClosing(e);
    }
}
