// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Concurrent;
using System.IO;
using SkiaSharp;

namespace MeuEditorDeVideo.ImageEngine.Text;

/// <summary>Fonte escolhida para um estilo: a face e os efeitos sintéticos quando a família não tem negrito/itálico.</summary>
public sealed record ResolvedFont(SKTypeface Typeface, bool FakeBold, bool FakeItalic)
{
    public string FamilyName => Typeface.FamilyName;
}

/// <summary>
/// Fontes disponíveis para textos (RF136): as do sistema e as adicionadas pelo usuário numa pasta própria
/// (arquivos .ttf/.otf/.ttc), sem precisar instalá-las no sistema. Usa o gerenciador de fontes do Skia
/// (DirectWrite no Windows, Fontconfig no Linux, CoreText no macOS).
/// </summary>
public static class FontCatalog
{
    /// <summary>Famílias tentadas, em ordem, quando a pedida não existe (a primeira é o padrão dos projetos).</summary>
    private static readonly string[] DefaultFamilies = ["Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Helvetica Neue", "Arial"];

    private static readonly ConcurrentDictionary<(string Name, bool Bold, bool Italic), ResolvedFont> Resolved = new();
    private static readonly Lock Gate = new();
    private static Dictionary<string, List<SKTypeface>>? _userFamilies;
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

    private static Dictionary<string, List<SKTypeface>> UserFamilies()
    {
        lock (Gate)
        {
            if (_userFamilies is not null) return _userFamilies;
            var result = new Dictionary<string, List<SKTypeface>>(StringComparer.OrdinalIgnoreCase);
            if (_userFolder is { } folder && Directory.Exists(folder))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!FontExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                        // .ttc: várias faces no mesmo arquivo, lidas pelo índice até acabar.
                        for (var index = 0; index < 64; index++)
                        {
                            var face = SKTypeface.FromFile(file, index);
                            if (face is null) break;
                            if (face.FamilyName is { Length: > 0 } name)
                            {
                                if (!result.TryGetValue(name, out var faces)) result[name] = faces = [];
                                faces.Add(face);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // Pasta inacessível: ficam só as fontes do sistema.
                }
            }
            return _userFamilies = result;
        }
    }

    /// <summary>Famílias disponíveis, em ordem alfabética (fontes do usuário incluídas).</summary>
    public static IReadOnlyList<string> Families()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in SKFontManager.Default.GetFontFamilies())
        {
            if (!string.IsNullOrWhiteSpace(f)) names.Add(f);
        }
        foreach (var name in UserFamilies().Keys) names.Add(name);
        return [.. names];
    }

    /// <summary>A fonte é do usuário (pasta própria) e não do sistema?</summary>
    public static bool IsUserFont(string family) => UserFamilies().ContainsKey(family);

    /// <summary>Fonte pelo nome e estilo; nomes desconhecidos caem na fonte padrão disponível no sistema.</summary>
    public static ResolvedFont Resolve(string? name, bool bold, bool italic)
    {
        name = string.IsNullOrWhiteSpace(name) ? DefaultFamilies[0] : name.Trim();
        return Resolved.GetOrAdd((name, bold, italic), k => ResolveCore(k.Name, k.Bold, k.Italic));
    }

    private static ResolvedFont ResolveCore(string name, bool bold, bool italic)
    {
        var style = new SKFontStyle(bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var face = UserFamilies().TryGetValue(name, out var faces) ? BestMatch(faces, style) : null;
        face ??= SKFontManager.Default.MatchFamily(name, style);
        foreach (var fallback in DefaultFamilies)
        {
            if (face is not null) break;
            face = SKFontManager.Default.MatchFamily(fallback, style);
        }
        face ??= SKTypeface.Default;
        return new ResolvedFont(face, bold && face.FontWeight < 600, italic && face.FontSlant == SKFontStyleSlant.Upright);
    }

    private static SKTypeface BestMatch(List<SKTypeface> faces, SKFontStyle style) =>
        faces.MinBy(f => Math.Abs(f.FontWeight - style.Weight) + ((f.FontSlant == SKFontStyleSlant.Upright) == (style.Slant == SKFontStyleSlant.Upright) ? 0 : 1000))!;

    /// <summary>
    /// Face que desenha <paramref name="codepoint"/>: a própria ou uma substituta do sistema (emoji, alfabetos que a
    /// fonte não cobre), como o WPF fazia automaticamente.
    /// </summary>
    internal static SKTypeface ForCodepoint(ResolvedFont font, int codepoint)
    {
        if (codepoint < 0x20 || Probe(font.Typeface).ContainsGlyph(codepoint)) return font.Typeface;
        return FallbackCache.GetOrAdd((font.Typeface, codepoint), k =>
            SKFontManager.Default.MatchCharacter(k.Face.FamilyName, k.Face.FontStyle, [], k.Codepoint) ?? k.Face);
    }

    private static readonly ConcurrentDictionary<(SKTypeface Face, int Codepoint), SKTypeface> FallbackCache = new();
    private static readonly ConcurrentDictionary<SKTypeface, SKFont> Probes = new();

    /// <summary>Fonte usada só para consultar a cobertura de caracteres da face.</summary>
    private static SKFont Probe(SKTypeface face) => Probes.GetOrAdd(face, f => new SKFont(f));
}
