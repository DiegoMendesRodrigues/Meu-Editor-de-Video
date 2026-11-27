// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
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
                $"{Root}.Core", $"{Root}.Application", $"{Root}.Infrastructure", $"{Root}.Desktop",
                "System.Windows", "Microsoft.Extensions")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Core_depende_apenas_do_Domain()
    {
        var result = Types.InAssembly(Load("Core"))
            .ShouldNot().HaveDependencyOnAny(
                $"{Root}.Application", $"{Root}.Infrastructure", $"{Root}.Desktop", "System.Windows")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_nao_conhece_infraestrutura_motores_nem_UI()
    {
        var forbidden = Engines.Select(e => $"{Root}.{e}")
            .Append($"{Root}.Infrastructure").Append($"{Root}.Desktop").Append($"{Root}.Presentation").Append("System.Windows").ToArray();
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
            .ShouldNot().HaveDependencyOnAny($"{Root}.Desktop", $"{Root}.Application", $"{Root}.Presentation")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// ViewModels e serviços de interface não conhecem o toolkit (WPF ou Avalonia) nem o aplicativo:
    /// rodam e são testados em Windows, Linux e macOS.
    /// </summary>
    [Fact]
    public void Presentation_nao_depende_do_toolkit_de_interface()
    {
        // System.Windows.Input.ICommand é da biblioteca base (System.ObjectModel) e vale em todos os sistemas; por isso
        // a regra olha os assemblies do WPF e do Avalonia (e o próprio aplicativo, que se chama MeuEditorDeVideo), e não o namespace System.Windows inteiro.
        string[] toolkits = ["PresentationFramework", "PresentationCore", "System.Xaml", "WindowsBase", "Avalonia", $"{Root}"];
        var refs = Load("Presentation").GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(refs, r => toolkits.Any(t => r == t || (t != Root && r.StartsWith(t + ".", StringComparison.Ordinal))));
        var result = Types.InAssembly(Load("Presentation"))
            .ShouldNot().HaveDependencyOnAny("System.Windows.Controls", "System.Windows.Media", "System.Windows.Threading",
                "System.Windows.Data", "Avalonia", "Microsoft.Win32", $"{Root}.Desktop.")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        "Tipos em violação: " + string.Join(", ", result.FailingTypeNames ?? []);
}
