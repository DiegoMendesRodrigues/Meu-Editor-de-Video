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

    /// <summary>Sistema em que o programa roda, no valor do parâmetro <c>os</c> da consulta: windows, linux ou macos.</summary>
    public static string CurrentOs =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>
    /// URL da verificação para o idioma da interface (códigos de <c>SupportedLanguages</c>) e o sistema operacional
    /// (<c>os</c>: windows, linux ou macos; o servidor responde com o link de download desse sistema). Só esses dois
    /// valores fixos saem do computador.
    /// </summary>
    public static Uri UrlFor(string language, string? os = null)
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
        var system = os switch
        {
            "windows" or "linux" or "macos" => os,
            _ => CurrentOs,
        };
        return new Uri($"https://{Host}/version?language={code}&os={system}");
    }

    /// <summary>O link da Microsoft Store só faz sentido no Windows.</summary>
    public static bool ShowsMicrosoftStoreLink => OperatingSystem.IsWindows();

    /// <summary>A verificação automática roda quando passaram mais de 30 dias da última (ou nunca houve uma).</summary>
    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is null || now > lastCheck.Value + Interval;

    /// <summary>Endereço que pode virar link clicável: só http/https; outro texto é mostrado como está.</summary>
    public static Uri? AsLink(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    /// <summary>
    /// Há versão nova só quando o campo "version" publicado é maior que a versão instalada (ex.: 1.4.0), comparando
    /// número a número ("1.10.0" é maior que "1.9.0"). Texto que não é versão não gera aviso.
    /// </summary>
    public static bool IsNewer(string published, string installed) =>
        ParseVersion(published) is { } site && ParseVersion(installed) is { } local && site > local;

    /// <summary>Lê "1.4", "v1.4.0" ou "1.4.0-beta+abc"; as partes ausentes valem zero e os sufixos são ignorados.</summary>
    private static Version? ParseVersion(string text)
    {
        var number = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!number.Contains('.')) number += ".0";
        return Version.TryParse(number, out var v)
            ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0))
            : null;
    }
}
