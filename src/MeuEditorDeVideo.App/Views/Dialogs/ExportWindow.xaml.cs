using System.Windows;
using MeuEditorDeVideo.App.ViewModels.Export;

namespace MeuEditorDeVideo.App.Views.Dialogs;

public partial class ExportWindow : Window
{
    public ExportWindow(ExportViewModel vm)
    {
        InitializeComponent();
        vm.CloseRequested += (_, result) => DialogResult = result;
        DataContext = vm;
        Loaded += (_, _) => NomeArquivo.Focus();
    }
}
