using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>Escolhe a implementação de gravação do sistema operacional em execução.</summary>
public static class ScreenRecorderFactory
{
    public static IScreenRecorder Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsScreenRecorder();
        if (OperatingSystem.IsMacOS()) return new MacScreenRecorder();
        return new LinuxScreenRecorder();
    }
}
