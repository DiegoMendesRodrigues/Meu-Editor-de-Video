using MeuEditorDeVideo.Core.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.ProjectEngine;

public static class ProjectEngineServiceCollectionExtensions
{
    public static IServiceCollection AddProjectEngine(this IServiceCollection services)
    {
        services.AddSingleton<IProjectStore, MedProjectStore>();
        services.AddSingleton<IMediaLocator, MediaLocator>();
        services.AddSingleton<IAutosaveStore, AutosaveStore>();
        return services;
    }
}
