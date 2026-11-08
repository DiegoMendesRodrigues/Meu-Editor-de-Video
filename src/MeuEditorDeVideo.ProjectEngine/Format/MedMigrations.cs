using System.Globalization;
using System.Text.Json.Nodes;

namespace MeuEditorDeVideo.ProjectEngine.Format;

/// <summary>
/// Migração entre versões do formato (§9.5). Opera sobre o JSON bruto, antes da desserialização,
/// para não depender das classes atuais do domínio.
/// </summary>
public interface IMedMigration
{
    MedVersion From { get; }
    MedVersion To { get; }

    void Migrate(JsonObject project);
}

public readonly record struct MedVersion(int Major, int Minor) : IComparable<MedVersion>
{
    public static MedVersion Parse(string? text)
    {
        var parts = (text ?? "").Split('.');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            throw new FormatException($"Versão de formato inválida: '{text}'.");
        return new MedVersion(major, minor);
    }

    public int CompareTo(MedVersion other) => (Major, Minor).CompareTo((other.Major, other.Minor));

    public static bool operator <(MedVersion a, MedVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(MedVersion a, MedVersion b) => a.CompareTo(b) > 0;

    public override string ToString() => MedJson.FormatVersion(Major, Minor);
}

/// <summary>Cadeia de migrações registradas. Versões futuras acrescentam itens aqui.</summary>
public sealed class MedMigrator(IEnumerable<IMedMigration> migrations)
{
    /// <summary>Versão gravada por este aplicativo.</summary>
    public static readonly MedVersion Current = new(1, 0);

    public static MedMigrator Default { get; } = new([]);

    private readonly IReadOnlyList<IMedMigration> _migrations = migrations.OrderBy(m => m.From).ToList();

    /// <summary>
    /// Aplica migrações em sequência até <see cref="Current"/>. Retorna true se alguma foi aplicada.
    /// Versão de major superior → não suportada (lança). Minor superior → lida com aviso (campos novos são ignorados).
    /// </summary>
    public bool Migrate(JsonObject project, MedVersion fileVersion, out IReadOnlyList<string> warnings)
    {
        var w = new List<string>();
        warnings = w;

        if (fileVersion.Major > Current.Major) throw new NotSupportedException();
        if (fileVersion > Current)
        {
            w.Add($"O projeto usa o formato {fileVersion}, mais novo que o desta versão ({Current}). " +
                  "Recursos desconhecidos serão ignorados e podem ser perdidos ao salvar.");
            return false;
        }

        var version = fileVersion;
        var migrated = false;
        while (version < Current)
        {
            var step = _migrations.FirstOrDefault(m => m.From == version)
                       ?? throw new InvalidOperationException($"Não há migração registrada a partir do formato {version}.");
            step.Migrate(project);
            version = step.To;
            migrated = true;
        }

        if (migrated) w.Add($"O projeto foi convertido do formato {fileVersion} para {Current}. Uma cópia de segurança do original foi criada ao salvar.");
        return migrated;
    }
}
