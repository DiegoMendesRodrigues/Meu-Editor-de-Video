using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.ModelDownloads;

/// <summary>
/// Download dos arquivos de modelos (única parte do aplicativo com acesso à rede):
/// <list type="bullet">
/// <item>só HTTPS e só os hosts permitidos (os redirecionamentos para a CDN da origem também precisam ser HTTPS);</item>
/// <item>nenhum dado do usuário é enviado — sem cookies, sem identificação além de um User-Agent genérico;</item>
/// <item>retoma um <c>.part</c> interrompido (cabeçalho Range) e confere tamanho e SHA-256 antes de entregar o arquivo.</item>
/// </list>
/// </summary>
public sealed class HttpModelDownloader : IModelDownloader, IDisposable
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _allowedHosts;
    private readonly ILogger<HttpModelDownloader>? _logger;

    public HttpModelDownloader(IReadOnlyList<string> allowedHosts, ILogger<HttpModelDownloader>? logger = null,
        HttpMessageHandler? handler = null)
    {
        _allowedHosts = allowedHosts;
        _logger = logger;
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // redirecionamentos conferidos um a um (HTTPS obrigatório)
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeuEditorDeVideo", "1.0"));
    }

    private const int MaxRedirects = 8;

    public async Task DownloadAsync(Uri url, string destination, long expectedBytes, string sha256, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DownloadCoreAsync(url, destination, expectedBytes, sha256, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or HttpIOException)
        {
            // Quem chama não depende de tipos de rede: a falha vira IOException com mensagem em pt-BR.
            throw new IOException($"Não foi possível baixar {Path.GetFileName(destination)} de {url.Host}: {ex.Message} " +
                "Verifique a conexão com a internet e tente de novo; o que já foi baixado é aproveitado.", ex);
        }
    }

    private async Task DownloadCoreAsync(Uri url, string destination, long expectedBytes, string sha256, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !_allowedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Endereço de download não permitido: {url}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var part = destination + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > expectedBytes) { File.Delete(part); existing = 0; }

        if (existing < expectedBytes)
        {
            using var response = await SendAsync(url, existing, cancellationToken);
            if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                existing = 0; // o servidor ignorou o Range: recomeça
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
                1 << 20, useAsync: true);
            var buffer = new byte[1 << 20];
            var total = existing;
            progress?.Report(total);
            int read;
            var lastReport = Environment.TickCount64;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > expectedBytes) throw new InvalidDataException("O servidor enviou mais dados que o esperado para o modelo.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (Environment.TickCount64 - lastReport > 150) { progress?.Report(total); lastReport = Environment.TickCount64; }
            }
            progress?.Report(total);
        }

        var length = new FileInfo(part).Length;
        string actual;
        await using (var stream = File.OpenRead(part))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        if (length != expectedBytes || !string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidDataException(length != expectedBytes
                ? $"Download incompleto: {length} de {expectedBytes} bytes. Tente de novo."
                : "O arquivo baixado não confere com o SHA-256 do catálogo e foi descartado.");
        }
        File.Move(part, destination, overwrite: true);
        _logger?.LogInformation("Download concluído e verificado: {File} ({Bytes} bytes)", Path.GetFileName(destination), length);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, long from, CancellationToken cancellationToken)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (from > 0) request.Headers.Range = new RangeHeaderValue(from, null);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                if (url.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("A origem redirecionou o download para um endereço sem HTTPS; download cancelado.");
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"A origem do modelo respondeu com erro {status}.");
            }
            return response;
        }
        throw new HttpRequestException("Redirecionamentos demais ao baixar o modelo.");
    }

    public void Dispose() => _http.Dispose();
}
