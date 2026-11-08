using System.IO.Compression;
using System.Security.Cryptography;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ProjectEngine.Format;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.ProjectEngine;

/// <summary>
/// Resolve onde estão as mídias do projeto (RF016, RF017, RF019).
/// Mídias empacotadas são extraídas para %LOCALAPPDATA%\MeuEditorDeVideo\cache\projects\{id}\media,
/// com verificação de SHA-256, porque decodificar vídeo diretamente de dentro do ZIP seria inviável.
/// </summary>
public sealed class MediaLocator(IAppPaths paths, ILogger<MediaLocator> logger) : IMediaLocator
{
    private const int FingerprintChunk = 1024 * 1024;

    public string ExtractionDirectory(Guid projectId) =>
        Path.Combine(paths.Cache, "projects", projectId.ToString("N"), "media");

    public async Task<MediaResolution> ResolveAsync(Project project, string? projectFilePath, CancellationToken cancellationToken = default)
    {
        var locations = new Dictionary<Guid, MediaLocation>();
        var projectDir = projectFilePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
        var extractDir = ExtractionDirectory(project.Id);

        Dictionary<string, MedManifestFile>? manifestFiles = null;
        ZipArchive? zip = null;
        FileStream? zipStream = null;

        try
        {
            foreach (var asset in project.Media)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Mídia empacotada: cache extraído (válido) ou extração a partir do .med.
                if (asset.PackagedEntry is { } entryName)
                {
                    var extracted = Path.Combine(extractDir, Path.GetFileName(entryName));
                    if (IsExtractionValid(extracted))
                    {
                        locations[asset.Id] = new MediaLocation(asset.Id, MediaStatus.Available, extracted);
                        continue;
                    }

                    if (projectFilePath is not null && File.Exists(projectFilePath))
                    {
                        if (zip is null)
                        {
                            zipStream = new FileStream(projectFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                            zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
                            var manifest = await MedProjectStore.ReadManifestAsync(zip, projectFilePath, cancellationToken).ConfigureAwait(false);
                            manifestFiles = manifest.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
                        }

                        if (zip.GetEntry(entryName) is { } entry && manifestFiles!.TryGetValue(entryName, out var expected))
                        {
                            await ExtractAsync(entry, expected, extracted, cancellationToken).ConfigureAwait(false);
                            locations[asset.Id] = new MediaLocation(asset.Id, MediaStatus.Available, extracted);
                            continue;
                        }
                    }
                }

                // 2) Caminho absoluto original; 3) caminho relativo ao .med.
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(asset.OriginalPath)) candidates.Add(asset.OriginalPath);
                if (projectDir is not null && !string.IsNullOrWhiteSpace(asset.RelativePath))
                    candidates.Add(Path.GetFullPath(Path.Combine(projectDir, asset.RelativePath)));

                var found = candidates.FirstOrDefault(File.Exists);
                if (found is null)
                {
                    locations[asset.Id] = new MediaLocation(asset.Id, MediaStatus.Missing, null);
                    continue;
                }

                var info = new FileInfo(found);
                var changed = asset.FileSize > 0 && info.Length != asset.FileSize;
                locations[asset.Id] = new MediaLocation(asset.Id, changed ? MediaStatus.Changed : MediaStatus.Available, found);
            }
        }
        finally
        {
            zip?.Dispose();
            if (zipStream is not null) await zipStream.DisposeAsync().ConfigureAwait(false);
        }

        var missing = locations.Values.Count(l => l.Status == MediaStatus.Missing);
        if (missing > 0) logger.LogWarning("{Missing} mídia(s) ausente(s) ao abrir o projeto.", missing);
        return new MediaResolution(locations);
    }

    private static bool IsExtractionValid(string extracted)
    {
        var marker = extracted + ".sha256";
        if (!File.Exists(extracted) || !File.Exists(marker)) return false;
        var parts = File.ReadAllText(marker).Split('|');
        return parts.Length == 2 && long.TryParse(parts[0], out var size) && size == new FileInfo(extracted).Length;
    }

    private static async Task ExtractAsync(ZipArchiveEntry entry, MedManifestFile expected, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";
        try
        {
            string hash;
            long size;
            await using (var input = await entry.OpenAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                (size, hash) = await MedProjectStore.CopyWithHashAsync(input, output, null, ct).ConfigureAwait(false);
            }

            if (size != expected.Size || !hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new MedException(MedErrors.IntegrityFailed(expected.Path));

            File.Move(temp, destination, overwrite: true);
            await File.WriteAllTextAsync(destination + ".sha256", $"{size}|{hash}", ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<IReadOnlyList<RelinkCandidate>> FindCandidatesAsync(
        IEnumerable<MediaAsset> assets, string folder, CancellationToken cancellationToken = default)
    {
        var wanted = assets
            .GroupBy(a => Path.GetFileName(a.OriginalPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var result = new List<RelinkCandidate>();
        if (wanted.Count == 0 || !Directory.Exists(folder)) return result;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!wanted.TryGetValue(Path.GetFileName(file), out var matches)) continue;

            var length = new FileInfo(file).Length;
            foreach (var asset in matches.Where(a => a.FileSize == 0 || a.FileSize == length))
            {
                var fingerprintMatches = asset.Fingerprint is not null
                    && asset.Fingerprint == await ComputeFingerprintAsync(file, cancellationToken).ConfigureAwait(false);
                result.Add(new RelinkCandidate(asset.Id, file, fingerprintMatches));
            }
        }

        // Preferir coincidências confirmadas pela impressão digital.
        return result.OrderBy(c => c.AssetId).ThenByDescending(c => c.FingerprintMatches).ToList();
    }

    /// <summary>SHA-256 de (tamanho + primeiro MiB + último MiB). Rápido mesmo para arquivos de vários GB.</summary>
    public async Task<string> ComputeFingerprintAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, FingerprintChunk, useAsync: true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(BitConverter.GetBytes(fs.Length));

        var buffer = new byte[FingerprintChunk];
        var read = await fs.ReadAtLeastAsync(buffer, Math.Min(FingerprintChunk, (int)Math.Min(fs.Length, int.MaxValue)), false, cancellationToken).ConfigureAwait(false);
        sha.AppendData(buffer, 0, read);

        if (fs.Length > FingerprintChunk * 2L)
        {
            fs.Seek(-FingerprintChunk, SeekOrigin.End);
            read = await fs.ReadAtLeastAsync(buffer, FingerprintChunk, false, cancellationToken).ConfigureAwait(false);
            sha.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
