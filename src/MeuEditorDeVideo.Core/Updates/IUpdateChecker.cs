// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
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
    /// Há versão nova quando o campo "version" publicado não é igual ao da versão instalada (ex.: 1.4.0), seja ele
    /// maior ou menor. A comparação é pelo texto, sem os espaços das pontas: "1.4" e "v1.4.0" são diferentes de "1.4.0".
    /// </summary>
    public static bool IsDifferentVersion(string published, string installed) =>
        !string.Equals(published.Trim(), installed.Trim(), StringComparison.Ordinal);
}
