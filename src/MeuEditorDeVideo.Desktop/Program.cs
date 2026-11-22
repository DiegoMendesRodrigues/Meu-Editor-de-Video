using Avalonia;

namespace MeuEditorDeVideo.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Também usado pelo designer e pelos testes de interface (Avalonia.Headless).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { DisableDefaultApplicationMenuItems = true })
            .LogToTrace();
}
