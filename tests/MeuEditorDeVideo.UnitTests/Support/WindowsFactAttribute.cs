// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Runtime.Versioning;

namespace MeuEditorDeVideo.UnitTests.Support;

/// <summary>Teste de código exclusivo do Windows: ignorado no Linux e no macOS.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Só no Windows.";
    }
}
