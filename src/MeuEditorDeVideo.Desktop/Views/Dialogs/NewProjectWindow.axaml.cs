// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

public partial class NewProjectWindow : Window
{
    private readonly NewProjectViewModel? _vm;
    private bool _syncing;

    public NewProjectWindow() { InitializeComponent(); }

    public NewProjectWindow(NewProjectViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ViewModel = vm;
        vm.CloseRequested += OnCloseRequested;
        DataContext = vm;
        BuildTemplateList(vm);
        Opened += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public NewProjectViewModel? ViewModel { get; }

    private void OnCloseRequested(object? sender, bool result) => Close(result);

    /// <summary>
    /// O ComboBox do Avalonia não agrupa: os modelos entram em ordem, cada grupo com um cabeçalho desabilitado (não
    /// selecionável) antes dele. A escolha é sincronizada com <see cref="NewProjectViewModel.SelectedTemplate"/> à mão.
    /// </summary>
    private void BuildTemplateList(NewProjectViewModel vm)
    {
        var row = (IDataTemplate)Resources["ModeloLinha"]!;
        ModeloCombo.ItemTemplate = row;
        string? group = null;
        var items = new List<ComboBoxItem>();
        foreach (var template in vm.Templates)
        {
            if (template.Group != group)
            {
                group = template.Group;
                items.Add(new ComboBoxItem
                {
                    Content = new TextBlock
                    {
                        Text = Translator.T(group),
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 6, 0, 0),
                        [!TextBlock.ForegroundProperty] = this.GetResourceObservable("Med.Accent").ToBinding(),
                    },
                    IsEnabled = false,
                    Focusable = false,
                });
            }
            items.Add(new ComboBoxItem { Content = template, ContentTemplate = row, Tag = template });
        }
        ModeloCombo.ItemsSource = items;
        SelectTemplate(vm.SelectedTemplate);
        ModeloCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (ModeloCombo.SelectedItem is ComboBoxItem { Tag: ProjectTemplate t }) vm.SelectedTemplate = t;
        };
        vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewProjectViewModel.SelectedTemplate) && _vm is not null) SelectTemplate(_vm.SelectedTemplate);
    }

    private void SelectTemplate(ProjectTemplate template)
    {
        _syncing = true;
        try
        {
            ModeloCombo.SelectedItem = ModeloCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, template));
        }
        finally
        {
            _syncing = false;
        }
    }
}
