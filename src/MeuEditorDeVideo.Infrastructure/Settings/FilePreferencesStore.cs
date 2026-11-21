using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Infrastructure.Settings;

/// <summary>
/// Preferências do usuário em <c>preferences.json</c> na pasta de dados do usuário (Linux e macOS, onde não há registro):
/// <c>Name</c>, <c>Language</c>, <c>ThemeColor</c>, <c>CheckForNewVersions</c> e <c>LastCheck</c> (ISO 8601).
/// Arquivo ausente ou corrompido equivale a nenhuma preferência salva. A gravação é atômica (temporário e substituição).
/// </summary>
public sealed class FilePreferencesStore(string filePath) : IUserPreferencesStore
{
    private readonly object _gate = new();

    public string FilePath { get; } = filePath;

    public UserPreferences Read()
    {
        lock (_gate)
        {
            var root = Load();
            bool? check = root["CheckForNewVersions"] is JsonValue c && c.TryGetValue<bool>(out var b) ? b : null;
            var last = Text(root, "LastCheck") is { } l &&
                DateTimeOffset.TryParse(l, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : (DateTimeOffset?)null;
            return new UserPreferences(Text(root, "Name"), Text(root, "Language"), Text(root, "ThemeColor"), check, last);
        }
    }

    public void WriteName(string name) => Write("Name", JsonValue.Create(name));

    public void WriteLanguage(string language) => Write("Language", JsonValue.Create(language));

    public void WriteThemeColor(string theme) => Write("ThemeColor", JsonValue.Create(theme));

    public void WriteCheckForNewVersions(bool check) => Write("CheckForNewVersions", JsonValue.Create(check));

    public void WriteLastCheck(DateTimeOffset at) => Write("LastCheck", JsonValue.Create(at.ToString("o", CultureInfo.InvariantCulture)));

    private static string? Text(JsonObject root, string name) =>
        root[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private JsonObject Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new JsonObject();
            return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }

    private void Write(string name, JsonNode? value)
    {
        lock (_gate)
        {
            var root = Load();
            root[name] = value;

            var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
    }
}
