using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

public sealed class ProxyPolicyTests
{
    private static MediaAsset Asset(MediaKind kind, int w, int h) => new()
    {
        Id = Guid.NewGuid(), Kind = kind, Name = "m", OriginalPath = @"C:\m.mp4", Width = w, Height = h,
    };

    [Theory]
    [InlineData(1920, 1080, 960, 540)]
    [InlineData(3840, 2160, 960, 540)]
    [InlineData(1080, 1920, 540, 960)]
    [InlineData(1366, 768, 960, 540)]
    [InlineData(640, 360, 640, 360)]   // menor que 540p: não amplia
    [InlineData(4096, 2160, 1024, 540)] // DCI 4K
    [InlineData(1440, 1080, 720, 540)] // 4:3
    [InlineData(641, 361, 640, 360)]   // dimensões ímpares viram o par mais próximo
    public void Tamanho_do_proxy(int w, int h, int pw, int ph)
    {
        Assert.Equal((pw, ph), ProxyPolicy.SizeFor(w, h));
    }

    [Fact]
    public void Tamanho_invalido_e_rejeitado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProxyPolicy.SizeFor(0, 1080));
    }

    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1080, 1920, false)]
    [InlineData(2560, 1440, true)]
    [InlineData(3840, 2160, true)]
    [InlineData(1440, 2560, true)]
    [InlineData(2048, 1080, true)]
    public void Recomendado_acima_de_1080p(int w, int h, bool expected)
    {
        Assert.Equal(expected, ProxyPolicy.IsRecommended(Asset(MediaKind.Video, w, h)));
    }

    [Fact]
    public void So_videos_tem_proxy()
    {
        Assert.True(ProxyPolicy.Supports(Asset(MediaKind.Video, 3840, 2160)));
        Assert.False(ProxyPolicy.Supports(Asset(MediaKind.Image, 6000, 4000)));
        Assert.False(ProxyPolicy.Supports(Asset(MediaKind.Audio, 0, 0)));
        Assert.False(ProxyPolicy.Supports(Asset(MediaKind.ImageSequence, 3840, 2160)));
        Assert.False(ProxyPolicy.IsRecommended(Asset(MediaKind.Image, 6000, 4000)));
    }

    [Fact]
    public void Configuracoes_padrao_usam_e_geram_proxies()
    {
        var s = new AppSettings();
        Assert.True(s.UseProxies);
        Assert.True(s.AutoGenerateProxies);
    }
}
