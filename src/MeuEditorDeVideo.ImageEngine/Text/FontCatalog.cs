using System.Collections.Concurrent;
using System.IO;
using System.Windows.Markup;
using System.Windows.Media;

namespace MeuEditorDeVideo.ImageEngine.Text;

/// <summary>
/// Fontes disponíveis para textos (RF136): as do sistema e as adicionadas pelo usuário numa pasta própria
/// (arquivos .ttf/.otf/.ttc), sem precisar instalá-las no Windows.
/// </summary>
public static class FontCatalog
{
    private static readonly XmlLanguage English = XmlLanguage.GetLanguage("en-us");
    private static readonly ConcurrentDictionary<string, FontFamily> Resolved = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();
    private static Dictionary<string, FontFamily>? _userFamilies;
    private static string? _userFolder;

    public static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc"];

    /// <summary>Pasta das fontes do usuário (configurada na inicialização do aplicativo).</summary>
    public static string? UserFontsFolder
    {
        get => _userFolder;
        set
        {
            _userFolder = value;
            Refresh();
        }
    }

    /// <summary>Relê a pasta de fontes do usuário (após adicionar uma fonte).</summary>
    public static void Refresh()
    {
        lock (Gate) _userFamilies = null;
        Resolved.Clear();
    }

    private static Dictionary<string, FontFamily> UserFamilies()
    {
        lock (Gate)
        {
            if (_userFamilies is not null) return _userFamilies;
            var result = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            if (_userFolder is { } folder && Directory.Exists(folder))
            {
                try
                {
                    var baseUri = new Uri(Path.GetFullPath(folder).TrimEnd('\\') + "\\");
                    foreach (var family in Fonts.GetFontFamilies(baseUri))
                    {
                        var name = NameOf(family);
                        if (name.Length > 0) result.TryAdd(name, new FontFamily(baseUri, "./#" + name));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
                {
                    // Pasta inacessível: ficam só as fontes do sistema.
                }
            }
            return _userFamilies = result;
        }
    }

    /// <summary>Nome da família (em inglês quando houver, como o Windows mostra nos aplicativos).</summary>
    public static string NameOf(FontFamily family)
    {
        if (family.FamilyNames.TryGetValue(English, out var en)) return en;
        var first = family.FamilyNames.Values.FirstOrDefault();
        if (first is not null) return first;
        var source = family.Source;
        var hash = source.LastIndexOf('#');
        return hash >= 0 ? source[(hash + 1)..] : source;
    }

    /// <summary>Famílias disponíveis, em ordem alfabética (fontes do usuário incluídas).</summary>
    public static IReadOnlyList<string> Families()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Fonts.SystemFontFamilies) names.Add(NameOf(f));
        foreach (var name in UserFamilies().Keys) names.Add(name);
        return [.. names];
    }

    /// <summary>A fonte é do usuário (pasta própria) e não do sistema?</summary>
    public static bool IsUserFont(string family) => UserFamilies().ContainsKey(family);

    /// <summary>Família pelo nome; nomes desconhecidos caem na fonte padrão do sistema (Segoe UI).</summary>
    public static FontFamily Resolve(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Segoe UI" : name.Trim();
        return Resolved.GetOrAdd(name, n => UserFamilies().TryGetValue(n, out var user) ? user : new FontFamily(n + ", Segoe UI"));
    }
}
