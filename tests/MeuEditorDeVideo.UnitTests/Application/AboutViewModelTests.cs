using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Janela "Sobre" (RF215).</summary>
public sealed class AboutViewModelTests
{
    [Fact]
    public void Mostra_desenvolvedor_versao_e_licenca()
    {
        var vm = new AboutViewModel();
        Assert.Equal("Desenvolvedor: Diego Mendes Rodrigues", vm.DeveloperText);
        Assert.StartsWith("Versão ", vm.Version);
        Assert.DoesNotContain("+", vm.Version);
        Assert.Contains("GPL-3.0-or-later", vm.License);
        Assert.Contains(vm.Components, c => c.Name.StartsWith("FFmpeg", StringComparison.Ordinal) && c.License == "GPL-3.0-or-later");
        Assert.Empty(vm.Models);
    }
}
