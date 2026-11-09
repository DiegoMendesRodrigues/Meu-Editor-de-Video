using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.AIEngine.Models;

/// <summary>Leitura e escrita do <c>manifest.json</c> dos pacotes <c>.medmodel</c> (camelCase, enums como texto).</summary>
public static class ModelManifestJson
{
    private static readonly Regex Sha256Pattern = new(@"^[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);

    /// <summary>Opções de serialização usadas por todo o gerenciador de modelos.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Lê um manifesto (UTF-8) de um fluxo. O fluxo não é fechado.</summary>
    /// <exception cref="InvalidDataException">O manifesto é inválido.</exception>
    public static ModelManifest Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Converte o texto JSON em manifesto e valida os campos obrigatórios.</summary>
    /// <exception cref="InvalidDataException">JSON malformado, capacidade desconhecida ou campo obrigatório ausente/inválido.</exception>
    public static ModelManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        ModelManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModelManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Invalid("JSON malformado, capacidade desconhecida ou campo obrigatório ausente."), ex);
        }

        if (manifest is null) throw new InvalidDataException(Invalid("o documento está vazio."));
        Validate(manifest);
        return manifest;
    }

    /// <summary>Serializa o manifesto no formato gravado no pacote.</summary>
    public static string Serialize(ModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, Options);
    }

    private static void Validate(ModelManifest m)
    {
        RequireText(m.Id, "id");
        RequireText(m.Name, "name");
        RequireText(m.Version, "version");
        RequireText(m.Source, "source");
        RequireText(m.Purpose, "purpose");
        RequireText(m.Format, "format");
        RequireText(m.File, "file");
        RequireText(m.License, "license");
        RequireText(m.Sha256, "sha256");

        if (!Sha256Pattern.IsMatch(m.Sha256))
            throw new InvalidDataException(Invalid("o campo 'sha256' deve conter 64 caracteres hexadecimais."));
        if (m.PackageFormat < 1)
            throw new InvalidDataException(Invalid("o campo 'packageFormat' deve ser 1 ou maior."));
        if (m.SizeBytes < 0)
            throw new InvalidDataException(Invalid("o campo 'sizeBytes' não pode ser negativo."));
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException(Invalid($"o campo obrigatório '{field}' está vazio."));
    }

    private static string Invalid(string detalhe) => $"O manifesto do modelo é inválido: {detalhe}";

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
}
