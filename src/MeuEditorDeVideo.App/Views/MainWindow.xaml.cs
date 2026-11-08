using System.Windows;
using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
