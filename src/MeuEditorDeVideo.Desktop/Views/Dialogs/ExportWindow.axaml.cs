// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using MeuEditorDeVideo.Presentation.ViewModels.Export;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Exportação (§5.4): fechar com true inicia a exportação; false cancela.</summary>
public partial class ExportWindow : Window
{
    public ExportWindow() { InitializeComponent(); }

    public ExportWindow(ExportViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        vm.CloseRequested += (_, result) => Close(result);
        DataContext = vm;
        Opened += (_, _) => NomeArquivo.Focus();
    }

    public ExportViewModel? ViewModel { get; }
}
