// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using System.Text.RegularExpressions;

namespace MeuEditorDeVideo.UnitTests.Architecture;

/// <summary>
/// O ONNX Runtime recebe o ponteiro do RunOptions, não a referência: um temporário é finalizado pelo GC durante a
/// inferência e derruba o processo (0xC0000005, só em Release). Todo RunOptions fica num using.
/// </summary>
public sealed class NativeLifetimeTests
{
    [Fact]
    public void Todo_RunOptions_fica_vivo_ate_o_fim_da_inferencia()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(raiz.FullName, "MeuEditorDeVideo.sln"))) raiz = raiz.Parent!;

        var criados = Directory.EnumerateFiles(Path.Combine(raiz.FullName, "src", "MeuEditorDeVideo.AIEngine"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadLines(f).Where(l => l.Contains("new RunOptions", StringComparison.Ordinal)).Select(l => (Arquivo: f, Linha: l.Trim())))
            .ToList();

        Assert.NotEmpty(criados);
        Assert.All(criados, c => Assert.True(Regex.IsMatch(c.Linha, @"^using (var|RunOptions) "), $"{c.Arquivo}: {c.Linha}"));
    }
}
