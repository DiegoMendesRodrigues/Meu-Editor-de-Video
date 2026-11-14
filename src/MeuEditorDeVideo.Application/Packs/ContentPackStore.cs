using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Application.Packs;

/// <summary>
/// Pacotes de conteúdo em <c>%LOCALAPPDATA%\MeuEditorDeVideo\Pacotes\&lt;id&gt;</c>. A instalação só extrai as pastas conhecidas,
/// com extensões permitidas, nomes sem caminhos e limite de tamanho; nada é executado.
/// </summary>
public sealed class ContentPackStore(IAppPaths paths, IDomainJson json) : IContentPackStore
{
    public const string UserPackId = "meus-itens";
    public const string StickersFolder = "figurinhas";
    public const string TitlesFolder = "titulos";
    public const string TemplatesFolder = "modelos";
    public const string TitleExtension = ".medtitle";
    private const string ManifestName = "pacote.json";
    private const long MaxEntryBytes = 200L * 1024 * 1024;
    private const long MaxTotalBytes = 1024L * 1024 * 1024;

    public static readonly string[] StickerExtensions = [".png", ".gif", ".webp", ".jpg", ".jpeg", ".svg"];

    private string Root => Path.Combine(paths.LocalData, "Pacotes");

    /// <summary>Pacotes que vêm com o programa (somente leitura). Um pacote instalado com o mesmo id tem precedência.</summary>
    public string BuiltInRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "pacotes");

    private static IEnumerable<string> Dirs(string root) =>
        Directory.Exists(root) ? Directory.EnumerateDirectories(root).Where(d => !Path.GetFileName(d).Contains(".tmp-", StringComparison.Ordinal)) : [];

    public IReadOnlyList<ContentPackInfo> Packs()
    {
        var installed = Dirs(Root).Select(d => Info(d, builtIn: false)).ToList();
        var ids = installed.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtIn = Dirs(BuiltInRoot).Select(d => Info(d, builtIn: true)).Where(p => !ids.Contains(p.Id));
        return [.. installed.Concat(builtIn).OrderBy(p => p.IsUserPack ? 0 : p.IsBuiltIn ? 1 : 2).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static ContentPackInfo Info(string folder, bool builtIn)
    {
        var id = Path.GetFileName(folder);
        return new ContentPackInfo(id, ReadName(folder) ?? id, folder,
            Files(folder, StickersFolder, StickerExtensions).Count(),
            Files(folder, TitlesFolder, [TitleExtension]).Count(),
            Files(folder, TemplatesFolder, [".med"]).Count())
        {
            IsUserPack = id == UserPackId,
            IsBuiltIn = builtIn,
        };
    }

    private static string? ReadName(string folder)
    {
        var manifest = Path.Combine(folder, ManifestName);
        if (!File.Exists(manifest)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(manifest))?["nome"]?.GetValue<string>() is { Length: > 0 } n ? n.Trim() : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException) { return null; }
    }

    private static IEnumerable<string> Files(string folder, string sub, string[] extensions)
    {
        var dir = Path.Combine(folder, sub);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order(StringComparer.CurrentCultureIgnoreCase)
            : [];
    }

    private IReadOnlyList<ContentPackItem> Items(string sub, string[] extensions) =>
        [.. Packs().SelectMany(p => Files(p.Folder, sub, extensions).Select(f => new ContentPackItem(p.Id, p.Name, Path.GetFileNameWithoutExtension(f), f)))];

    public IReadOnlyList<ContentPackItem> Stickers() => Items(StickersFolder, StickerExtensions);

    public IReadOnlyList<ContentPackItem> Titles() => Items(TitlesFolder, [TitleExtension]);

    public IReadOnlyList<ContentPackItem> Templates() => Items(TemplatesFolder, [".med"]);

    public ContentPackInfo Install(string packagePath)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(packagePath); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("O arquivo não é um pacote válido (.medpack ou .zip).", ex);
        }
        using (zip)
        {
            var entries = new List<(ZipArchiveEntry Entry, string Folder, string Name)>();
            string? name = null;
            long total = 0;
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                var parts = e.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                // Aceita o conteúdo na raiz ou dentro de uma única pasta (ZIP feito de uma pasta).
                if (parts.Length >= 1 && parts[^1].Equals(ManifestName, StringComparison.OrdinalIgnoreCase) && parts.Length <= 2)
                {
                    using var r = new StreamReader(e.Open());
                    try { name = JsonNode.Parse(r.ReadToEnd())?["nome"]?.GetValue<string>()?.Trim(); }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
                    continue;
                }
                if (parts.Length < 2) continue;
                var folder = parts[^2].ToLowerInvariant();
                var file = parts[^1];
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var ok = folder switch
                {
                    StickersFolder => StickerExtensions.Contains(ext),
                    TitlesFolder => ext == TitleExtension,
                    TemplatesFolder => ext == ".med",
                    _ => false,
                };
                if (!ok || file != Path.GetFileName(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                if (e.Length > MaxEntryBytes || (total += e.Length) > MaxTotalBytes)
                    throw new InvalidDataException("O pacote é grande demais (limite de 1 GB, 200 MB por arquivo).");
                entries.Add((e, folder, file));
            }
            if (entries.Count == 0)
                throw new InvalidDataException("O pacote não tem figurinhas, títulos nem modelos (pastas figurinhas, titulos e modelos).");

            name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(packagePath) : name;
            var id = Slug(name);
            if (id == UserPackId) id += "-importado";
            var target = Path.Combine(Root, id);
            var temp = target + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var (entry, folder, file) in entries)
                {
                    var dir = Path.Combine(temp, folder);
                    Directory.CreateDirectory(dir);
                    entry.ExtractToFile(Path.Combine(dir, file), overwrite: true);
                }
                File.WriteAllText(Path.Combine(temp, ManifestName), new JsonObject { ["nome"] = name }.ToJsonString());
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(temp, target);
            }
            catch
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
                throw;
            }
            return Info(target, builtIn: false);
        }
    }

    public void Export(string packId, string destinationPath)
    {
        var folder = Folder(packId);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        var manifest = Path.Combine(folder, ManifestName);
        zip.CreateEntryFromFile(File.Exists(manifest) ? manifest : WriteTempManifest(folder), ManifestName);
        foreach (var (sub, exts) in new[] { (StickersFolder, StickerExtensions), (TitlesFolder, new[] { TitleExtension }), (TemplatesFolder, new[] { ".med" }) })
            foreach (var f in Files(folder, sub, exts))
                zip.CreateEntryFromFile(f, sub + "/" + Path.GetFileName(f));
    }

    private static string WriteTempManifest(string folder)
    {
        var path = Path.Combine(folder, ManifestName);
        File.WriteAllText(path, new JsonObject { ["nome"] = Path.GetFileName(folder) == UserPackId ? "Meus itens" : Path.GetFileName(folder) }.ToJsonString());
        return path;
    }

    public void Remove(string packId)
    {
        var folder = Folder(packId);
        if (!folder.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Este pacote vem com o programa e não pode ser removido.");
        Directory.Delete(folder, recursive: true);
    }

    private string Folder(string packId)
    {
        if (Slug(packId) != packId) throw new DirectoryNotFoundException("Pacote não encontrado.");
        foreach (var root in new[] { Root, BuiltInRoot })
        {
            var folder = Path.Combine(root, packId);
            if (Directory.Exists(folder)) return folder;
        }
        throw new DirectoryNotFoundException("Pacote não encontrado.");
    }

    private string UserFolder(string sub)
    {
        var pack = Path.Combine(Root, UserPackId);
        var dir = Path.Combine(pack, sub);
        Directory.CreateDirectory(dir);
        var manifest = Path.Combine(pack, ManifestName);
        if (!File.Exists(manifest)) File.WriteAllText(manifest, new JsonObject { ["nome"] = "Meus itens" }.ToJsonString());
        return dir;
    }

    public ContentPackItem SaveTitle(Clip textClip, string name)
    {
        if (!textClip.IsText) throw new InvalidOperationException("Só clipes de texto podem virar títulos.");
        var clip = textClip with { Start = MediaTime.Zero, LinkId = null, GroupId = null, MotionLink = null, Trackers = [] };
        var path = Unique(Path.Combine(UserFolder(TitlesFolder), SafeFileName(name) + TitleExtension));
        File.WriteAllText(path, json.Serialize(clip));
        return new ContentPackItem(UserPackId, "Meus itens", Path.GetFileNameWithoutExtension(path), path);
    }

    public Clip LoadTitle(string path)
    {
        Clip? clip;
        try { clip = json.Deserialize<Clip>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("O título está corrompido ou é de uma versão incompatível.", ex);
        }
        if (clip is null || !clip.IsText || clip.Duration <= MediaTime.Zero) throw new InvalidDataException("O arquivo não contém um título.");
        return clip with { Id = Guid.NewGuid(), Start = MediaTime.Zero };
    }

    public string NewTemplatePath(string name) => Unique(Path.Combine(UserFolder(TemplatesFolder), SafeFileName(name) + ".med"));

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    private static string SafeFileName(string name)
    {
        var s = string.Concat((name ?? "").Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return s.Length == 0 ? "Sem nome" : s.Length > 80 ? s[..80] : s;
    }

    /// <summary>Identificador do pacote (nome da pasta): minúsculas, letras, números e hífens.</summary>
    public static string Slug(string name)
    {
        var norm = name.Normalize(System.Text.NormalizationForm.FormD);
        var chars = norm.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        var slug = string.Join('-', new string([.. chars]).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "pacote" : slug.Length > 60 ? slug[..60].Trim('-') : slug;
    }
}
