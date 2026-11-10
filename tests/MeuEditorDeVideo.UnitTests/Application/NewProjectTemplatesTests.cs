using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Modelos da tela "Novo projeto": gerais, internet e redes sociais, personalizado.</summary>
public sealed class NewProjectTemplatesTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? Changed
        {
            add { }
            remove { }
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static NewProjectViewModel Create() => new(null!, new FakeSettings());

    [Theory]
    [InlineData("YouTube")]
    [InlineData("YouTube Shorts")]
    [InlineData("Instagram Reels")]
    [InlineData("Instagram Stories")]
    [InlineData("Instagram Feed")]
    [InlineData("TikTok")]
    [InlineData("Kwai")]
    [InlineData("WhatsApp Status")]
    [InlineData("WhatsApp compartilhamento")]
    [InlineData("Facebook Feed")]
    [InlineData("Facebook Reels")]
    [InlineData("LinkedIn")]
    public void Ha_modelos_para_as_plataformas_de_internet(string label)
    {
        var template = Assert.Single(Create().Templates, t => t.Label == label);
        Assert.Equal("Internet e redes sociais", template.Group);
    }

    [Fact]
    public void Cada_modelo_preenche_resolucao_e_fps_e_gera_projeto_valido()
    {
        var vm = Create();
        foreach (var template in vm.Templates.Where(t => !t.IsCustom))
        {
            vm.SelectedTemplate = template;
            Assert.Same(template, vm.SelectedTemplate); // não vira "Personalizado" sozinho
            Assert.Equal(template.Width.ToString(), vm.CustomWidth);
            Assert.Equal(template.Height.ToString(), vm.CustomHeight);
            Assert.Equal(template.FrameRate, vm.SelectedFrameRate.Value);
            var settings = new ProjectSettings { Width = template.Width, Height = template.Height, FrameRate = template.FrameRate };
            Assert.Empty(settings.Validate());
        }
    }

    [Fact]
    public void Personalizado_fica_por_ultimo_e_os_grupos_ficam_juntos()
    {
        var templates = Create().Templates;
        Assert.True(templates[^1].IsCustom);
        var groups = templates.Select(t => t.Group).ToList();
        Assert.Equal(groups.Distinct().Count(), groups.Where((g, i) => i == 0 || groups[i - 1] != g).Count());
    }
}
