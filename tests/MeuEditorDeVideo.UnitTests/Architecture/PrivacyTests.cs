using System.Reflection;
using NetArchTest.Rules;

namespace MeuEditorDeVideo.UnitTests.Architecture;

/// <summary>
/// RF169 / RNF005 / CA016: nenhum assembly do produto pode usar APIs de rede.
/// Exceções exigem justificativa documentada e devem ser adicionadas explicitamente aqui.
/// </summary>
public class PrivacyTests
{
    private static readonly string[] NetworkNamespaces =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebSockets",
        "System.Net.WebClient",
        "System.Net.HttpWebRequest",
        "System.Net.Mail",
    ];

    public static TheoryData<string> ProductAssemblies() =>
    [
        "MeuEditorDeVideo", // App
        "MeuEditorDeVideo.Domain", "MeuEditorDeVideo.Core", "MeuEditorDeVideo.Application",
        "MeuEditorDeVideo.Infrastructure", "MeuEditorDeVideo.ProjectEngine", "MeuEditorDeVideo.MediaEngine",
        "MeuEditorDeVideo.TimelineEngine", "MeuEditorDeVideo.ImageEngine", "MeuEditorDeVideo.AudioEngine",
        "MeuEditorDeVideo.AIEngine", "MeuEditorDeVideo.RenderingEngine",
    ];

    [Theory, MemberData(nameof(ProductAssemblies))]
    public void Nenhum_assembly_do_produto_usa_APIs_de_rede(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName))
            .ShouldNot().HaveDependencyOnAny(NetworkNamespaces)
            .GetResult();
        Assert.True(result.IsSuccessful, "Uso de rede encontrado em: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory, MemberData(nameof(ProductAssemblies))]
    public void Nenhum_assembly_do_produto_referencia_bibliotecas_de_rede(string assemblyName)
    {
        var refs = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("System.Net.Http", refs);
        Assert.DoesNotContain("System.Net.Sockets", refs);
        Assert.DoesNotContain("System.Net.WebSockets.Client", refs);
    }
}
