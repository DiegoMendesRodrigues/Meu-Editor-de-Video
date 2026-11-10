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
        plan.Where(e => !HasPackage(e)).Sum(e => e.SizeBytes);

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
                progress?.Report(new ModelInstallProgress(entry.Id, entry.Name, i + 1, plan.Count, stage, bytes, entry.SizeBytes));

            var package = PackagePath(entry);
            if (!HasPackage(entry))
            {
                var file = Path.Combine(DownloadsDirectory, entry.File);
                Report(ModelInstallStage.Downloading, 0);
                logger?.LogInformation("Baixando {Id} de {Url}", entry.Id, entry.Url);
                await downloader.DownloadAsync(entry.Url, file, entry.SizeBytes, entry.Sha256,
                    new Progress<long>(b => Report(ModelInstallStage.Downloading, b)), cancellationToken);
                Report(ModelInstallStage.Packaging, entry.SizeBytes);
                await ModelPackageBuilder.BuildAsync(entry, file, catalog.LicensePath(entry), package, cancellationToken);
                File.Delete(file);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Report(ModelInstallStage.Installing, entry.SizeBytes);
            await registry.InstallAsync(package, cancellationToken);
            logger?.LogInformation("Modelo {Id} {Version} instalado a partir do catálogo", entry.Id, entry.Version);
            Report(ModelInstallStage.Done, entry.SizeBytes);
        }
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
                && zip.GetEntry(entry.File)?.Length == entry.SizeBytes;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return false;
        }
    }
}
