namespace MeuEditorDeVideo.Core.Abstractions;

/// <summary>
/// Preferências do usuário: nome, idioma, tema e verificação de novas versões. Ficam no registro do Windows
/// (HKCU\Software\Meu Editor de Vídeo) e, no Linux e no macOS, em preferences.json na pasta de configurações.
/// Valor nulo = não armazenado.
/// </summary>
public sealed record UserPreferences(string? Name, string? Language, string? ThemeColor, bool? CheckForNewVersions, DateTimeOffset? LastCheck)
{
    /// <summary>Valores aceitos em ThemeColor.</summary>
    public static readonly IReadOnlyList<string> ThemeColors = ["Light", "Dark", "System"];

    /// <summary>
    /// Todas as preferências estão no registro e preenchidas. Senão, ao abrir, o programa pede as preferências na janela
    /// de boas-vindas.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name)
        && SupportedLanguages.IsSupported(Language)
        && ThemeColors.Contains(ThemeColor)
        && CheckForNewVersions is not null
        && LastCheck is not null;
}

public interface IUserPreferencesStore
{
    UserPreferences Read();

    void WriteName(string name);

    void WriteLanguage(string language);

    void WriteThemeColor(string theme);

    void WriteCheckForNewVersions(bool check);

    void WriteLastCheck(DateTimeOffset at);
}
