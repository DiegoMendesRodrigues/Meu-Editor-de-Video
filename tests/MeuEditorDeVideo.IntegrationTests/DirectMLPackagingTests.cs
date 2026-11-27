// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>
/// No Windows o DirectML.dll do pacote acompanha o aplicativo. Sem ele, o ONNX Runtime carrega o do System32, que pode ser
/// antigo demais. O pacote só o copia com PlatformTarget definido (Directory.Build.props).
/// </summary>
public sealed class DirectMLPackagingTests
{
    [Fact]
    public void DirectML_do_pacote_acompanha_o_aplicativo_no_Windows()
    {
        var existe = File.Exists(Path.Combine(AppContext.BaseDirectory, "DirectML.dll"));
        Assert.Equal(OperatingSystem.IsWindows(), existe);
    }
}
