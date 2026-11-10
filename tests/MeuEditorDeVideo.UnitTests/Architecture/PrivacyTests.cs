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

    /// <summary>
    /// Única exceção (decisão de 10/11/2026): <c>MeuEditorDeVideo.ModelDownloads</c> baixa os arquivos dos modelos do
    /// catálogo quando o usuário pede. Ele só pode depender do Core — sem acesso a mídias, projetos, áudio ou IA — e
    /// só pode usar HTTP (sem sockets, WebSockets ou e-mail).
    /// </summary>
    [Fact]
    public void Download_de_modelos_e_a_unica_excecao_e_fica_isolado()
    {
        var assembly = Assembly.Load("MeuEditorDeVideo.ModelDownloads");
        var product = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("MeuEditorDeVideo", StringComparison.Ordinal));
        Assert.Equal(["MeuEditorDeVideo.Core"], product);
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny("System.Net.Sockets", "System.Net.WebSockets", "System.Net.Mail", "System.Net.WebClient")
            .GetResult();
        Assert.True(result.IsSuccessful, "Uso de rede além de HTTP em: " + string.Join(", ", result.FailingTypeNames ?? []));
        Assert.DoesNotContain("MeuEditorDeVideo.ModelDownloads", ProductAssemblies().Cast<object[]>().Select(row => (string)row[0]));
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
