using System.Reflection;

namespace MeuEditorDeVideo.App.Services;

public static class AppInfo
{
    public const string ProductName = "Meu Editor de Vídeo";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
