using System.IO;
using MeuEditorDeVideo.AIEngine;
using MeuEditorDeVideo.Application;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.ProjectEngine;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Presentation.Hosting;

/// <summary>
/// Composição comum aos aplicativos de interface (WPF e Avalonia): motores, casos de uso, view models e serviços que
/// não dependem do toolkit. Cada aplicativo acrescenta só o que é dele: <see cref="IDialogService"/>,
/// <see cref="IThemeService"/> e as janelas.
/// </summary>
public static class EditorComposition
{
    public static IServiceCollection AddMeuEditor(this IServiceCollection services)
    {
        services.AddProjectEngine();
        services.AddMediaEngine();
        services.AddPreviewEngine();
        services.AddMeuEditorApplication();
        services.AddAiEngine();
        // Instalador de modelos a partir do catálogo oficial: o download (única parte com rede) só roda quando o usuário pede.
        ModelDownloads.ModelDownloadsServiceCollectionExtensions.AddModelDownloads(services, AIEngine.Models.ModelCatalog.AllowedHosts);
        services.AddSingleton(sp => new AIEngine.Models.CatalogModelInstaller(
            AIEngine.Models.ModelCatalog.Load(), sp.GetRequiredService<Core.AI.IModelRegistry>(), sp.GetRequiredService<Core.AI.IModelDownloader>(),
            Path.Combine(sp.GetRequiredService<IAppPaths>().LocalData, "modelos-baixados"),
            sp.GetService<ILogger<AIEngine.Models.CatalogModelInstaller>>()));
        // Assistente virtual: a base de conhecimento são os manuais instalados na pasta "ajuda" do aplicativo.
        services.AddSingleton<Core.Help.IHelpKnowledgeBase>(_ =>
            new Application.Help.ManualKnowledgeBase(Path.Combine(AppContext.BaseDirectory, "ajuda")));

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<IProjectWorkflow, ProjectWorkflow>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<MissingMediaViewModel>();
        services.AddTransient<ViewModels.Media.MediaLibraryViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<EditorViewModel>();

        // F11: configurações e desempenho.
        services.AddSingleton<ShortcutService>();
        services.AddSingleton<ICacheMaintenance>(sp => new Infrastructure.Cache.CacheMaintenance(
            sp.GetRequiredService<IAppPaths>(), sp.GetService<ILogger<Infrastructure.Cache.CacheMaintenance>>()));
        services.AddSingleton<IHardwareInfoService>(sp =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            var settings = sp.GetRequiredService<ISettingsService>().Current;
            var projects = settings.DefaultProjectsDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Meu Editor de Vídeo");
            return new Infrastructure.Hardware.HardwareDetector(
                [("Cache", paths.Cache), ("Projetos", projects)],
                MediaEngine.Export.EncoderCatalog.HardwareSummary,
                MediaEngine.Native.HardwareProbe.D3D11VideoDecoding,
                sp.GetService<ILogger<Infrastructure.Hardware.HardwareDetector>>());
        });
        return services;
    }
}
