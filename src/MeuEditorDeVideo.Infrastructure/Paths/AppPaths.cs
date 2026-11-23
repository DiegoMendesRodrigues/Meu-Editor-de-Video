using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Infrastructure.Paths;

public sealed class AppPaths : IAppPaths
{
    private const string AppFolder = "MeuEditorDeVideo";

    // DoNotVerify: sem ele o .NET devolve "" se a pasta ainda não existe (usuário novo no Linux) e os dados iriam
    // para a pasta atual.
    public AppPaths()
        : this(Roots(
            OperatingSystem.IsMacOS(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify)))
    {
    }

    private AppPaths((string Roaming, string Local) roots)
        : this(roots.Roaming, roots.Local)
    {
    }

    /// <summary>
    /// Raízes dos dados do usuário. Windows: %APPDATA% e %LOCALAPPDATA%. Linux: ~/.config e ~/.local/share (XDG).
    /// macOS: as duas em ~/Library/Application Support, a pasta própria do sistema para dados de aplicativos: o .NET
    /// devolve ~/.config e ~/.local/share também no macOS, por isso ele não é usado ali. Pura, para testar em qualquer
    /// sistema.
    /// </summary>
    public static (string Roaming, string Local) Roots(bool macOs, string home, string appData, string localAppData) =>
        macOs
            ? (Path.Combine(home, "Library", "Application Support", AppFolder), Path.Combine(home, "Library", "Application Support", AppFolder))
            : (Path.Combine(appData, AppFolder), Path.Combine(localAppData, AppFolder));

    /// <summary>Construtor para testes, permitindo raízes isoladas.</summary>
    public AppPaths(string roamingRoot, string localRoot)
    {
        RoamingData = roamingRoot;
        LocalData = localRoot;
    }

    public string RoamingData { get; }
    public string LocalData { get; }
    public string Logs => Path.Combine(LocalData, "logs");
    public string Cache => Path.Combine(LocalData, "cache");
    public string Autosave => Path.Combine(LocalData, "autosave");
    public string AiModels => Path.Combine(LocalData, "ai-models");
    public string Backups => Path.Combine(LocalData, "backups");
    /// <summary>Fontes adicionadas pelo usuário para textos (sem instalar no Windows).</summary>
    public string Fonts => Path.Combine(RoamingData, "fonts");
    public string SettingsFile => Path.Combine(RoamingData, "settings.json");

    public void EnsureCreated()
    {
        foreach (var dir in new[] { RoamingData, LocalData, Logs, Cache, Autosave, AiModels, Backups })
            Directory.CreateDirectory(dir);
    }
}
