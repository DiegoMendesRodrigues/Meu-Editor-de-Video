using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.AIEngine.Models;

/// <summary>
/// Gerenciador de modelos instalados em <c>&lt;modelsDirectory&gt;\&lt;id&gt;\</c>. Instala pacotes <c>.medmodel</c> (zip
/// locais) validando manifesto, compatibilidade, caminhos e SHA-256 antes de copiar. Nunca baixa nada.
/// Thread-safe: consultas podem ser feitas de qualquer thread; <see cref="Changed"/> é disparado a partir da thread
/// de segundo plano que concluiu a operação (assine com despacho para a UI, se necessário).
/// </summary>
public sealed class ModelRegistry : IModelRegistry
{
    /// <summary>Nome do manifesto dentro do pacote e da pasta instalada.</summary>
    public const string ManifestFileName = "manifest.json";

    private const string NotAPackageMessage = "O arquivo não é um pacote de modelo válido (.medmodel).";
    private const int CopyBufferSize = 81920;

    // Id: 2 a 64 caracteres; \z em vez de $ para não aceitar quebra de linha final.
    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9._-]{1,63}\z", RegexOptions.CultureInvariant);

    private readonly string _root;
    private readonly ILogger<ModelRegistry> _logger;
    private readonly object _gate = new();     // protege a lista em memória
    private readonly object _ioGate = new();   // serializa instalações e remoções
    private List<InstalledModel> _installed;

    /// <param name="modelsDirectory">Pasta raiz dos modelos instalados (criada se não existir).</param>
    /// <param name="logger">Logger opcional.</param>
    public ModelRegistry(string modelsDirectory, ILogger<ModelRegistry>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelsDirectory);
        _root = Path.GetFullPath(modelsDirectory);
        _logger = logger ?? NullLogger<ModelRegistry>.Instance;
        Directory.CreateDirectory(_root);
        _installed = Scan();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<InstalledModel> Installed
    {
        get
        {
            lock (_gate) return _installed.ToArray();
        }
    }

    /// <inheritdoc />
    public InstalledModel? Find(ModelCapability capability)
    {
        lock (_gate)
        {
            InstalledModel? best = null;
            foreach (var model in _installed)
            {
                if (model.Manifest.Capability != capability) continue;
                if (best is null || CompareVersions(model.Manifest.Version, best.Manifest.Version) > 0) best = model;
            }
            return best;
        }
    }

    /// <inheritdoc />
    public bool IsAvailable(ModelCapability capability) => Find(capability) is { } model && File.Exists(model.ModelPath);

    /// <inheritdoc />
    public Task<InstalledModel> InstallAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        return Task.Run(() => Install(packagePath, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        return Task.Run(() => Remove(id, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(string id, CancellationToken cancellationToken = default)
    {
        InstalledModel? model;
        lock (_gate) model = _installed.FirstOrDefault(m => string.Equals(m.Manifest.Id, id, StringComparison.Ordinal));
        if (model is null || !File.Exists(model.ModelPath)) return false;

        await using var stream = new FileStream(model.ModelPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), model.Manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Refresh()
    {
        SetInstalled(Scan());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetInstalled(List<InstalledModel> models)
    {
        lock (_gate) _installed = models;
    }

    private List<InstalledModel> Scan()
    {
        var models = new List<InstalledModel>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue; // pastas temporárias de instalação e ocultas

            var manifestPath = Path.Combine(dir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning("Pasta de modelo ignorada: não há {Manifesto} em {Pasta}.", ManifestFileName, dir);
                continue;
            }

            try
            {
                var manifest = ModelManifestJson.Parse(File.ReadAllText(manifestPath));
                if (!string.Equals(manifest.Id, name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Pasta de modelo ignorada: o Id '{Id}' não corresponde ao nome da pasta {Pasta}.", manifest.Id, dir);
                    continue;
                }
                models.Add(new InstalledModel(manifest, dir));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Pasta de modelo ignorada: manifesto inválido em {Pasta}.", dir);
            }
        }
        return models;
    }

    private InstalledModel Install(string packagePath, CancellationToken ct)
    {
        InstalledModel model;
        lock (_ioGate)
        {
            model = InstallLocked(packagePath, ct);
        }
        _logger.LogInformation("Modelo {Id} instalado.", model.Manifest.Id);
        Changed?.Invoke(this, EventArgs.Empty);
        return model;
    }

    private InstalledModel InstallLocked(string packagePath, CancellationToken ct)
    {
        if (!File.Exists(packagePath)) throw new InvalidDataException(NotAPackageMessage);

        using var zip = OpenPackage(packagePath);
        EnsureSafeEntries(zip);

        var manifestEntry = zip.GetEntry(ManifestFileName)
            ?? throw new InvalidDataException("O pacote não contém o arquivo manifest.json na raiz.");
        var manifest = ReadManifest(manifestEntry);

        if (!IdPattern.IsMatch(manifest.Id))
            throw new InvalidDataException($"O identificador do modelo é inválido ('{manifest.Id}'): use de 2 a 64 caracteres minúsculos, números, '.', '_' ou '-', começando por letra ou número.");
        if (manifest.PackageFormat > IModelRegistry.SupportedPackageFormat)
            throw new InvalidDataException("Este modelo exige uma versão mais nova do Meu Editor de Vídeo.");

        EnsureSimpleFileName(manifest.File, "file");
        var modelEntry = RequireEntry(zip, manifest.File, $"O arquivo do modelo '{manifest.File}' declarado no manifesto não está no pacote.");
        if (manifest.LicenseFile is { } licenseFile)
        {
            EnsureSimpleFileName(licenseFile, "licenseFile");
            RequireEntry(zip, licenseFile, $"O arquivo de licença '{licenseFile}' declarado no manifesto não está no pacote.");
        }
        if (manifest.Documentation is { } documentation)
        {
            EnsureSimpleFileName(documentation, "documentation");
            RequireEntry(zip, documentation, $"O arquivo de documentação '{documentation}' declarado no manifesto não está no pacote.");
        }

        var staging = Path.Combine(_root, ".install-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(_root, manifest.Id);
        Directory.CreateDirectory(staging);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long modelSize = 0;
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var destination = ResolveInside(staging, entry.FullName);
                if (IsDirectoryEntry(entry))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                if (ReferenceEquals(entry, modelEntry))
                    modelSize = ExtractEntry(entry, destination, hash, ct);
                else
                    ExtractEntry(entry, destination, null, ct);
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O arquivo do modelo está corrompido: o hash SHA-256 não confere com o manifesto.");
            if (manifest.SizeBytes > 0 && modelSize != manifest.SizeBytes)
                throw new InvalidDataException("O arquivo do modelo está corrompido: o tamanho não confere com o manifesto.");

            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }

        SetInstalled(Scan());
        return new InstalledModel(manifest, target);
    }

    private static ZipArchive OpenPackage(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(NotAPackageMessage);
        }
    }

    private static void EnsureSafeEntries(ZipArchive zip)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (!IsSafeEntryName(name))
                throw new InvalidDataException($"O pacote contém um caminho de arquivo inválido ('{name}'). A instalação foi cancelada.");
            if (!names.Add(name.TrimEnd('/', '\\')))
                throw new InvalidDataException($"O pacote contém entradas duplicadas ('{name}').");
        }
    }

    private static bool IsSafeEntryName(string name)
    {
        if (name.Length == 0) return false;
        if (name.StartsWith('/') || name.StartsWith('\\') || Path.IsPathRooted(name) || name.Contains(':')) return false;
        foreach (var segment in name.Split('/', '\\'))
        {
            if (segment == "..") return false;
        }
        return true;
    }

    private static void EnsureSimpleFileName(string value, string field)
    {
        if (Path.GetFileName(value) != value || value is "." or ".." || value.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O campo '{field}' do manifesto deve ser o nome de um arquivo na raiz do pacote.");
    }

    private static ZipArchiveEntry RequireEntry(ZipArchive zip, string name, string message) =>
        zip.GetEntry(name) ?? throw new InvalidDataException(message);

    private static ModelManifest ReadManifest(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return ModelManifestJson.Read(stream);
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

    /// <summary>Resolve o caminho de uma entrada dentro da pasta de destino, recusando qualquer escape.</summary>
    private static string ResolveInside(string baseDirectory, string entryName)
    {
        var baseFull = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(baseFull, entryName));
        if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O pacote contém um caminho de arquivo inválido ('{entryName}'). A instalação foi cancelada.");
        return full;
    }

    /// <summary>Extrai uma entrada; se <paramref name="hash"/> não for nulo, alimenta-o com o conteúdo. Retorna o tamanho.</summary>
    private static long ExtractEntry(ZipArchiveEntry entry, string destination, IncrementalHash? hash, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = entry.Open();
        using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize);
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            target.Write(buffer, 0, read);
            hash?.AppendData(buffer, 0, read);
            total += read;
        }
        return total;
    }

    private void Remove(string id, CancellationToken ct)
    {
        lock (_ioGate)
        {
            ct.ThrowIfCancellationRequested();
            var folder = Path.Combine(_root, id);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            SetInstalled(Scan());
        }
        _logger.LogInformation("Modelo {Id} removido.", id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateId(string id)
    {
        if (!IdPattern.IsMatch(id))
            throw new ArgumentException("Identificador de modelo inválido.", nameof(id));
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível apagar a pasta temporária de instalação {Pasta}.", path);
        }
    }

    private static int CompareVersions(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return va.CompareTo(vb);
        return string.CompareOrdinal(a, b);
    }
}
