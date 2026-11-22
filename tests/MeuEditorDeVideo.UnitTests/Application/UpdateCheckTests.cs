using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.ModelDownloads;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Verificar atualizações: URL por idioma, prazo de 30 dias, comparação de versão e leitura do JSON.</summary>
public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("de", "de")]
    [InlineData("es", "es")]
    [InlineData("fr", "fr-FR")]
    [InlineData("en", "en")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt")]
    [InlineData("xx", "en")]
    public void Url_depende_do_idioma(string language, string code) =>
        Assert.Equal($"https://meueditordevideos.aplicativoia.online/version?language={code}&os={UpdateEndpoints.CurrentOs}", UpdateEndpoints.UrlFor(language).AbsoluteUri);

    [Theory]
    [InlineData("windows", "windows")]
    [InlineData("linux", "linux")]
    [InlineData("macos", "macos")]
    [InlineData("freebsd", null)]
    [InlineData(null, null)]
    public void Url_leva_o_sistema_operacional(string? os, string? expected)
    {
        var url = UpdateEndpoints.UrlFor("pt-BR", os);
        Assert.Equal(UpdateEndpoints.Host, url.Host);
        Assert.Equal($"?language=pt-BR&os={expected ?? UpdateEndpoints.CurrentOs}", url.Query);
    }

    [Fact]
    public void Sistema_atual_e_um_dos_tres_valores_do_servico() =>
        Assert.Contains(UpdateEndpoints.CurrentOs, new[] { "windows", "linux", "macos" });

    [Fact]
    public void Verificacao_automatica_vence_depois_de_30_dias()
    {
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        Assert.True(UpdateEndpoints.IsDue(null, now));
        Assert.False(UpdateEndpoints.IsDue(now.AddDays(-30), now));
        Assert.True(UpdateEndpoints.IsDue(now.AddDays(-30).AddMinutes(-1), now));
    }

    [Theory]
    [InlineData("1.4.0", false)]
    [InlineData(" 1.4.0 ", false)]
    [InlineData("1.3.0", true)]
    [InlineData("1.4.1", true)]
    [InlineData("1.10.0", true)]
    [InlineData("1.4", true)]
    [InlineData("1.4.0.0", true)]
    [InlineData("v1.4.0", true)]
    [InlineData("em breve", true)]
    public void Nova_versao_e_qualquer_uma_diferente_da_instalada(string publicada, bool nova)
    {
        Assert.Equal(nova, UpdateEndpoints.IsDifferentVersion(publicada, "1.4.0"));
    }

    [Fact]
    public void A_versao_de_referencia_e_a_do_aplicativo_1_4_0()
    {
        Assert.Equal("1.4.0", MeuEditorDeVideo.Presentation.Services.AppInfo.Version);
    }

    [Fact]
    public void So_http_e_https_viram_link()
    {
        Assert.NotNull(UpdateEndpoints.AsLink("https://exemplo.com/a.msi"));
        Assert.Null(UpdateEndpoints.AsLink("microsoft"));
        Assert.Null(UpdateEndpoints.AsLink("file:///C:/Windows/notepad.exe"));
        Assert.Null(UpdateEndpoints.AsLink(null));
    }

    [Fact]
    public void Le_o_json_do_servico()
    {
        const string json = """
            {"version":"1.2.0","languages":"Alem\u00e3o, Ingl\u00eas","operatingSystem":"Windows 11","author":"Autor",
             "microsoftStoreLink":"microsoft","installLink":"https:\/\/exemplo.com\/a.msi"}
            """;
        var info = HttpUpdateChecker.Parse(Encoding.UTF8.GetBytes(json));
        Assert.Equal("1.2.0", info.Version);
        Assert.Equal("Alemão, Inglês", info.Languages);
        Assert.Equal("Windows 11", info.OperatingSystem);
        Assert.Equal("Autor", info.Author);
        Assert.Equal("microsoft", info.MicrosoftStoreLink);
        Assert.Equal("https://exemplo.com/a.msi", info.InstallLink);
    }

    [Fact]
    public void Lista_de_idiomas_em_array_vira_texto()
    {
        var info = HttpUpdateChecker.Parse("""{"version":"2.0.0","languages":["de","en"]}"""u8);
        Assert.Equal("de, en", info.Languages);
        Assert.Equal("", info.Author);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("não é json")]
    public void Resposta_invalida_vira_IOException(string body) =>
        Assert.Throws<IOException>(() => HttpUpdateChecker.Parse(Encoding.UTF8.GetBytes(body)));

    [Fact]
    public async Task Consulta_a_url_do_idioma_e_erro_http_vira_IOException()
    {
        Uri? asked = null;
        var ok = new StubHandler(r =>
        {
            asked = r.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"version":"1.1.0"}""") };
        });
        using (var checker = new HttpUpdateChecker(handler: ok))
            Assert.Equal("1.1.0", (await checker.CheckAsync("fr")).Version);
        Assert.Equal($"https://meueditordevideos.aplicativoia.online/version?language=fr-FR&os={UpdateEndpoints.CurrentOs}", asked!.AbsoluteUri);

        using var failing = new HttpUpdateChecker(handler: new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<IOException>(() => failing.CheckAsync("en"));

        using var offline = new HttpUpdateChecker(handler: new StubHandler(_ => throw new HttpRequestException("sem rede")));
        await Assert.ThrowsAsync<IOException>(() => offline.CheckAsync("en"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
