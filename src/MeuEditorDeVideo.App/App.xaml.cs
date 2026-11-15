using System.Globalization;
using System.Windows;
using System.Windows.Markup;
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

    /// <summary>Argumento da nova instância ao reiniciar pelo idioma: espera o processo anterior terminar.</summary>
    private const string WaitArgument = "--aguardar-processo=";

    protected override async void OnStartup(StartupEventArgs e)
    {
        WaitForPreviousInstance(e.Args);
        var paths = new AppPaths();
        ImageEngine.Text.FontCatalog.UserFontsFolder = paths.Fonts;
        // Preferências do registro lidas antes do host: o JsonSettingsService completa os valores que faltam, e a janela de
        // boas-vindas precisa saber o que o usuário ainda não preencheu.
        var preferenceStore = new Infrastructure.Settings.RegistryPreferencesStore();
        var preferences = preferenceStore.Read();
        // Os argumentos não vão para o host: o único argumento esperado é o caminho de um .med (associação de arquivos).
        _host = Host.CreateDefaultBuilder()
            .UseMeuEditorInfrastructure(paths, preferenceStore)
            .ConfigureServices(ConfigureServices)
            .Build();

        RegisterGlobalExceptionHandlers();

        await _host.StartAsync();

        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Meu Editor de Vídeo {Version} iniciado.", AppInfo.Version);

        // Idioma (Configurações › Geral, registro "Language"): cultura e traduções antes de criar as janelas.
        var language = Services.GetRequiredService<ISettingsService>().Current.Language;
        if (!SupportedLanguages.IsSupported(language)) language = SupportedLanguages.FromCulture(CultureInfo.CurrentUICulture.Name);
        ApplyCulture(language);
        Localization.Translator.Load(language, System.IO.Path.Combine(AppContext.BaseDirectory, "i18n"));
        Localization.LocalizationHook.TranslateResources(Resources);
        Localization.LocalizationHook.Install();

        Services.GetRequiredService<IThemeService>().Apply(Services.GetRequiredService<ISettingsService>().Current.Theme);

        // Boas-vindas: preferências ausentes ou vazias no registro. Se o idioma escolhido for outro, reinicia nele.
        if (!preferences.IsComplete && !await AskPreferencesAsync(preferences, preferenceStore, language))
            return;

        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        // Sem tela inicial: o editor abre com um projeto novo vazio; depois, se for o caso, o projeto pedido, a recuperação
        // de um fechamento inesperado ou o último projeto.
        Services.GetRequiredService<IProjectService>().CreateUntitled(Localization.Translator.T("Sem título"));
        Services.GetRequiredService<INavigationService>().NavigateTo<EditorViewModel>();
        window.Show();

        Services.GetRequiredService<AutosaveScheduler>().Start();
        await Services.GetRequiredService<IRenderQueueService>().InitializeAsync();
        StartBackgroundMaintenance(logger);
        if (StartupArguments.ProjectPath(e.Args) is { } project)
            await OpenProjectAsync(project, logger);
        else if (!await OfferRecoveryAsync())
            await OpenLastProjectAsync(logger);
        // Verificação mensal de atualizações (registro CheckForNewVersions e LastCheck).
        await Services.GetRequiredService<UpdateCheckService>().CheckOnStartupAsync();

        base.OnStartup(e);
    }

    private bool _restartRequested;

    /// <summary>
    /// Reinicia o programa para aplicar o idioma salvo em Configurações › Geral. Fecha a janela pelo caminho normal
    /// (pergunta sobre alterações não salvas; se o usuário cancelar, nada muda) e abre de novo o projeto salvo.
    /// </summary>
    public void RestartForLanguage()
    {
        if (MainWindow is not Views.MainWindow window) return;
        _restartRequested = true;
        void Canceled(object? sender, EventArgs e)
        {
            _restartRequested = false;
            window.CloseCanceled -= Canceled;
        }
        window.CloseCanceled += Canceled;
        window.Close();
    }

    /// <summary>
    /// Mostra a janela de boas-vindas, grava as cinco preferências no registro (Name, Language, ThemeColor,
    /// CheckForNewVersions e LastCheck) e aplica o tema. Devolve false quando o programa vai reiniciar no idioma escolhido.
    /// </summary>
    private async Task<bool> AskPreferencesAsync(UserPreferences current, IUserPreferencesStore store, string language)
    {
        // Sem janela principal ainda: a de boas-vindas não pode virar a principal e encerrar o programa ao fechar.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var welcome = new Views.Dialogs.WelcomeWindow(current);
        welcome.ShowDialog();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var chosen = welcome.Result;

        store.WriteName(chosen.Name!);
        store.WriteLanguage(chosen.Language!);
        store.WriteThemeColor(chosen.ThemeColor!);
        store.WriteCheckForNewVersions(chosen.CheckForNewVersions!.Value);
        store.WriteLastCheck(chosen.LastCheck!.Value);

        var settings = Services.GetRequiredService<ISettingsService>();
        var theme = Enum.Parse<AppTheme>(chosen.ThemeColor!);
        await settings.SaveAsync(settings.Current with
        {
            UserName = chosen.Name!,
            Language = chosen.Language!,
            Theme = theme,
            CheckForNewVersions = chosen.CheckForNewVersions.Value,
            LastUpdateCheck = chosen.LastCheck,
        });

        if (!string.Equals(chosen.Language, language, StringComparison.OrdinalIgnoreCase) && Environment.ProcessPath is { } exe)
        {
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add(WaitArgument + Environment.ProcessId);
            System.Diagnostics.Process.Start(start);
            Shutdown();
            return false;
        }
        Services.GetRequiredService<IThemeService>().Apply(theme);
        return true;
    }

    /// <summary>Ao reiniciar, a instância anterior ainda pode estar fechando o projeto e o autosave: espera até 15 s.</summary>
    private static void WaitForPreviousInstance(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(WaitArgument, StringComparison.Ordinal));
        if (arg is null || !int.TryParse(arg[WaitArgument.Length..], out var pid)) return;
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(pid);
            previous.WaitForExit(15_000);
        }
        catch (ArgumentException)
        {
            // Já terminou.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Antes de qualquer await: OnExit é async void e o processo termina no primeiro await.
        if (_restartRequested && Environment.ProcessPath is { } exe)
        {
            var reopen = Services.GetRequiredService<IProjectService>().Current?.FilePath;
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add(WaitArgument + Environment.ProcessId);
            if (reopen is not null && System.IO.File.Exists(reopen)) start.ArgumentList.Add(reopen);
            System.Diagnostics.Process.Start(start);
        }

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

    /// <summary>
    /// Fechamento inesperado (RF011): oferece recuperar o salvamento automático mais recente ou descartá-lo.
    /// Retorna true se recuperou.
    /// </summary>
    private static async Task<bool> OfferRecoveryAsync()
    {
        var projects = Services.GetRequiredService<IProjectService>();
        if (projects.FindRecoverable().FirstOrDefault() is not { } r) return false;
        if (!Services.GetRequiredService<IDialogService>().Confirm("Recuperar projeto",
                $"O projeto \"{r.Name}\" não foi fechado corretamente. Há um salvamento automático de {r.SavedAt.LocalDateTime:g}.\n\n" +
                "Deseja recuperá-lo agora? Se escolher não, a cópia continua guardada e é oferecida de novo na próxima vez."))
            return false;
        return await Services.GetRequiredService<IProjectWorkflow>().RecoverAsync(r);
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
        // Assistente virtual: a base de conhecimento são os manuais instalados na pasta "ajuda" do aplicativo.
        services.AddSingleton<Core.Help.IHelpKnowledgeBase>(_ =>
            new MeuEditorDeVideo.Application.Help.ManualKnowledgeBase(System.IO.Path.Combine(AppContext.BaseDirectory, "ajuda")));

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<IProjectWorkflow, ProjectWorkflow>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<MissingMediaViewModel>();
        services.AddTransient<ViewModels.Media.MediaLibraryViewModel>();

        services.AddSingleton<ShellViewModel>();
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

    private static void ApplyCulture(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
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
