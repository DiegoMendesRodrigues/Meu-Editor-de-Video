using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Application.Media;

public sealed record ImportFailure(string Path, string Reason)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record ImportProgress(int Done, int Total, string? CurrentFile);

public sealed record MediaImportResult(IReadOnlyList<MediaAsset> Imported, IReadOnlyList<ImportFailure> Failed, int SkippedDuplicates)
{
    public static readonly MediaImportResult Empty = new([], [], 0);
}

public interface IMediaImportService
{
    /// <summary>
    /// Importa arquivos e pastas (RF021–RF024, RF026, RF027). Todos os importados entram numa única ação de desfazer.
    /// Arquivos incompatíveis ou danificados são listados em <see cref="MediaImportResult.Failed"/> (RF036).
    /// </summary>
    Task<MediaImportResult> ImportAsync(IReadOnlyList<string> paths, string? category = null,
        IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Importa a sequência de imagens numeradas que contém o arquivo informado (RF025).</summary>
    Task<MediaImportResult> ImportSequenceAsync(string anyFrame, FrameRate? frameRate = null, string? category = null,
        CancellationToken cancellationToken = default);
}

public sealed class MediaImportService(
    IProjectService projects,
    IMediaProbe probe,
    IMediaLocator locator,
    ILogger<MediaImportService> logger) : IMediaImportService
{
    public async Task<MediaImportResult> ImportAsync(IReadOnlyList<string> paths, string? category = null,
        IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = projects.Current ?? throw new InvalidOperationException("Nenhum projeto aberto.");
        var (files, rejected) = Expand(paths);

        var known = session.Project.Media.Select(m => m.OriginalPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toImport = files.Where(f => !known.Contains(f)).ToList();
        var skipped = files.Count - toImport.Count;

        var failures = rejected.ToList();
        var done = 0;
        progress?.Report(new ImportProgress(0, toImport.Count, null));

        var tasks = toImport.Select(async path =>
        {
            try
            {
                var result = await probe.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
                if (!result.IsOk) return (Asset: (MediaAsset?)null, Failure: new ImportFailure(path, result.Error ?? "Arquivo incompatível."));
                var fingerprint = await locator.ComputeFingerprintAsync(path, cancellationToken).ConfigureAwait(false);
                return (Asset: Build(path, result, fingerprint, category, sequence: null), Failure: (ImportFailure?)null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (Asset: null, Failure: new ImportFailure(path, "Não foi possível ler o arquivo: " + ex.Message));
            }
            finally
            {
                var n = Interlocked.Increment(ref done);
                progress?.Report(new ImportProgress(n, toImport.Count, Path.GetFileName(path)));
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(true);

        // Mantém a ordem em que os arquivos foram escolhidos.
        var imported = results.Where(r => r.Asset is not null).Select(r => r.Asset!).ToList();
        failures.AddRange(results.Where(r => r.Failure is not null).Select(r => r.Failure!));

        await CommitAsync(imported, cancellationToken).ConfigureAwait(true);
        logger.LogInformation("Importação: {Ok} mídias, {Failed} falhas, {Skipped} já existentes.", imported.Count, failures.Count, skipped);
        return new MediaImportResult(imported, failures, skipped);
    }

    public async Task<MediaImportResult> ImportSequenceAsync(string anyFrame, FrameRate? frameRate = null, string? category = null,
        CancellationToken cancellationToken = default)
    {
        var session = projects.Current ?? throw new InvalidOperationException("Nenhum projeto aberto.");
        var sequence = ImageSequenceDetector.Detect(anyFrame);
        if (sequence is null)
            return new MediaImportResult([], [new ImportFailure(anyFrame,
                "Não foi encontrada uma sequência: são necessárias ao menos 2 imagens numeradas em ordem (ex.: quadro_0001.png, quadro_0002.png).")], 0);

        var name = sequence.Prefix.TrimEnd('_', '-', ' ', '.');
        if (session.Project.StorageMode == StorageMode.Packaged)
            throw new MedException(MedErrors.SequencePackagingUnsupported(name.Length > 0 ? name : sequence.FileName(sequence.FirstNumber)));

        var first = sequence.PathOf(sequence.FirstNumber);
        if (session.Project.Media.Any(m => m.Sequence is not null && string.Equals(m.OriginalPath, first, StringComparison.OrdinalIgnoreCase)))
            return new MediaImportResult([], [], 1);

        var rate = frameRate ?? session.Project.Settings.FrameRate;
        var result = await probe.ProbeSequenceAsync(sequence, rate, cancellationToken).ConfigureAwait(true);
        if (!result.IsOk) return new MediaImportResult([], [new ImportFailure(first, result.Error ?? "Sequência incompatível.")], 0);

        var fingerprint = await locator.ComputeFingerprintAsync(first, cancellationToken).ConfigureAwait(true);
        var asset = Build(first, result, fingerprint, category, sequence) with
        {
            Name = $"{(name.Length > 0 ? name : "Sequência")} ({sequence.FrameCount} imagens)",
        };
        await CommitAsync([asset], cancellationToken).ConfigureAwait(true);
        return new MediaImportResult([asset], [], 0);
    }

    private async Task CommitAsync(IReadOnlyList<MediaAsset> assets, CancellationToken ct)
    {
        if (assets.Count == 0) return;
        projects.Execute(new AddMediaAssetsCommand(assets));
        await projects.RefreshMediaAsync(ct).ConfigureAwait(true);
    }

    private static MediaAsset Build(string path, MediaProbeResult r, string? fingerprint, string? category, ImageSequenceInfo? sequence)
    {
        var info = new FileInfo(path);
        return new MediaAsset
        {
            Id = Guid.NewGuid(),
            Kind = r.Kind,
            Name = info.Name,
            OriginalPath = info.FullName,
            FileSize = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            Fingerprint = fingerprint,
            Duration = r.Duration,
            Width = r.Width,
            Height = r.Height,
            FrameRate = r.FrameRate,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            Info = r.Info,
            Sequence = sequence,
        };
    }

    /// <summary>Expande pastas (recursivamente) e separa extensões não suportadas. Remove repetidos mantendo a ordem.</summary>
    internal static (List<string> Files, List<ImportFailure> Rejected) Expand(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var rejected = new List<ImportFailure>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string file)
        {
            var full = Path.GetFullPath(file);
            if (!seen.Add(full)) return;
            if (MediaFormats.IsSupported(full)) files.Add(full);
            else rejected.Add(new ImportFailure(full, "Formato de arquivo não suportado."));
        }

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                // Em pastas, arquivos não suportados (documentos etc.) são simplesmente ignorados.
                foreach (var f in Directory.EnumerateFiles(path, "*", options).Where(MediaFormats.IsSupported).Order(StringComparer.OrdinalIgnoreCase))
                    Add(f);
            }
            else if (File.Exists(path))
            {
                Add(path);
            }
            else
            {
                rejected.Add(new ImportFailure(path, "Arquivo não encontrado."));
            }
        }

        return (files, rejected);
    }
}
