using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Infrastructure.Export;

/// <summary>
/// Biblioteca de modelos de exportação (§10, plano §4.5): os embutidos ficam em JSON somente leitura
/// (templates\export ao lado do executável) e podem ser revisados sem alterar código; os do usuário — novos ou
/// personalizações de um embutido (mesmo Id) — ficam em %APPDATA%\MeuEditorDeVideo\templates.
/// </summary>
public sealed class JsonExportTemplateStore : IExportTemplateStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(), new FrameRateJsonConverter() },
    };

    private readonly ILogger<JsonExportTemplateStore> _logger;
    private readonly string _userDirectory;
    private readonly string? _builtInDirectory;
    private readonly Lock _gate = new();
    private IReadOnlyList<ExportTemplate>? _builtIn;

    public JsonExportTemplateStore(IAppPaths paths, ILogger<JsonExportTemplateStore> logger)
        : this(Path.Combine(paths.RoamingData, "templates"), LocateBuiltIn(), logger) { }

    public JsonExportTemplateStore(string userDirectory, string? builtInDirectory, ILogger<JsonExportTemplateStore> logger)
    {
        _userDirectory = userDirectory;
        _builtInDirectory = builtInDirectory;
        _logger = logger;
    }

    public IReadOnlyList<ExportTemplate> GetAll()
    {
        var builtIn = BuiltIn();
        var user = LoadDirectory(_userDirectory, isBuiltIn: false);
        var result = new List<ExportTemplate>();
        foreach (var b in builtIn)
            result.Add(user.FirstOrDefault(u => u.Id == b.Id) is { } custom ? custom with { IsBuiltIn = true } : b);
        result.AddRange(user.Where(u => builtIn.All(b => b.Id != u.Id)).OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase));
        return result;
    }

    public ExportTemplate? GetBuiltIn(string id) => BuiltIn().FirstOrDefault(t => t.Id == id);

    public async Task SaveAsync(ExportTemplate template, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_userDirectory);
        var path = UserPath(template.Id);
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(fs, ToDocument(template), JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = UserPath(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task RestoreDefaultsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var b in BuiltIn())
        {
            var path = UserPath(b.Id);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }

    /// <summary>Novo identificador para um modelo do usuário.</summary>
    public static string NewUserId() => "USR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private IReadOnlyList<ExportTemplate> BuiltIn()
    {
        lock (_gate)
            return _builtIn ??= _builtInDirectory is null ? [] : LoadDirectory(_builtInDirectory, isBuiltIn: true).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    private string UserPath(string id)
    {
        var safe = string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_userDirectory, safe + ".json");
    }

    private List<ExportTemplate> LoadDirectory(string dir, bool isBuiltIn)
    {
        var list = new List<ExportTemplate>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<TemplateDocument>(File.ReadAllText(file), JsonOptions);
                if (doc is null || string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.Name)) continue;
                list.Add(new ExportTemplate
                {
                    Id = doc.Id,
                    Name = doc.Name,
                    Description = doc.Description ?? "",
                    Settings = doc.Settings ?? new ExportSettings(),
                    IsBuiltIn = isBuiltIn,
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning(ex, "Modelo de exportação ignorado: {File}", Path.GetFileName(file));
            }
        }
        return list;
    }

    private static TemplateDocument ToDocument(ExportTemplate t) => new(t.Id, t.Name, t.Description, t.Settings);

    /// <summary>Pasta dos modelos embutidos: ao lado do executável ou, em desenvolvimento, resources\ do repositório.</summary>
    private static string? LocateBuiltIn()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "templates", "export");
        if (Directory.Exists(local)) return local;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "resources", "templates", "export");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private sealed record TemplateDocument(string Id, string Name, string? Description, ExportSettings? Settings);
}

/// <summary>Taxa de quadros em JSON como texto legível: "30", "29.97" é rejeitado em favor de "30000/1001".</summary>
public sealed class FrameRateJsonConverter : JsonConverter<FrameRate>
{
    public override FrameRate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetInt32(out var whole) ? new FrameRate(whole, 1)
                : throw new JsonException("Taxa de quadros fracionária deve ser escrita como fração (ex.: \"30000/1001\").");
        var text = reader.GetString() ?? throw new JsonException("Taxa de quadros vazia.");
        var parts = text.Split('/');
        if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return new FrameRate(n, 1);
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var den) && den > 0)
            return new FrameRate(num, den);
        throw new JsonException($"Taxa de quadros inválida: \"{text}\".");
    }

    public override void Write(Utf8JsonWriter writer, FrameRate value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Denominator == 1
            ? value.Numerator.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{value.Numerator}/{value.Denominator}"));
}
