using System.Reflection;
using NetArchTest.Rules;

namespace MeuEditorDeVideo.UnitTests.Architecture;

/// <summary>Garante as referências controladas entre camadas (§2.2, §11).</summary>
public class LayeringTests
{
    private const string Root = "MeuEditorDeVideo";

    private static Assembly Load(string name) => Assembly.Load($"{Root}.{name}");

    private static readonly string[] Engines =
        ["ProjectEngine", "MediaEngine", "TimelineEngine", "ImageEngine", "AudioEngine", "AIEngine", "RenderingEngine"];

    [Fact]
    public void Domain_nao_depende_de_nenhuma_outra_camada()
    {
        var result = Types.InAssembly(Load("Domain"))
            .ShouldNot().HaveDependencyOnAny(
                $"{Root}.Core", $"{Root}.Application", $"{Root}.Infrastructure", $"{Root}.App",
                "System.Windows", "Microsoft.Extensions")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Core_depende_apenas_do_Domain()
    {
        var result = Types.InAssembly(Load("Core"))
            .ShouldNot().HaveDependencyOnAny(
                $"{Root}.Application", $"{Root}.Infrastructure", $"{Root}.App", "System.Windows")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_nao_conhece_infraestrutura_motores_nem_UI()
    {
        var forbidden = Engines.Select(e => $"{Root}.{e}")
            .Append($"{Root}.Infrastructure").Append($"{Root}.App").Append("System.Windows").ToArray();
        var result = Types.InAssembly(Load("Application")).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [InlineData("ProjectEngine")]
    [InlineData("MediaEngine")]
    [InlineData("TimelineEngine")]
    [InlineData("ImageEngine")]
    [InlineData("AudioEngine")]
    [InlineData("AIEngine")]
    [InlineData("RenderingEngine")]
    [InlineData("Infrastructure")]
    public void Motores_e_infraestrutura_nao_dependem_da_UI_nem_da_Application(string assembly)
    {
        var result = Types.InAssembly(Load(assembly))
            .ShouldNot().HaveDependencyOnAny($"{Root}.App", $"{Root}.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        "Tipos em violação: " + string.Join(", ", result.FailingTypeNames ?? []);
}
