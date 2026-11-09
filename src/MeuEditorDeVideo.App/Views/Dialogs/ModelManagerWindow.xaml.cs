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
}
