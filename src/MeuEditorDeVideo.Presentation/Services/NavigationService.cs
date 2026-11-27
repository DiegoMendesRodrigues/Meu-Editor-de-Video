// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Presentation.Services;

public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : PageViewModel;
}

/// <summary>Troca a página exibida na janela principal. As Views são resolvidas por DataTemplate (Views/ViewMappings.xaml).</summary>
public sealed class NavigationService(IServiceProvider services, ShellViewModel shell) : INavigationService
{
    public void NavigateTo<TViewModel>() where TViewModel : PageViewModel
    {
        var previous = shell.CurrentPage;
        var next = services.GetRequiredService<TViewModel>();
        shell.CurrentPage = next;
        previous?.OnNavigatedFrom();
        next.OnNavigatedTo();
    }
}
