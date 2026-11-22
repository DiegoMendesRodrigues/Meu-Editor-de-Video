using System.Text.Json;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.AIEngine.Models;

/// <summary>
/// Catálogo oficial de modelos (<c>catalogo-ia\modelos.json</c> e <c>catalogo-ia\licencas\</c> ao lado do executável):
/// leitura, validação e ordem de instalação com dependências.
/// </summary>
public sealed class ModelCatalog
{
    /// <summary>Hosts das origens oficiais (o download só começa nesses endereços, sempre por HTTPS).</summary>
    public static IReadOnlyList<string> AllowedHosts { get; } = ["huggingface.co", "github.com"];

    private static readonly string[] ValidPlatforms = ["windows", "linux", "macos"];

    public ModelCatalog(IReadOnlyList<ModelCatalogEntry> entries, string licensesDirectory)
    {
        Entries = entries;
        LicensesDirectory = licensesDirectory;
        Validate(entries);
    }

    /// <summary>Pasta do catálogo distribuída com o aplicativo.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "catalogo-ia");

    public IReadOnlyList<ModelCatalogEntry> Entries { get; }
    public string LicensesDirectory { get; }

    /// <summary>Entradas oferecidas no sistema <paramref name="os"/> (as sem <c>plataformas</c> valem para todos).</summary>
    public IReadOnlyList<ModelCatalogEntry> EntriesFor(AiOs os) =>
        Entries.Where(e => e.AvailableOn(AiPlatform.Tag(os))).ToList();

    /// <summary>Entradas oferecidas no sistema em que o programa roda.</summary>
    public IReadOnlyList<ModelCatalogEntry> EntriesForCurrentOs => EntriesFor(AiPlatform.CurrentOs);

    public ModelCatalogEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>Lê <c>modelos.json</c> de <paramref name="directory"/>; catálogo vazio se a pasta não existir.</summary>
    /// <exception cref="InvalidDataException">O catálogo está malformado.</exception>
    public static ModelCatalog Load(string? directory = null)
    {
        directory ??= DefaultDirectory;
        var file = Path.Combine(directory, "modelos.json");
        if (!File.Exists(file)) return new ModelCatalog([], Path.Combine(directory, "licencas"));
        List<ModelCatalogEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ModelCatalogEntry>>(File.ReadAllText(file), ModelManifestJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("O catálogo de modelos está malformado: " + ex.Message, ex);
        }
        return new ModelCatalog(entries ?? [], Path.Combine(directory, "licencas"));
    }

    public string LicensePath(ModelCatalogEntry entry) => Path.Combine(LicensesDirectory, entry.LicenseFile);

    /// <summary>
    /// Ordem de instalação: as dependências (recursivamente) antes de quem depende delas, sem repetições, na ordem
    /// do catálogo quando não há relação.
    /// </summary>
    public IReadOnlyList<ModelCatalogEntry> InstallOrder(IEnumerable<string> ids)
    {
        var order = new List<ModelCatalogEntry>();
        var visiting = new HashSet<string>();
        void Visit(string id)
        {
            if (order.Any(e => e.Id == id)) return;
            var entry = Find(id) ?? throw new InvalidOperationException($"Modelo '{id}' não existe no catálogo.");
            if (!visiting.Add(id)) throw new InvalidDataException($"Dependência circular no catálogo envolvendo '{id}'.");
            foreach (var dep in entry.DependsOn) Visit(dep);
            visiting.Remove(id);
            order.Add(entry);
        }
        foreach (var id in ids) Visit(id);
        return order;
    }

    private static void Validate(IReadOnlyList<ModelCatalogEntry> entries)
    {
        var ids = new HashSet<string>();
        foreach (var e in entries)
        {
            if (!ids.Add(e.Id)) throw new InvalidDataException($"O catálogo repete o modelo '{e.Id}'.");
            if (e.Url.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(e.Url.Host, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Endereço não permitido para '{e.Id}': {e.Url}. Use HTTPS em {string.Join(" ou ", AllowedHosts)}.");
            if (e.Sha256.Length != 64 || !e.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"SHA-256 inválido para '{e.Id}'.");
            if (e.SizeBytes <= 0) throw new InvalidDataException($"Tamanho inválido para '{e.Id}'.");
            foreach (var p in e.Plataformas)
                if (!ValidPlatforms.Contains(p, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Plataforma inválida para '{e.Id}': '{p}'. Use {string.Join(", ", ValidPlatforms)}.");
            if (!ModelRegistry.IsPlainFileName(e.File) || !ModelRegistry.IsPlainFileName(e.LicenseFile))
                throw new InvalidDataException($"Nome de arquivo inválido para '{e.Id}'.");
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { e.File };
            foreach (var f in e.ExtraFiles)
            {
                if (f.Url.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(f.Url.Host, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Endereço não permitido para '{e.Id}': {f.Url}. Use HTTPS em {string.Join(" ou ", AllowedHosts)}.");
                if (f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"SHA-256 inválido para '{e.Id}' ({f.File}).");
                if (f.SizeBytes <= 0) throw new InvalidDataException($"Tamanho inválido para '{e.Id}' ({f.File}).");
                if (string.IsNullOrWhiteSpace(f.File) || !ModelRegistry.IsPlainFileName(f.File) || !files.Add(f.File)
                    || f.File.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || f.File.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase)
                    || f.File.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Nome de arquivo inválido ou repetido para '{e.Id}': {f.File}.");
            }
        }
        foreach (var e in entries)
            foreach (var dep in e.DependsOn)
            {
                if (!ids.Contains(dep)) throw new InvalidDataException($"'{e.Id}' depende de '{dep}', que não está no catálogo.");
                var d = entries.First(x => x.Id == dep);
                foreach (var os in ValidPlatforms.Where(os => e.AvailableOn(os) && !d.AvailableOn(os)))
                    throw new InvalidDataException($"'{e.Id}' depende de '{dep}', que não existe para {os}.");
            }
    }

    /// <summary>Compara versões ("3.1" &gt; "3.0.9"); texto não numérico é comparado como texto.</summary>
    public static int CompareVersions(string a, string b) =>
        Version.TryParse(Normalize(a), out var va) && Version.TryParse(Normalize(b), out var vb)
            ? va.CompareTo(vb)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string v) => v.Contains('.') ? v : v + ".0";
}
