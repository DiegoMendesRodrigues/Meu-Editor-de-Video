using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MeuEditorDeVideo.Core.Updates;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.ModelDownloads;

/// <summary>
/// Consulta o serviço de versões (Ajuda › Verificar atualizações e a verificação mensal):
/// <list type="bullet">
/// <item>só HTTPS e só o host de <see cref="UpdateEndpoints.Host"/>, sem redirecionamentos;</item>
/// <item>nenhum dado do usuário é enviado além do idioma da interface e do sistema operacional (windows, linux ou macos) na URL; sem cookies, User-Agent genérico;</item>
/// <item>resposta limitada a 64 KB e lida como JSON.</item>
/// </list>
/// </summary>
public sealed class HttpUpdateChecker : IUpdateChecker, IDisposable
{
    private const int MaxBytes = 64 * 1024;
    private readonly HttpClient _http;
    private readonly ILogger<HttpUpdateChecker>? _logger;

    public HttpUpdateChecker(ILogger<HttpUpdateChecker>? logger = null, HttpMessageHandler? handler = null)
    {
        _logger = logger;
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MeuEditorDeVideo", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<UpdateInfo> CheckAsync(string language, CancellationToken cancellationToken = default)
    {
        var url = UpdateEndpoints.UrlFor(language);
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IOException($"O serviço de versões respondeu {(int)response.StatusCode} ({response.ReasonPhrase}).");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) throw new IOException("A resposta do serviço de versões é grande demais.");
            }
            var info = Parse(buffer.ToArray());
            _logger?.LogInformation("Verificação de atualizações: versão publicada {Version}.", info.Version);
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or HttpIOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"Não foi possível consultar {url.Host}: {ex.Message} Verifique a conexão com a internet e tente de novo.", ex);
        }
    }

    /// <summary>Lê o JSON do serviço; "version" é obrigatório, os demais campos podem ser texto ou lista.</summary>
    public static UpdateInfo Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new IOException("A resposta do serviço de versões não é um objeto JSON.");
            var version = Text(root, "version");
            if (string.IsNullOrWhiteSpace(version)) throw new IOException("A resposta do serviço de versões não tem o campo \"version\".");
            return new UpdateInfo(version.Trim(), Text(root, "languages") ?? "", Text(root, "operatingSystem") ?? "",
                Text(root, "author") ?? "", Text(root, "microsoftStoreLink")?.Trim(), Text(root, "installLink")?.Trim());
        }
        catch (JsonException ex)
        {
            throw new IOException("A resposta do serviço de versões não é um JSON válido.", ex);
        }
    }

    private static string? Text(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var value = property.Value;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
