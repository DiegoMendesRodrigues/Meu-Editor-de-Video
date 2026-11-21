using System.Windows;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Ferramenta "Baixar modelos de IA" (catálogo oficial → pacotes .medmodel → instalação).</summary>
public partial class ModelCatalogWindow
{
    public ModelCatalogWindow(ModelCatalogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, e) =>
        {
            if (!viewModel.IsBusy) return;
            if (MessageBox.Show(this, Presentation.Localization.Translator.T("A instalação está em andamento. Cancelar e fechar?"), Presentation.Localization.Translator.T("Baixar modelos de IA"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                viewModel.CancelCommand.Execute(null);
            else
                e.Cancel = true;
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
