using System.Diagnostics;
using System.Globalization;
using System.IO;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Presentation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Presentation.Hosting;

/// <summary>
/// Passos da inicialização e do reinício que não dependem do toolkit de interface: idioma, recuperação, último
/// projeto, manutenção em segundo plano e relançar o programa (troca de idioma).
/// </summary>
public static class EditorStartup
{
    /// <summary>Argumento da nova instância ao reiniciar pelo idioma: espera o processo anterior terminar.</summary>
    public const string WaitArgument = "--aguardar-processo=";

    /// <summary>Idioma salvo, ou o mais próximo do sistema se o salvo não for suportado.</summary>
    public static string ResolveLanguage(IServiceProvider services)
    {
        var language = services.GetRequiredService<ISettingsService>().Current.Language;
        return SupportedLanguages.IsSupported(language) ? language : SupportedLanguages.FromCulture(CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>Cultura (datas, números) e traduções da interface antes de criar as janelas.</summary>
    public static void ApplyLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Localization.Translator.Load(language, Path.Combine(AppContext.BaseDirectory, "i18n"));
    }

    /// <summary>Ao reiniciar, a instância anterior ainda pode estar fechando o projeto e o autosave: espera até 15 s.</summary>
    public static void WaitForPreviousInstance(IEnumerable<string> args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(WaitArgument, StringComparison.Ordinal));
        if (arg is null || !int.TryParse(arg[WaitArgument.Length..], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(15_000);
        }
        catch (ArgumentException)
        {
            // Já terminou.
        }
    }

    /// <summary>
    /// Abre uma nova instância que espera esta terminar e, se houver, abre <paramref name="project"/>. Dentro de um
    /// pacote .app do macOS, relança o pacote com <c>open -n</c> (o executável sozinho não teria menu nem ícone).
    /// </summary>
    public static void Relaunch(string? project = null)
    {
        if (Environment.ProcessPath is not { } exe) return;
        var args = new List<string> { WaitArgument + Environment.ProcessId };
        if (project is not null && File.Exists(project)) args.Add(project);

        ProcessStartInfo start;
        var bundle = OperatingSystem.IsMacOS() ? AppBundle(exe) : null;
        if (bundle is not null)
        {
            start = new ProcessStartInfo("open") { UseShellExecute = false };
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add(bundle);
            start.ArgumentList.Add("--args");
        }
        else
        {
            start = new ProcessStartInfo(exe) { UseShellExecute = false };
        }
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start)?.Dispose();
    }

    /// <summary>Pasta .app que contém o executável (…/Nome.app/Contents/MacOS/exe), ou null.</summary>
    public static string? AppBundle(string exe)
    {
        var macos = Path.GetDirectoryName(exe);
        var contents = macos is null ? null : Path.GetDirectoryName(macos);
        var app = contents is null ? null : Path.GetDirectoryName(contents);
        return app is not null && app.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(macos), "MacOS", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal)
            ? app
            : null;
    }

    /// <summary>
    /// Grava as cinco preferências da janela de boas-vindas (Name, Language, ThemeColor, CheckForNewVersions e
    /// LastCheck) e as copia para as configurações. Devolve o tema escolhido.
    /// </summary>
    public static async Task<AppTheme> SavePreferencesAsync(IServiceProvider services, IUserPreferencesStore store, UserPreferences chosen)
    {
        store.WriteName(chosen.Name!);
        store.WriteLanguage(chosen.Language!);
        store.WriteThemeColor(chosen.ThemeColor!);
        store.WriteCheckForNewVersions(chosen.CheckForNewVersions!.Value);
        store.WriteLastCheck(chosen.LastCheck!.Value);

        var settings = services.GetRequiredService<ISettingsService>();
        var theme = Enum.Parse<AppTheme>(chosen.ThemeColor!);
        await settings.SaveAsync(settings.Current with
        {
            UserName = chosen.Name!,
            Language = chosen.Language!,
            Theme = theme,
            CheckForNewVersions = chosen.CheckForNewVersions.Value,
            LastUpdateCheck = chosen.LastCheck,
        });
        return theme;
    }

    /// <summary>
    /// Depois de mostrar a janela principal: salvamento automático, fila de renderização, manutenção, o projeto pedido
    /// (ou a recuperação, ou o último projeto) e a verificação mensal de atualizações.
    /// </summary>
    public static async Task AfterMainWindowShownAsync(IServiceProvider services, IReadOnlyList<string> args, ILogger logger)
    {
        services.GetRequiredService<AutosaveScheduler>().Start();
        await services.GetRequiredService<IRenderQueueService>().InitializeAsync();
        StartBackgroundMaintenance(services, logger);
        if (StartupArguments.ProjectPath(args.ToArray()) is { } project)
            await OpenProjectAsync(services, project, logger);
        else if (!await OfferRecoveryAsync(services))
            await OpenLastProjectAsync(services, logger);
        // Verificação mensal de atualizações (registro CheckForNewVersions e LastCheck).
        await services.GetRequiredService<UpdateCheckService>().CheckOnStartupAsync();
    }

    /// <summary>Fecha o projeto e o salvamento automático ao sair.</summary>
    public static async Task ShutdownAsync(IServiceProvider services)
    {
        await services.GetRequiredService<AutosaveScheduler>().DisposeAsync();
        await services.GetRequiredService<IProjectService>().CloseAsync();
    }

    /// <summary>
    /// Em segundo plano: detecta o hardware (§12.3, inclui testar os codificadores) e aplica o limite do cache em disco
    /// (RF209). Nada disso bloqueia a interface.
    /// </summary>
    private static void StartBackgroundMaintenance(IServiceProvider services, ILogger logger)
    {
        var hardware = services.GetRequiredService<IHardwareInfoService>();
        var cache = services.GetRequiredService<ICacheMaintenance>();
        var limit = (long)Math.Max(1, services.GetRequiredService<ISettingsService>().Current.PreviewCacheGigabytes) << 30;
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
    private static async Task<bool> OfferRecoveryAsync(IServiceProvider services)
    {
        var projects = services.GetRequiredService<IProjectService>();
        if (projects.FindRecoverable().FirstOrDefault() is not { } r) return false;
        if (!services.GetRequiredService<IDialogService>().Confirm("Recuperar projeto",
                $"O projeto \"{r.Name}\" não foi fechado corretamente. Há um salvamento automático de {r.SavedAt.LocalDateTime:g}.\n\n" +
                "Deseja recuperá-lo agora? Se escolher não, a cópia continua guardada e é oferecida de novo na próxima vez."))
            return false;
        return await services.GetRequiredService<IProjectWorkflow>().RecoverAsync(r);
    }

    /// <summary>"Abrir o último projeto ao iniciar" (configurações › Geral).</summary>
    private static async Task OpenLastProjectAsync(IServiceProvider services, ILogger logger)
    {
        var settings = services.GetRequiredService<ISettingsService>().Current;
        if (!settings.OpenLastProjectOnStartup || settings.RecentProjects.FirstOrDefault() is not { } last || !File.Exists(last)) return;
        await OpenProjectAsync(services, last, logger);
    }

    /// <summary>Abre um projeto na inicialização (arquivo .med aberto pelo sistema ou o último projeto).</summary>
    public static async Task OpenProjectAsync(IServiceProvider services, string path, ILogger logger)
    {
        try
        {
            await services.GetRequiredService<IProjectWorkflow>().OpenAsync(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível abrir o projeto {Caminho} na inicialização.", path);
        }
    }
}
