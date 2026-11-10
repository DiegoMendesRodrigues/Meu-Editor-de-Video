using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.AIEngine;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.ProjectEngine;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.App.Views;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Infrastructure.Hosting;
using MeuEditorDeVideo.Infrastructure.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ApplyPortugueseCulture();

        var paths = new AppPaths();
        ImageEngine.Text.FontCatalog.UserFontsFolder = paths.Fonts;
        // Os argumentos não vão para o host: o único argumento esperado é o caminho de um .med (associação de arquivos).
        _host = Host.CreateDefaultBuilder()
            .UseMeuEditorInfrastructure(paths)
            .ConfigureServices(ConfigureServices)
            .Build();

        RegisterGlobalExceptionHandlers();

        await _host.StartAsync();

        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Meu Editor de Vídeo {Version} iniciado.", AppInfo.Version);

        Services.GetRequiredService<IThemeService>().Apply(Services.GetRequiredService<ISettingsService>().Current.Theme);

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        Services.GetRequiredService<INavigationService>().NavigateTo<HomeViewModel>();
        window.Show();

        Services.GetRequiredService<AutosaveScheduler>().Start();
        await Services.GetRequiredService<IRenderQueueService>().InitializeAsync();
        StartBackgroundMaintenance(logger);
        if (StartupArguments.ProjectPath(e.Args) is { } project)
            await OpenProjectAsync(project, logger);
        else
            await OpenLastProjectAsync(logger);

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await Services.GetRequiredService<AutosaveScheduler>().DisposeAsync();
            await Services.GetRequiredService<IProjectService>().CloseAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Em segundo plano: detecta o hardware (§12.3, inclui testar os codificadores) e aplica o limite do cache em disco
    /// (RF209). Nada disso bloqueia a interface.
    /// </summary>
    private static void StartBackgroundMaintenance(ILogger logger)
    {
        var hardware = Services.GetRequiredService<IHardwareInfoService>();
        var cache = Services.GetRequiredService<ICacheMaintenance>();
        var limit = (long)Math.Max(1, Services.GetRequiredService<ISettingsService>().Current.PreviewCacheGigabytes) << 30;
        _ = Task.Run(() =>
        {
            try
            {
                hardware.Get();
                var freed = cache.Trim(limit);
                if (freed > 0) logger.LogInformation("Cache em disco reduzido ao limite: {Bytes} bytes liberados.", freed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha na manutenção em segundo plano.");
            }
        });
    }

    /// <summary>"Abrir o último projeto ao iniciar" (configurações › Geral).</summary>
    private static async Task OpenLastProjectAsync(ILogger logger)
    {
        var settings = Services.GetRequiredService<ISettingsService>().Current;
        if (!settings.OpenLastProjectOnStartup || settings.RecentProjects.FirstOrDefault() is not { } last || !System.IO.File.Exists(last)) return;
        await OpenProjectAsync(last, logger);
    }

    /// <summary>Abre um projeto na inicialização (arquivo .med aberto pelo Windows ou o último projeto).</summary>
    private static async Task OpenProjectAsync(string path, ILogger logger)
    {
        try
        {
            await Services.GetRequiredService<IProjectWorkflow>().OpenAsync(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível abrir o projeto {Caminho} na inicialização.", path);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
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
            System.IO.Path.Combine(sp.GetRequiredService<IAppPaths>().LocalData, "modelos-baixados"),
            sp.GetService<ILogger<AIEngine.Models.CatalogModelInstaller>>()));

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IProjectWorkflow, ProjectWorkflow>();
        services.AddSingleton<IFileRecycler, RecycleBinFileRecycler>();
        services.AddTransient<RecentProjectRemoval>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<MissingMediaViewModel>();
        services.AddTransient<ViewModels.Media.MediaLibraryViewModel>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<EditorViewModel>();

        services.AddSingleton<MainWindow>();

        // F11: configurações e desempenho.
        services.AddSingleton<ShortcutService>();
        services.AddSingleton<ICacheMaintenance>(sp => new Infrastructure.Cache.CacheMaintenance(
            sp.GetRequiredService<IAppPaths>(), sp.GetService<ILogger<Infrastructure.Cache.CacheMaintenance>>()));
        services.AddSingleton<IHardwareInfoService>(sp =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            var settings = sp.GetRequiredService<ISettingsService>().Current;
            var projects = settings.DefaultProjectsDirectory
                ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Meu Editor de Vídeo");
            return new Infrastructure.Hardware.HardwareDetector(
                [("Cache", paths.Cache), ("Projetos", projects)],
                MediaEngine.Export.EncoderCatalog.HardwareSummary,
                MediaEngine.Native.HardwareProbe.D3D11VideoDecoding,
                sp.GetService<ILogger<Infrastructure.Hardware.HardwareDetector>>());
        });
    }

    private static void ApplyPortugueseCulture()
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    /// <summary>Tratamento centralizado de exceções (RNF010): registra no log e informa o usuário em pt-BR.</summary>
    private void RegisterGlobalExceptionHandlers()
    {
        var logger = Services.GetRequiredService<ILogger<App>>();

        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Exceção não tratada na interface.");
            ShowError(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogCritical(args.ExceptionObject as Exception, "Exceção fatal não tratada (encerrando: {Terminating}).", args.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Exceção não observada em tarefa assíncrona.");
            args.SetObserved();
        };
    }

    private static void ShowError(Exception ex)
    {
        var error = ex is MedException med ? med.Error : MedError.Unexpected(ex);
        Services.GetRequiredService<IDialogService>().ShowError(error);
    }
}
