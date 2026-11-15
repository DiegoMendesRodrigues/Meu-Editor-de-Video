using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMeuEditorApplication(this IServiceCollection services)
    {
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<AutosaveScheduler>();
        services.AddSingleton<IMediaImportService, MediaImportService>();
        services.AddSingleton<IRenderQueueService, RenderQueueService>();
        services.AddSingleton<Core.Projects.IContentPackStore, Packs.ContentPackStore>();
        services.AddSingleton<Help.HelpAssistantService>();
        return services;
    }
}
