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
        Assert.Equal($"https://meueditordevideos.aplicativoia.online/version?language={code}", UpdateEndpoints.UrlFor(language).AbsoluteUri);

    [Fact]
    public void Verificacao_automatica_vence_depois_de_30_dias()
    {
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        Assert.True(UpdateEndpoints.IsDue(null, now));
        Assert.False(UpdateEndpoints.IsDue(now.AddDays(-30), now));
        Assert.True(UpdateEndpoints.IsDue(now.AddDays(-30).AddMinutes(-1), now));
    }

    [Fact]
    public void Nova_versao_e_qualquer_versao_diferente()
    {
        Assert.False(UpdateEndpoints.IsNewer("1.1.0", "1.1.0"));
        Assert.False(UpdateEndpoints.IsNewer(" 1.1.0 ", "1.1.0"));
        Assert.True(UpdateEndpoints.IsNewer("1.2.0", "1.1.0"));
        Assert.False(UpdateEndpoints.IsNewer("1.3.0", "1.3.0"));
        Assert.True(UpdateEndpoints.IsNewer("1.4.0", "1.3.0"));
        Assert.True(UpdateEndpoints.IsNewer("1.2.0", "1.3.0"));
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
        Assert.Equal("https://meueditordevideos.aplicativoia.online/version?language=fr-FR", asked!.AbsoluteUri);

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
