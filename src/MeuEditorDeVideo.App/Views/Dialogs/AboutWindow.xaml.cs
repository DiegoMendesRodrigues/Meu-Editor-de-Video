using System.Windows;
using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Janela "Sobre" (RF215).</summary>
public partial class AboutWindow
{
    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public static void ShowFor(Window? owner)
    {
        var models = App.Services.GetService(typeof(IModelRegistry)) as IModelRegistry;
        var window = new AboutWindow(new AboutViewModel(models)) { Owner = owner };
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
