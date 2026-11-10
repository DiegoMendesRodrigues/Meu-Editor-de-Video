using MeuEditorDeVideo.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Infrastructure.Cache;

/// <summary>
/// Medição e limpeza do cache em disco (RF209, RNF013). Só mexe nas áreas
/// <c>media</c> e <c>mattes</c> dentro de <see cref="IAppPaths.Cache"/>; o restante (ex.: <c>projects</c>) nunca é tocado.
/// Pontos de reanálise (junções e links simbólicos) nunca são seguidos.
/// </summary>
public sealed class CacheMaintenance(IAppPaths paths, ILogger<CacheMaintenance>? logger = null) : ICacheMaintenance
{
    private const string MediaFolder = "media";
    private const string MattesFolder = "mattes";
    private const string MediaArea = "Miniaturas, formas de onda e proxies";
    private const string MattesArea = "Máscaras de IA";

    /// <summary>Entradas modificadas nesse intervalo são consideradas em uso e não são removidas por Trim.</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(2);

    private static readonly EnumerationOptions ChildOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private readonly object _gate = new();

    /// <summary>Uma entrada = uma subpasta imediata de uma área.</summary>
    private sealed record Entry(string Folder, long Bytes, DateTime LastUsedUtc);

    public IReadOnlyList<CacheUsage> Measure()
    {
        var result = new List<CacheUsage>(2);
        foreach (var (area, folder) in Areas())
        {
            var entries = ScanEntries(folder);
            result.Add(new CacheUsage(area, folder, entries.Sum(e => e.Bytes), entries.Count));
        }
        return result;
    }

    public long Trim(long limitBytes, CancellationToken cancellationToken = default)
    {
        if (limitBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(limitBytes), limitBytes, "O limite não pode ser negativo.");

        lock (_gate)
        {
            var entries = ScanAll();
            long total = entries.Sum(e => e.Bytes);
            if (total <= limitBytes)
                return 0;

            var cutoff = DateTime.UtcNow - RecentWindow;
            long freed = 0;
            foreach (var entry in entries.OrderBy(e => e.LastUsedUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (total - freed <= limitBytes)
                    break;
                if (entry.LastUsedUtc > cutoff)
                    continue; // usada há pouco: provavelmente em uso

                freed += DeleteEntry(entry.Folder);
            }

            logger?.LogInformation("Cache reduzido em {Bytes} bytes para o limite de {Limite} bytes.", freed, limitBytes);
            return freed;
        }
    }

    public long Clear(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            long freed = 0;
            foreach (var entry in ScanAll())
            {
                cancellationToken.ThrowIfCancellationRequested();
                freed += DeleteEntry(entry.Folder);
            }

            logger?.LogInformation("Cache limpo: {Bytes} bytes liberados.", freed);
            return freed;
        }
    }

    private IEnumerable<(string Area, string Folder)> Areas()
    {
        yield return (MediaArea, Path.Combine(paths.Cache, MediaFolder));
        yield return (MattesArea, Path.Combine(paths.Cache, MattesFolder));
    }

    private List<Entry> ScanAll() => Areas().SelectMany(a => ScanEntries(a.Folder)).ToList();

    private static List<Entry> ScanEntries(string areaFolder)
    {
        var result = new List<Entry>();
        foreach (var child in SafeChildren(areaFolder).OfType<DirectoryInfo>())
        {
            if (IsReparsePoint(child))
                continue;

            var files = CollectFiles(child.FullName);
            long bytes = 0;
            var lastUsed = DateTime.MinValue;
            foreach (var file in files)
            {
                bytes += file.Length;
                lastUsed = Latest(lastUsed, Latest(file.LastWriteTimeUtc, file.LastAccessTimeUtc));
            }
            if (files.Count == 0)
                lastUsed = child.LastWriteTimeUtc; // pasta sem arquivos: usa a data da própria pasta

            result.Add(new Entry(child.FullName, bytes, lastUsed));
        }
        return result;
    }

    /// <summary>Apaga os arquivos que puder da entrada e as pastas que ficaram vazias. Devolve os bytes realmente liberados.</summary>
    private long DeleteEntry(string entryFolder)
    {
        long freed = 0;
        foreach (var file in CollectFiles(entryFolder))
        {
            try
            {
                long size = file.Length;
                file.Delete();
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning("Arquivo em uso ou protegido mantido no cache: {Arquivo} ({Motivo})", file.FullName, ex.Message);
            }
        }

        RemoveEmptyDirectories(entryFolder);
        return freed;
    }

    private static void RemoveEmptyDirectories(string folder)
    {
        foreach (var child in SafeChildren(folder).OfType<DirectoryInfo>())
        {
            if (!IsReparsePoint(child))
                RemoveEmptyDirectories(child.FullName);
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pasta ainda em uso (ex.: arquivo bloqueado dentro dela): permanece.
        }
    }

    /// <summary>Todos os arquivos sob <paramref name="root"/>, sem seguir pontos de reanálise.</summary>
    private static List<FileInfo> CollectFiles(string root)
    {
        var files = new List<FileInfo>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            foreach (var item in SafeChildren(pending.Pop()))
            {
                if (IsReparsePoint(item))
                    continue;

                if (item is DirectoryInfo directory)
                    pending.Push(directory.FullName);
                else if (item is FileInfo file)
                    files.Add(file);
            }
        }
        return files;
    }

    private static List<FileSystemInfo> SafeChildren(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ChildOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsReparsePoint(FileSystemInfo item)
    {
        try
        {
            return (item.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // na dúvida, não segue
        }
    }

    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;
}
