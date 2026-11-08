using MeuEditorDeVideo.Application.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMeuEditorApplication(this IServiceCollection services)
    {
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<AutosaveScheduler>();
        return services;
    }
}
