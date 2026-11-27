// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Janela "Sobre" (RF215).</summary>
public partial class AboutWindow : Window
{
    public AboutWindow() : this(new AboutViewModel()) { }

    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public static void ShowFor(Window? owner)
    {
        var models = App.Services.GetService(typeof(IModelRegistry)) as IModelRegistry;
        var window = new AboutWindow(new AboutViewModel(models));
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DialogShow.Modal(window, owner);
    }

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();
}
