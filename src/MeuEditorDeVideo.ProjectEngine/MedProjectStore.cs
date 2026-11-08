using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ProjectEngine.Format;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.ProjectEngine;

/// <summary>
/// Implementação do formato .med (§9): ZIP com manifest.json + project.json + mídias opcionais.
/// Gravação atômica: o arquivo é montado e verificado num temporário da mesma pasta e só então substitui o original.
/// </summary>
public sealed class MedProjectStore(IAppPaths paths, ILogger<MedProjectStore> logger, MedMigrator? migrator = null) : IProjectStore
{
    private const int MaxBackupsPerProject = 10;
    private const int CopyBufferSize = 1024 * 1024;
    private readonly MedMigrator _migrator = migrator ?? MedMigrator.Default;

    private static readonly string? AppVersion =
        typeof(MedProjectStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

    public string CurrentFormatVersion => MedMigrator.Current.ToString();

    // ------------------------------------------------------------------ Abrir

    public async Task<LoadedProject> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new MedException(MedErrors.ProjectFileNotFound(path));

        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var manifest = await ReadManifestAsync(zip, path, cancellationToken).ConfigureAwait(false);
            var fileVersion = ParseVersion(manifest.FormatVersion, path);
            if (fileVersion.Major > MedMigrator.Current.Major)
                throw new MedException(MedErrors.NewerFormat(manifest.FormatVersion, CurrentFormatVersion));

            await VerifyIntegrityAsync(zip, manifest, cancellationToken).ConfigureAwait(false);

            var projectEntry = zip.GetEntry(MedLayout.Project) ?? throw new MedException(MedErrors.NotAProject(path));
            JsonObject json;
            await using (var ps = await projectEntry.OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                json = (await JsonNode.ParseAsync(ps, cancellationToken: cancellationToken).ConfigureAwait(false)) as JsonObject
                       ?? throw new MedException(MedErrors.NotAProject(path));
            }

            var migrated = _migrator.Migrate(json, fileVersion, out var warnings);
            var project = json.Deserialize<Project>(MedJson.Options) ?? throw new MedException(MedErrors.NotAProject(path));

            var problems = ProjectValidator.Validate(project);
            if (problems.Count > 0) throw new MedException(MedErrors.InvalidProject(problems));

            var allWarnings = warnings.ToList();
            if (manifest.ProjectId != project.Id)
                allWarnings.Add("O identificador do manifesto não corresponde ao do projeto.");

            logger.LogInformation("Projeto aberto: formato {Version}, {Media} mídias, migrado={Migrated}.",
                manifest.FormatVersion, project.Media.Count, migrated);

            return new LoadedProject(project, Path.GetFullPath(path), manifest.FormatVersion, migrated, allWarnings);
        }
        catch (MedException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new MedException(MedErrors.NotAProject(path, ex));
        }
        catch (UnauthorizedAccessException ex) { throw new MedException(MedErrors.AccessDenied(path, ex)); }
        catch (IOException ex) when (IsSharingViolation(ex)) { throw new MedException(MedErrors.FileInUse(path, ex)); }
    }

    private static MedVersion ParseVersion(string text, string path)
    {
        try { return MedVersion.Parse(text); }
        catch (FormatException ex) { throw new MedException(MedErrors.NotAProject(path, ex)); }
    }

    internal static async Task<MedManifest> ReadManifestAsync(ZipArchive zip, string path, CancellationToken ct)
    {
        var entry = zip.GetEntry(MedLayout.Manifest) ?? throw new MedException(MedErrors.NotAProject(path));
        await using var s = await entry.OpenAsync(ct).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<MedManifest>(s, MedJson.Options, ct).ConfigureAwait(false);
        if (manifest is null || manifest.Format != MedManifest.FormatName) throw new MedException(MedErrors.NotAProject(path));
        return manifest;
    }

    /// <summary>Confere hash dos arquivos de dados e tamanho das mídias (o hash das mídias é conferido na extração).</summary>
    private static async Task VerifyIntegrityAsync(ZipArchive zip, MedManifest manifest, CancellationToken ct)
    {
        foreach (var file in manifest.Files)
        {
            var entry = zip.GetEntry(file.Path) ?? throw new MedException(MedErrors.IntegrityFailed(file.Path));
            if (entry.Length != file.Size) throw new MedException(MedErrors.IntegrityFailed(file.Path));
            if (MedLayout.IsMedia(file.Path)) continue;

            await using var s = await entry.OpenAsync(ct).ConfigureAwait(false);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct).ConfigureAwait(false));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new MedException(MedErrors.IntegrityFailed(file.Path));
        }
    }

    // ------------------------------------------------------------------ Salvar

    public async Task<Project> SaveAsync(SaveRequest request, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(request.TargetPath);
        var directory = Path.GetDirectoryName(target)!;
        var project = request.PreservePaths ? request.Project : PreparePaths(request, directory);

        var problems = ProjectValidator.Validate(project);
        if (problems.Count > 0) throw new MedException(MedErrors.InvalidProject(problems));

        var temp = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await WritePackageAsync(project, request, temp, progress, cancellationToken).ConfigureAwait(false);
            await VerifyWrittenAsync(temp, cancellationToken).ConfigureAwait(false);

            if (File.Exists(target))
                File.Replace(temp, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, target);

            logger.LogInformation("Projeto salvo ({Mode}, {Media} mídias, incluiMídias={Include}).",
                project.StorageMode, project.Media.Count, request.IncludeMedia);
            progress?.Report(1);
            return project;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not MedException)
        {
            logger.LogError(ex, "Falha ao salvar projeto.");
            throw new MedException(MapSaveError(ex, target));
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Atualiza caminhos relativos e entradas de pacote conforme o destino e a modalidade.</summary>
    private static Project PreparePaths(SaveRequest request, string directory)
    {
        var media = request.Project.Media.Select(asset =>
        {
            var relative = RelativeOrNull(directory, asset.OriginalPath);
            string? entry = null;
            if (request.IncludeMedia)
            {
                if (asset.Kind == MediaKind.ImageSequence)
                    throw new MedException(MedErrors.SequencePackagingUnsupported(asset.Name));
                if (!request.MediaSources.TryGetValue(asset.Id, out var source) || !File.Exists(source))
                    throw new MedException(MedErrors.MediaUnavailableForPackaging(asset.Name));
                entry = MedLayout.MediaFolder(asset.Kind) + asset.Id.ToString("N") + Path.GetExtension(source).ToLowerInvariant();
            }

            return asset with { RelativePath = relative, PackagedEntry = entry };
        });

        return request.Project with { Media = [.. media] };
    }

    private static string? RelativeOrNull(string baseDir, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        if (!string.Equals(Path.GetPathRoot(baseDir), Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase)) return null;
        return Path.GetRelativePath(baseDir, path);
    }

    private static async Task WritePackageAsync(Project project, SaveRequest request, string temp, IProgress<double>? progress, CancellationToken ct)
    {
        var files = new List<MedManifestFile>();

        await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Mídias primeiro (maior volume), sem compressão: vídeo/áudio já são comprimidos e
                // entradas "stored" permitem leitura rápida.
                if (request.IncludeMedia)
                {
                    var packaged = project.Media.Where(m => m.PackagedEntry is not null).ToList();
                    long total = packaged.Sum(m => new FileInfo(request.MediaSources[m.Id]).Length), done = 0;
                    foreach (var asset in packaged)
                    {
                        var source = request.MediaSources[asset.Id];
                        var entry = zip.CreateEntry(asset.PackagedEntry!, CompressionLevel.NoCompression);
                        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
                        await using var output = await entry.OpenAsync(ct).ConfigureAwait(false);
                        var (size, hash) = await CopyWithHashAsync(input, output, n =>
                        {
                            done += n;
                            if (total > 0) progress?.Report(0.95 * done / total);
                        }, ct).ConfigureAwait(false);
                        files.Add(new MedManifestFile { Path = asset.PackagedEntry!, Size = size, Sha256 = hash });
                    }
                }

                var projectBytes = JsonSerializer.SerializeToUtf8Bytes(project, MedJson.Options);
                await WriteEntryAsync(zip, MedLayout.Project, projectBytes, ct).ConfigureAwait(false);
                files.Add(new MedManifestFile { Path = MedLayout.Project, Size = projectBytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(projectBytes)) });

                var manifest = new MedManifest
                {
                    FormatVersion = MedMigrator.Current.ToString(),
                    ProjectId = project.Id,
                    Name = project.Name,
                    CreatedAt = project.CreatedAt,
                    ModifiedAt = project.ModifiedAt,
                    StorageMode = project.StorageMode,
                    AppVersion = AppVersion,
                    Files = files,
                };
                await WriteEntryAsync(zip, MedLayout.Manifest, JsonSerializer.SerializeToUtf8Bytes(manifest, MedJson.Options), ct).ConfigureAwait(false);
            }

            await fs.FlushAsync(ct).ConfigureAwait(false);
            fs.Flush(flushToDisk: true);
        }
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] data, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var s = await entry.OpenAsync(ct).ConfigureAwait(false);
        await s.WriteAsync(data, ct).ConfigureAwait(false);
    }

    internal static async Task<(long Size, string Sha256)> CopyWithHashAsync(Stream input, Stream output, Action<int>? onChunk, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[CopyBufferSize];
        long size = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            sha.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            size += read;
            onChunk?.Invoke(read);
        }

        return (size, Convert.ToHexStringLower(sha.GetHashAndReset()));
    }

    /// <summary>Validação do resultado (§7.2): reabre o temporário e confere manifesto, tamanhos e dados.</summary>
    private static async Task VerifyWrittenAsync(string temp, CancellationToken ct)
    {
        await using var fs = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var manifest = await ReadManifestAsync(zip, temp, ct).ConfigureAwait(false);
        await VerifyIntegrityAsync(zip, manifest, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ Cópias de segurança

    public async Task<string?> CreateBackupAsync(string projectPath, Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath)) return null;

        var dir = Path.Combine(paths.Backups, projectId.ToString("N"));
        Directory.CreateDirectory(dir);
        var name = $"{Path.GetFileNameWithoutExtension(projectPath)}_{DateTime.Now:yyyyMMdd-HHmmss-fff}.med";
        var destination = Path.Combine(dir, name);
        for (var n = 2; File.Exists(destination); n++)
            destination = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}_{n}.med");

        await using (var input = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        foreach (var old in new DirectoryInfo(dir).GetFiles("*.med").OrderByDescending(f => f.CreationTimeUtc).Skip(MaxBackupsPerProject))
            TryDelete(old.FullName);

        logger.LogInformation("Cópia de segurança criada em {Dir}.", dir);
        return destination;
    }

    // ------------------------------------------------------------------ Utilitários

    private static MedError MapSaveError(Exception ex, string target) => ex switch
    {
        IOException io when IsDiskFull(io) => MedErrors.DiskFull(io),
        IOException io when IsSharingViolation(io) => MedErrors.FileInUse(target, io),
        UnauthorizedAccessException ua => MedErrors.AccessDenied(target, ua),
        _ => MedErrors.SaveFailed(target, ex),
    };

    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x70 or 0x27;

    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 0x20 or 0x21;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
