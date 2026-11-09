using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Infrastructure.Paths;

public sealed class AppPaths : IAppPaths
{
    private const string AppFolder = "MeuEditorDeVideo";

    public AppPaths()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder))
    {
    }

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
