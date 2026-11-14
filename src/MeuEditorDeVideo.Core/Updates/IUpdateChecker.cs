namespace MeuEditorDeVideo.Core.Updates;

/// <summary>Dados da versão publicada, lidos do JSON do serviço de versões.</summary>
public sealed record UpdateInfo(
    string Version,
    string Languages,
    string OperatingSystem,
    string Author,
    string? MicrosoftStoreLink,
    string? InstallLink);

/// <summary>
/// Consulta a versão publicada do programa (Ajuda › Verificar atualizações e a verificação mensal ao abrir). A
/// implementação fica em <c>MeuEditorDeVideo.ModelDownloads</c>, o único assembly com acesso à rede. Falhas de rede ou
/// de formato viram <see cref="IOException"/> com mensagem em pt-BR.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateInfo> CheckAsync(string language, CancellationToken cancellationToken = default);
}

/// <summary>Endereço do serviço de versões e regras da verificação.</summary>
public static class UpdateEndpoints
{
    public const string Host = "meueditordevideos.aplicativoia.online";

    /// <summary>Intervalo da verificação automática ao abrir o programa.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(30);

    /// <summary>URL da verificação para o idioma da interface (códigos de <c>SupportedLanguages</c>).</summary>
    public static Uri UrlFor(string language)
    {
        var code = language switch
        {
            "de" => "de",
            "es" => "es",
            "fr" => "fr-FR",
            "pt-BR" => "pt-BR",
            "pt-PT" => "pt",
            _ => "en",
        };
        return new Uri($"https://{Host}/version?language={code}");
    }

    /// <summary>A verificação automática roda quando passaram mais de 30 dias da última (ou nunca houve uma).</summary>
    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is null || now > lastCheck.Value + Interval;

    /// <summary>Há versão nova quando a publicada é diferente da instalada.</summary>
    /// <summary>Endereço que pode virar link clicável: só http/https; outro texto é mostrado como está.</summary>
    public static Uri? AsLink(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    public static bool IsNewer(string published, string installed) =>
        !string.Equals(published.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);
}
