// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Globalization;
using MeuEditorDeVideo.Core.Abstractions;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MeuEditorDeVideo.Infrastructure.Settings;

/// <summary>
/// Preferências do usuário em <c>HKEY_CURRENT_USER\Software\Meu Editor de Vídeo</c> (a mesma chave do instalador):
/// <c>Name</c>, <c>Language</c> (pt-BR, pt-PT, en, es, fr, de), <c>ThemeColor</c> (Dark, Light, System),
/// <c>CheckForNewVersions</c> (DWORD: 1 = Sim, 0 = Não) e <c>LastCheck</c> (data e hora ISO 8601).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryPreferencesStore(string keyPath = RegistryPreferencesStore.DefaultKey) : IUserPreferencesStore
{
    public const string DefaultKey = @"Software\Meu Editor de Vídeo";

    public string KeyPath { get; } = keyPath;

    public UserPreferences Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null) return new UserPreferences(null, null, null, null, null);
        var check = key.GetValue("CheckForNewVersions") switch
        {
            int i => i != 0,
            string s when bool.TryParse(s, out var b) => b,
            string s => s.Trim() is "1" or "Sim" or "Yes",
            _ => (bool?)null,
        };
        var last = key.GetValue("LastCheck") is string l &&
            DateTimeOffset.TryParse(l, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : (DateTimeOffset?)null;
        return new UserPreferences(key.GetValue("Name") as string, key.GetValue("Language") as string, key.GetValue("ThemeColor") as string, check, last);
    }

    public void WriteName(string name) => Write("Name", name, RegistryValueKind.String);

    public void WriteLanguage(string language) => Write("Language", language, RegistryValueKind.String);

    public void WriteThemeColor(string theme) => Write("ThemeColor", theme, RegistryValueKind.String);

    public void WriteCheckForNewVersions(bool check) => Write("CheckForNewVersions", check ? 1 : 0, RegistryValueKind.DWord);

    public void WriteLastCheck(DateTimeOffset at) => Write("LastCheck", at.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);

    private void Write(string name, object value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value, kind);
    }
}
