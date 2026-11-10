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
            if (Path.GetFileName(e.File) != e.File || Path.GetFileName(e.LicenseFile) != e.LicenseFile)
                throw new InvalidDataException($"Nome de arquivo inválido para '{e.Id}'.");
        }
        foreach (var e in entries)
            foreach (var dep in e.DependsOn)
                if (!ids.Contains(dep)) throw new InvalidDataException($"'{e.Id}' depende de '{dep}', que não está no catálogo.");
    }

    /// <summary>Compara versões ("3.1" &gt; "3.0.9"); texto não numérico é comparado como texto.</summary>
    public static int CompareVersions(string a, string b) =>
        Version.TryParse(Normalize(a), out var va) && Version.TryParse(Normalize(b), out var vb)
            ? va.CompareTo(vb)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string v) => v.Contains('.') ? v : v + ".0";
}
