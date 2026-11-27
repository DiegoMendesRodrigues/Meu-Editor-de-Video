// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Janela do gerenciador de modelos de IA.</summary>
public partial class ModelManagerWindow : Window
{
    public ModelManagerWindow() { InitializeComponent(); }

    public ModelManagerWindow(ModelManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // "GPU (DirectML)" no Windows, "GPU (Core ML)" no macOS (Apple Silicon); sem provedor de GPU (Linux), a opção fica
        // desabilitada e os modelos rodam na CPU.
        OpcaoGpu.Content = Translator.T(AiPlatform.GpuLabel());
        OpcaoGpu.IsEnabled = AiPlatform.GpuAvailable;
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>Cria o ViewModel da ferramenta de download (os testes trocam por um com serviços falsos).</summary>
    public Func<ModelCatalogViewModel> CatalogFactory { get; set; } =
        () => ActivatorUtilities.CreateInstance<ModelCatalogViewModel>(App.Services);

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Ferramenta de download e instalação dos modelos do catálogo.</summary>
    private void BaixarModelos_Click(object? sender, RoutedEventArgs e) =>
        DialogShow.Modal(new ModelCatalogWindow(CatalogFactory()), this);
}
