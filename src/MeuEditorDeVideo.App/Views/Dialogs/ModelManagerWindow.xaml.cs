using System.Windows;
using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela não modal do gerenciador de modelos de IA.</summary>
public partial class ModelManagerWindow
{
    public ModelManagerWindow(ModelManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Ferramenta de download e instalação dos modelos do catálogo.</summary>
    private void BaixarModelos_Click(object sender, RoutedEventArgs e)
    {
        var vm = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ModelCatalogViewModel>(App.Services);
        new ModelCatalogWindow(vm) { Owner = this }.ShowDialog();
    }
}
