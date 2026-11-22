using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.AIEngine.Models;

/// <summary>Situação de um modelo do catálogo no computador.</summary>
public enum CatalogModelState
{
    NotInstalled,
    Installed,

    /// <summary>Instalado numa versão mais antiga que a do catálogo.</summary>
    UpdateAvailable,
}

/// <summary>
/// Instalação a partir do catálogo: para cada modelo (dependências primeiro) baixa o arquivo da origem oficial, confere o
/// SHA-256, gera o pacote <c>.medmodel</c> e o instala pelo <see cref="IModelRegistry"/> — as mesmas validações de um pacote
/// instalado à mão. Os pacotes gerados ficam guardados para reinstalação ou cópia para outro computador.
/// </summary>
public sealed class CatalogModelInstaller(ModelCatalog catalog, IModelRegistry registry, IModelDownloader downloader,
    string workDirectory, ILogger<CatalogModelInstaller>? logger = null)
{
    public ModelCatalog Catalog => catalog;

    /// <summary>Pasta onde os pacotes gerados ficam guardados.</summary>
    public string PackagesDirectory => Path.Combine(workDirectory, "pacotes");

    private string DownloadsDirectory => Path.Combine(workDirectory, "downloads");

    public CatalogModelState StateOf(ModelCatalogEntry entry)
    {
        var installed = registry.Installed.FirstOrDefault(m => m.Manifest.Id == entry.Id && !m.BuiltIn);
        if (installed is null) return CatalogModelState.NotInstalled;
        return ModelCatalog.CompareVersions(installed.Manifest.Version, entry.Version) < 0
            ? CatalogModelState.UpdateAvailable
            : CatalogModelState.Installed;
    }

    /// <summary>Modelos que serão instalados para os pedidos: dependências que faltam + os pedidos (mesmo já instalados, para reinstalar).</summary>
    public IReadOnlyList<ModelCatalogEntry> Plan(IEnumerable<string> ids)
    {
        var requested = ids.ToHashSet();
        return catalog.InstallOrder(requested)
            .Where(e => requested.Contains(e.Id) || StateOf(e) != CatalogModelState.Installed)
            .ToList();
    }

    /// <summary>Bytes a baixar no plano (pacotes já gerados e íntegros não são baixados de novo).</summary>
    public long DownloadBytes(IReadOnlyList<ModelCatalogEntry> plan) =>
        plan.Where(e => !HasPackage(e)).Sum(e => e.TotalBytes);

    /// <summary>Instala o plano na ordem. Um erro interrompe a instalação; os modelos já instalados continuam valendo.</summary>
    public async Task InstallAsync(IReadOnlyList<ModelCatalogEntry> plan, IProgress<ModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DownloadsDirectory);
        Directory.CreateDirectory(PackagesDirectory);
        for (var i = 0; i < plan.Count; i++)
        {
            var entry = plan[i];
            void Report(ModelInstallStage stage, long bytes) =>
                progress?.Report(new ModelInstallProgress(entry.Id, entry.Name, i + 1, plan.Count, stage, bytes, entry.TotalBytes));

            var package = PackagePath(entry);
            if (!HasPackage(entry))
            {
                // Modelos de vários arquivos ficam numa subpasta própria (nomes como config.json podem se repetir).
                var folder = entry.ExtraFiles.Count == 0 ? DownloadsDirectory : Path.Combine(DownloadsDirectory, entry.Id);
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, entry.File);
                Report(ModelInstallStage.Downloading, 0);
                logger?.LogInformation("Baixando {Id} de {Url}", entry.Id, entry.Url);
                long done = 0;
                foreach (var (url, name, size, sha) in Files(entry))
                {
                    var before = done;
                    await downloader.DownloadAsync(url, Path.Combine(folder, name), size, sha,
                        new InlineProgress(b => Report(ModelInstallStage.Downloading, before + b)), cancellationToken);
                    done += size;
                }
                Report(ModelInstallStage.Packaging, entry.TotalBytes);
                await ModelPackageBuilder.BuildAsync(entry, file, catalog.LicensePath(entry), package, cancellationToken);
                foreach (var f in Files(entry)) File.Delete(Path.Combine(folder, f.File));
                if (entry.ExtraFiles.Count > 0) TryDeleteEmpty(folder);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Report(ModelInstallStage.Installing, entry.TotalBytes);
            await registry.InstallAsync(package, cancellationToken);
            logger?.LogInformation("Modelo {Id} {Version} instalado a partir do catálogo", entry.Id, entry.Version);
            Report(ModelInstallStage.Done, entry.TotalBytes);
        }
    }

    /// <summary>
    /// Repassa na hora, na thread de quem avisa: com <see cref="Progress{T}"/> um "baixando" atrasado chegava depois de "concluído".
    /// </summary>
    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    private static IEnumerable<(Uri Url, string File, long Size, string Sha256)> Files(ModelCatalogEntry entry)
    {
        yield return (entry.Url, entry.File, entry.SizeBytes, entry.Sha256);
        foreach (var f in entry.ExtraFiles) yield return (f.Url, f.File, f.SizeBytes, f.Sha256);
    }

    private static void TryDeleteEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (IOException) { }
    }

    public string PackagePath(ModelCatalogEntry entry) => Path.Combine(PackagesDirectory, $"{entry.Id}-{entry.Version}.medmodel");

    /// <summary>Pacote gerado antes, com o manifesto desta versão do catálogo (o registro revalida o hash ao instalar).</summary>
    private bool HasPackage(ModelCatalogEntry entry)
    {
        var path = PackagePath(entry);
        if (!File.Exists(path)) return false;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            if (zip.GetEntry(ModelRegistry.ManifestFileName) is not { } e) return false;
            ModelManifest m;
            using (var s = e.Open()) m = ModelManifestJson.Read(s);
            return string.Equals(m.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)
                && Files(entry).All(f => zip.GetEntry(f.File)?.Length == f.Size);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return false;
        }
    }
}
