using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.ProjectEngine;
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

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseMeuEditorInfrastructure(new AppPaths())
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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddProjectEngine();
        services.AddMediaEngine();
        services.AddMeuEditorApplication();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IProjectWorkflow, ProjectWorkflow>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<MissingMediaViewModel>();
        services.AddTransient<ViewModels.Media.MediaLibraryViewModel>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<EditorViewModel>();

        services.AddSingleton<MainWindow>();
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
