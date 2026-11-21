using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Infrastructure.Settings;

/// <summary>Escolhe o armazenamento das preferências do usuário: registro no Windows, arquivo JSON nos demais sistemas.</summary>
public static class UserPreferencesStoreFactory
{
    public const string FileName = "preferences.json";

    public static IUserPreferencesStore Create(IAppPaths paths) =>
        OperatingSystem.IsWindows()
            ? new RegistryPreferencesStore()
            : new FilePreferencesStore(Path.Combine(paths.RoamingData, FileName));
}
