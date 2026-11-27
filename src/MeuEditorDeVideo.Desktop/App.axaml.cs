// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Desktop.Views;
using MeuEditorDeVideo.Infrastructure.Hosting;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Presentation.Hosting;
using MeuEditorDeVideo.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Desktop;

public partial class App : Avalonia.Application
{
    private IHost? _host;
    private bool _restartRequested;
    private bool _readyForFiles;
    private string? _fileToOpen;

    /// <summary>Serviços do aplicativo; null antes da inicialização (designer, testes de interface sem host).</summary>
    public static IServiceProvider? ServicesOrNull => (Current as App)?._host?.Services;

    public static IServiceProvider Services => ServicesOrNull ?? throw new InvalidOperationException("O aplicativo ainda não iniciou.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        JanelasNaTela.Iniciar();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Os ViewModels (projeto Presentation) agendam na thread da interface por esta abstração.
        UiThread.Dispatcher = new AvaloniaUiDispatcher();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A janela de boas-vindas e os diálogos da inicialização não podem encerrar o programa ao fechar.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var args = desktop.Args ?? [];
            // O Finder entrega o .med por evento, não por argumento; assinado já, porque chega durante a inicialização.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += OnActivated;
            Dispatcher.UIThread.Post(async () => await StartAsync(desktop, args));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, string[] args)
    {
        EditorStartup.WaitForPreviousInstance(args);
        var paths = new AppPaths();
        ImageEngine.Text.FontCatalog.UserFontsFolder = paths.Fonts;
        // Preferências lidas antes do host: o JsonSettingsService completa os valores que faltam, e a janela de
        // boas-vindas precisa saber o que o usuário ainda não preencheu.
        var preferenceStore = Infrastructure.Settings.UserPreferencesStoreFactory.Create(paths);
        var preferences = preferenceStore.Read();
        // Os argumentos não vão para o host: o único argumento esperado é o caminho de um .med (associação de arquivos).
        _host = Host.CreateDefaultBuilder()
            .UseMeuEditorInfrastructure(paths, preferenceStore)
            .ConfigureServices(ConfigureServices)
            .Build();

        RegisterGlobalExceptionHandlers();
        await _host.StartAsync();

        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Meu Editor de Vídeo {Version} iniciado (Avalonia).", AppInfo.Version);

        // Idioma (Configurações › Geral): cultura e traduções antes de criar as janelas.
        var language = EditorStartup.ResolveLanguage(Services);
        EditorStartup.ApplyLanguage(language);
        Localization.LocalizationHook.Install();

        Services.GetRequiredService<IThemeService>().Apply(Services.GetRequiredService<ISettingsService>().Current.Theme);

        // Boas-vindas: preferências ausentes ou vazias. Se o idioma escolhido for outro, reinicia nele.
        if (!preferences.IsComplete && !await AskPreferencesAsync(desktop, preferences, preferenceStore, language))
            return;

        // Atalho global (F10 da gravação de tela): SharpHook onde houver suporte; o gancho só inicia no primeiro uso.
        GlobalHotkeys.Current = GlobalHotkeySupport.Create(logger);

        var window = Services.GetRequiredService<MainWindow>();
        window.BeforeClose = ShutdownServicesAsync;
        desktop.MainWindow = window;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        // Sem tela inicial: o editor abre com um projeto novo vazio; depois, se for o caso, o projeto pedido, a recuperação
        // de um fechamento inesperado ou o último projeto.
        Services.GetRequiredService<IProjectService>().CreateUntitled(Translator.T("Sem título"));
        Services.GetRequiredService<INavigationService>().NavigateTo<EditorViewModel>();
        window.Show();

        var requested = _fileToOpen;
        _fileToOpen = null;
        await EditorStartup.AfterMainWindowShownAsync(Services, requested is null ? args : [requested, .. args], logger);
        _readyForFiles = true;
        if (_fileToOpen is { } late)
        {
            _fileToOpen = null;
            await EditorStartup.OpenProjectAsync(Services, late, logger);
        }
    }

    /// <summary>Arquivo aberto pelo sistema (macOS): guardado até a janela ficar pronta ou aberto na hora.</summary>
    private async void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is not FileActivatedEventArgs files) return;
        if (StartupArguments.ProjectPath(files.Files.Select(f => f.TryGetLocalPath()).OfType<string>()) is not { } path) return;
        if (!_readyForFiles)
        {
            _fileToOpen = path;
            return;
        }
        if (ServicesOrNull is not { } services) return;
        await EditorStartup.OpenProjectAsync(services, path, services.GetRequiredService<ILogger<App>>());
    }

    /// <summary>
    /// Reinicia o programa para aplicar o idioma salvo em Configurações › Geral. Fecha a janela pelo caminho normal
    /// (pergunta sobre alterações não salvas; se o usuário cancelar, nada muda) e abre de novo o projeto salvo.
    /// </summary>
    public void RestartForLanguage()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window }) return;
        _restartRequested = true;
        void Canceled(object? sender, EventArgs e)
        {
            _restartRequested = false;
            window.CloseCanceled -= Canceled;
        }
        window.CloseCanceled += Canceled;
        window.Close();
    }

    /// <summary>Janela de boas-vindas. Devolve false quando o programa vai reiniciar no idioma escolhido.</summary>
    private async Task<bool> AskPreferencesAsync(IClassicDesktopStyleApplicationLifetime desktop, UserPreferences current,
        IUserPreferencesStore store, string language)
    {
        var welcome = new Views.Dialogs.WelcomeWindow(current);
        var closed = new TaskCompletionSource();
        welcome.Closed += (_, _) => closed.TrySetResult();
        welcome.Show();
        await closed.Task;

        var chosen = welcome.Result;
        var theme = await EditorStartup.SavePreferencesAsync(Services, store, chosen);
        if (!string.Equals(chosen.Language, language, StringComparison.OrdinalIgnoreCase))
        {
            EditorStartup.Relaunch();
            await ShutdownServicesAsync();
            desktop.Shutdown();
            return false;
        }
        Services.GetRequiredService<IThemeService>().Apply(theme);
        return true;
    }

    /// <summary>Chamado pela janela principal depois de confirmar o fechamento: projeto, autosave e host.</summary>
    private async Task ShutdownServicesAsync()
    {
        if (_host is null) return;
        (GlobalHotkeys.Current as IDisposable)?.Dispose();
        GlobalHotkeys.Current = new NoGlobalHotkey();
        if (_restartRequested)
            EditorStartup.Relaunch(Services.GetRequiredService<IProjectService>().Current?.FilePath);
        await EditorStartup.ShutdownAsync(Services);
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        _host = null;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddMeuEditor();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>Tratamento centralizado de exceções (RNF010): registra no log e informa o usuário em pt-BR.</summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        var logger = Services.GetRequiredService<ILogger<App>>();

        Dispatcher.UIThread.UnhandledException += (_, args) =>
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
        // Durante o encerramento o host já pode ter sido descartado: aí só fica o registro no log.
        try { ServicesOrNull?.GetService<IDialogService>()?.ShowError(error); }
        catch (ObjectDisposedException) { }
    }
}
