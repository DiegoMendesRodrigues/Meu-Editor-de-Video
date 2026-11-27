// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Presentation.ViewModels.Export;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Fila de renderização: janela não modal (segundo plano) ou modal acompanhando uma exportação.</summary>
public partial class RenderQueueWindow : Window
{
    public RenderQueueWindow() { InitializeComponent(); }

    public RenderQueueWindow(RenderQueueViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        ViewModel = vm;
        Closed += (_, _) => vm.Dispose();
    }

    public RenderQueueViewModel? ViewModel { get; }

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();
}
