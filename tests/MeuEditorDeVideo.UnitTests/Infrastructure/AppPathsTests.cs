// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using MeuEditorDeVideo.Infrastructure.Paths;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

/// <summary>Pastas de dados do usuário em cada sistema.</summary>
public sealed class AppPathsTests
{
    [Fact]
    public void No_macOS_os_dados_ficam_em_Library_Application_Support()
    {
        var (roaming, local) = AppPaths.Roots(true, "/Users/ana", "/Users/ana/.config", "/Users/ana/.local/share");

        var esperado = Path.Combine("/Users/ana", "Library", "Application Support", "MeuEditorDeVideo");
        Assert.Equal(esperado, roaming);
        Assert.Equal(esperado, local);
    }

    [Fact]
    public void No_Linux_e_no_Windows_valem_as_pastas_do_sistema()
    {
        var (roaming, local) = AppPaths.Roots(false, "/home/ana", "/home/ana/.config", "/home/ana/.local/share");

        Assert.Equal(Path.Combine("/home/ana/.config", "MeuEditorDeVideo"), roaming);
        Assert.Equal(Path.Combine("/home/ana/.local/share", "MeuEditorDeVideo"), local);
    }

    [Fact]
    public void O_construtor_padrao_segue_o_sistema_em_que_roda()
    {
        var paths = new AppPaths();

        Assert.EndsWith("MeuEditorDeVideo", paths.LocalData);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), paths.LocalData);
        if (OperatingSystem.IsMacOS()) Assert.Contains(Path.Combine("Library", "Application Support"), paths.LocalData);
        // Subpastas e arquivos dentro da raiz, nunca na pasta atual.
        Assert.StartsWith(paths.LocalData, paths.Logs);
        Assert.StartsWith(paths.RoamingData, paths.SettingsFile);
    }
}
