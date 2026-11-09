using System.Windows;
using MeuEditorDeVideo.App.ViewModels.Export;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Fila de renderização: janela não modal (segundo plano) ou modal acompanhando uma exportação.</summary>
public partial class RenderQueueWindow : Window
{
    public RenderQueueWindow(RenderQueueViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Closed += (_, _) => vm.Dispose();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
