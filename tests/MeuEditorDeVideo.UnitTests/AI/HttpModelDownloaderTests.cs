using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MeuEditorDeVideo.ModelDownloads;

namespace MeuEditorDeVideo.UnitTests.AI;

/// <summary>Download dos modelos com um servidor simulado (nenhum acesso real à rede).</summary>
public sealed class HttpModelDownloaderTests : IDisposable
{
    private static readonly string[] Hosts = ["huggingface.co", "github.com"];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-download-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly byte[] _content = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7 % 253)).ToArray();

    public HttpModelDownloaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Sha => Convert.ToHexStringLower(SHA256.HashData(_content));

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private HttpResponseMessage Content(HttpRequestMessage request)
    {
        if (request.Headers.Range?.Ranges.FirstOrDefault() is { From: { } from })
        {
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(_content[(int)from..]) };
            return partial;
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_content) };
    }

    private static HttpResponseMessage Redirect(string to)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(to);
        return r;
    }

    [Fact]
    public async Task Baixa_segue_redirecionamento_https_e_confere_o_hash()
    {
        var server = new FakeServer(r => r.RequestUri!.Host == "huggingface.co" ? Redirect("https://cdn-lfs.hf.co/arquivo") : Content(r));
        using var d = new HttpModelDownloader(Hosts, handler: server);
        var dest = Path.Combine(_dir, "m.onnx");
        long last = 0;
        await d.DownloadAsync(new Uri("https://huggingface.co/x/resolve/abc/m.onnx"), dest, _content.Length, Sha,
            new Progress<long>(b => last = b));
        Assert.Equal(_content, await File.ReadAllBytesAsync(dest));
        Assert.False(File.Exists(dest + ".part"));
        Assert.Equal(2, server.Requests.Count);
        Assert.Contains(server.Requests, r => r.Headers.UserAgent.ToString().StartsWith("MeuEditorDeVideo", StringComparison.Ordinal));
        Assert.All(server.Requests, r => Assert.False(r.Headers.Contains("Cookie")));
    }

    [Theory]
    [InlineData("http://huggingface.co/m.onnx")]
    [InlineData("https://exemplo.com/m.onnx")]
    public async Task Endereco_fora_da_lista_ou_sem_https_e_recusado(string url)
    {
        var server = new FakeServer(Content);
        using var d = new HttpModelDownloader(Hosts, handler: server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.DownloadAsync(new Uri(url), Path.Combine(_dir, "m"), _content.Length, Sha));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Redirecionamento_para_http_e_recusado()
    {
        var server = new FakeServer(_ => Redirect("http://inseguro.example/m.onnx"));
        using var d = new HttpModelDownloader(Hosts, handler: server);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            d.DownloadAsync(new Uri("https://github.com/x/raw/abc/m.onnx"), Path.Combine(_dir, "m"), _content.Length, Sha));
    }

    [Fact]
    public async Task Hash_diferente_descarta_o_arquivo()
    {
        using var d = new HttpModelDownloader(Hosts, handler: new FakeServer(Content));
        var dest = Path.Combine(_dir, "m.onnx");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            d.DownloadAsync(new Uri("https://huggingface.co/m.onnx"), dest, _content.Length, new string('0', 64)));
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task Download_interrompido_e_retomado_com_range()
    {
        var dest = Path.Combine(_dir, "m.onnx");
        await File.WriteAllBytesAsync(dest + ".part", _content[..100_000]);
        var server = new FakeServer(Content);
        using var d = new HttpModelDownloader(Hosts, handler: server);
        await d.DownloadAsync(new Uri("https://huggingface.co/m.onnx"), dest, _content.Length, Sha);
        Assert.Equal(_content, await File.ReadAllBytesAsync(dest));
        Assert.Equal(new RangeItemHeaderValue(100_000, null).From, server.Requests.Single().Headers.Range!.Ranges.Single().From);
    }

    [Fact]
    public async Task Erro_do_servidor_vira_mensagem_em_portugues()
    {
        using var d = new HttpModelDownloader(Hosts, handler: new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            d.DownloadAsync(new Uri("https://huggingface.co/m.onnx"), Path.Combine(_dir, "m"), _content.Length, Sha));
        Assert.Contains("Não foi possível baixar", ex.Message);
        Assert.Contains("404", ex.Message);
    }
}
