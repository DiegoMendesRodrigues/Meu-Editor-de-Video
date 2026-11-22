using MeuEditorDeVideo.Desktop;
using NetArchTest.Rules;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Só o ModelDownloads usa a rede (RF169 / RNF005); o aplicativo Avalonia também não.</summary>
public class PrivacyTests
{
    [Fact]
    public void Aplicativo_nao_usa_APIs_de_rede()
    {
        var assembly = typeof(App).Assembly;
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny("System.Net.Http", "System.Net.Sockets", "System.Net.WebSockets",
                "System.Net.WebClient", "System.Net.HttpWebRequest", "System.Net.Mail")
            .GetResult();
        Assert.True(result.IsSuccessful, "Uso de rede encontrado em: " + string.Join(", ", result.FailingTypeNames ?? []));
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("System.Net.Http", refs);
        Assert.DoesNotContain("System.Net.Sockets", refs);
    }

    [Fact]
    public void Aplicativo_nao_referencia_o_WPF()
    {
        var refs = typeof(App).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(refs, r => r is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml");
    }
}
