using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Application.Export;

public enum RenderJobStatus
{
    Queued,
    Running,

    /// <summary>Interrompida (aplicativo fechado ou queda): pode ser retomada aproveitando os trechos prontos.</summary>
    Interrupted,
    Completed,
    Failed,
    Canceled,
}

/// <summary>Trabalho da fila de renderização (estado persistido em disco).</summary>
public sealed record RenderJob
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string OutputPath { get; init; }
    public required ExportSettings Settings { get; init; }
    public string TemplateName { get; init; } = "";
    public Guid SequenceId { get; init; }
    public MediaTime RangeStart { get; init; }
    public MediaTime? RangeEnd { get; init; }
    public RenderJobStatus Status { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Encoder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public TimeSpan? Elapsed { get; init; }

    public bool IsActive => Status is RenderJobStatus.Queued or RenderJobStatus.Running;

    public bool CanResume => Status is RenderJobStatus.Interrupted or RenderJobStatus.Failed or RenderJobStatus.Canceled;
}

public sealed record RenderJobProgress(Guid JobId, RenderProgress Progress);

public interface IRenderQueueService
{
    IReadOnlyList<RenderJob> Jobs { get; }

    /// <summary>A lista ou o estado de algum trabalho mudou (disparado fora da thread da interface).</summary>
    event EventHandler? Changed;

    event EventHandler<RenderJobProgress>? ProgressChanged;

    RenderProgress? ProgressOf(Guid jobId);

    /// <summary>Lê a fila persistida; trabalhos que estavam em andamento ficam "Interrompidos".</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<RenderJob> EnqueueAsync(Project project, IReadOnlyDictionary<Guid, string> mediaPaths, ExportSettings settings,
        string outputPath, string templateName, MediaTime rangeStart, MediaTime? rangeEnd, CancellationToken cancellationToken = default);

    void Cancel(Guid jobId);

    void Resume(Guid jobId);

    void Remove(Guid jobId);

    void ClearFinished();

    /// <summary>Aguarda o término (concluído, falha ou cancelado) de um trabalho.</summary>
    Task<RenderJob> WhenFinishedAsync(Guid jobId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fila de renderização em segundo plano (§7): um trabalho por vez, persistida em
/// %LOCALAPPDATA%\MeuEditorDeVideo\render-queue (lista, foto do projeto e trechos já codificados por trabalho).
/// </summary>
public sealed class RenderQueueService : IRenderQueueService, IDisposable
{
    private const string QueueFile = "queue.json";

    private readonly IRenderEngine _engine;
    private readonly IDomainJson _json;
    private readonly ISettingsService _settings;
    private readonly ILogger<RenderQueueService> _logger;
    private readonly string _root;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<Guid, RenderProgress> _progress = [];
    private readonly Dictionary<Guid, TaskCompletionSource<RenderJob>> _waiters = [];
    private List<RenderJob> _jobs = [];
    private CancellationTokenSource? _current;
    private Guid? _currentId;
    private Task? _worker;

    public RenderQueueService(IRenderEngine engine, IDomainJson json, ISettingsService settings, IAppPaths paths, ILogger<RenderQueueService> logger)
        : this(engine, json, settings, Path.Combine(paths.LocalData, "render-queue"), logger) { }

    public RenderQueueService(IRenderEngine engine, IDomainJson json, ISettingsService settings, string root, ILogger<RenderQueueService> logger)
    {
        _engine = engine;
        _json = json;
        _settings = settings;
        _root = root;
        _logger = logger;
    }

    public IReadOnlyList<RenderJob> Jobs
    {
        get { lock (_gate) return [.. _jobs]; }
    }

    public event EventHandler? Changed;

    public event EventHandler<RenderJobProgress>? ProgressChanged;

    public RenderProgress? ProgressOf(Guid jobId)
    {
        lock (_gate) return _progress.GetValueOrDefault(jobId);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, QueueFile);
        List<RenderJob> loaded = [];
        try
        {
            if (File.Exists(path))
                loaded = _json.Deserialize<List<RenderJob>>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Fila de renderização ilegível; começando vazia.");
        }

        lock (_gate)
        {
            _jobs = loaded.Select(j => j.Status == RenderJobStatus.Running ? j with { Status = RenderJobStatus.Interrupted } : j).ToList();
            _worker ??= Task.Run(WorkerAsync);
        }
        await SaveAsync().ConfigureAwait(false);
        if (_jobs.Any(j => j.Status == RenderJobStatus.Queued)) _signal.Release();
        RaiseChanged();
    }

    public async Task<RenderJob> EnqueueAsync(Project project, IReadOnlyDictionary<Guid, string> mediaPaths, ExportSettings settings,
        string outputPath, string templateName, MediaTime rangeStart, MediaTime? rangeEnd, CancellationToken cancellationToken = default)
    {
        var problems = ExportFormats.Validate(settings, project.Settings).ToList();
        if (problems.Count > 0) throw new MedException(MedErrors.InvalidExportSettings(problems));
        lock (_gate)
            _worker ??= Task.Run(WorkerAsync);

        var job = new RenderJob
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileName(outputPath),
            OutputPath = outputPath,
            Settings = settings,
            TemplateName = templateName,
            SequenceId = project.MainSequence.Id,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            Status = RenderJobStatus.Queued,
            CreatedAt = DateTimeOffset.Now,
        };

        // Foto imutável do projeto: edições posteriores não alteram o que está na fila.
        var dir = JobDirectory(job.Id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "project.json"), _json.Serialize(project), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(dir, "media.json"), _json.Serialize(mediaPaths.ToDictionary(p => p.Key, p => p.Value)), cancellationToken).ConfigureAwait(false);

        lock (_gate) _jobs.Add(job);
        await SaveAsync().ConfigureAwait(false);
        RaiseChanged();
        _signal.Release();
        return job;
    }

    public void Cancel(Guid jobId)
    {
        lock (_gate)
        {
            if (_currentId == jobId)
            {
                _current?.Cancel();
                return;
            }
        }
        Update(jobId, j => j.Status == RenderJobStatus.Queued ? j with { Status = RenderJobStatus.Canceled, FinishedAt = DateTimeOffset.Now } : j);
    }

    public void Resume(Guid jobId)
    {
        Update(jobId, j => j.CanResume ? j with { Status = RenderJobStatus.Queued, Error = null, ErrorCode = null } : j);
        _signal.Release();
    }

    public void Remove(Guid jobId)
    {
        lock (_gate)
        {
            if (_currentId == jobId) return;
            _jobs.RemoveAll(j => j.Id == jobId);
            _progress.Remove(jobId);
        }
        TryDelete(JobDirectory(jobId));
        _ = SaveAsync();
        RaiseChanged();
    }

    public void ClearFinished()
    {
        List<RenderJob> removed;
        lock (_gate)
        {
            removed = _jobs.Where(j => j.Status is RenderJobStatus.Completed or RenderJobStatus.Canceled or RenderJobStatus.Failed).ToList();
            _jobs.RemoveAll(removed.Contains);
        }
        foreach (var j in removed) TryDelete(JobDirectory(j.Id));
        _ = SaveAsync();
        RaiseChanged();
    }

    public Task<RenderJob> WhenFinishedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId) ?? throw new ArgumentException("Trabalho inexistente.", nameof(jobId));
            if (!job.IsActive) return Task.FromResult(job);
            if (!_waiters.TryGetValue(jobId, out var tcs))
                _waiters[jobId] = tcs = new TaskCompletionSource<RenderJob>(TaskCreationOptions.RunContinuationsAsynchronously);
            return tcs.Task.WaitAsync(cancellationToken);
        }
    }

    // ------------------------------------------------------------------ execução

    private async Task WorkerAsync()
    {
        var ct = _shutdown.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!ct.IsCancellationRequested && NextQueued() is { } job)
                await RunAsync(job, ct).ConfigureAwait(false);
        }
    }

    private RenderJob? NextQueued()
    {
        lock (_gate) return _jobs.FirstOrDefault(j => j.Status == RenderJobStatus.Queued);
    }

    private async Task RunAsync(RenderJob job, CancellationToken shutdown)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        lock (_gate)
        {
            _current = cts;
            _currentId = job.Id;
        }
        Update(job.Id, j => j with { Status = RenderJobStatus.Running, Error = null, ErrorCode = null });

        RenderJob final;
        try
        {
            var dir = JobDirectory(job.Id);
            var project = _json.Deserialize<Project>(await File.ReadAllTextAsync(Path.Combine(dir, "project.json"), cts.Token).ConfigureAwait(false))
                ?? throw new InvalidDataException("Foto do projeto ausente.");
            var media = _json.Deserialize<Dictionary<Guid, string>>(await File.ReadAllTextAsync(Path.Combine(dir, "media.json"), cts.Token).ConfigureAwait(false)) ?? [];

            var request = new RenderRequest
            {
                Project = project,
                SequenceId = job.SequenceId,
                MediaPaths = media,
                Settings = job.Settings,
                OutputPath = job.OutputPath,
                RangeStart = job.RangeStart,
                RangeEnd = job.RangeEnd,
                WorkDirectory = Path.Combine(dir, "trechos"),
                HardwareDecoding = _settings.Current.UseGpuAcceleration,
            };
            var progress = new InlineProgress(p =>
            {
                lock (_gate) _progress[job.Id] = p;
                ProgressChanged?.Invoke(this, new RenderJobProgress(job.Id, p));
            });

            var result = await _engine.RenderAsync(request, progress, cts.Token).ConfigureAwait(false);
            final = Update(job.Id, j => j with
            {
                Status = RenderJobStatus.Completed,
                Files = result.Files,
                Warnings = result.Warnings,
                Encoder = result.VideoEncoder,
                FinishedAt = DateTimeOffset.Now,
                Elapsed = result.Elapsed,
            });
            TryDelete(Path.Combine(dir, "trechos"));
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            final = Update(job.Id, j => j with { Status = RenderJobStatus.Interrupted });
        }
        catch (OperationCanceledException)
        {
            final = Update(job.Id, j => j with { Status = RenderJobStatus.Canceled, FinishedAt = DateTimeOffset.Now });
        }
        catch (MedException ex)
        {
            _logger.LogWarning(ex, "Exportação falhou: {Code}", ex.Error.Code);
            final = Update(job.Id, j => j with { Status = RenderJobStatus.Failed, Error = ex.Error.Message, ErrorCode = ex.Error.Code, FinishedAt = DateTimeOffset.Now });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado na exportação.");
            var err = MedError.Unexpected(ex);
            final = Update(job.Id, j => j with { Status = RenderJobStatus.Failed, Error = err.Message, ErrorCode = err.Code, FinishedAt = DateTimeOffset.Now });
        }
        finally
        {
            lock (_gate)
            {
                _current = null;
                _currentId = null;
            }
        }

        TaskCompletionSource<RenderJob>? waiter;
        lock (_gate)
        {
            _waiters.Remove(job.Id, out waiter);
        }
        waiter?.TrySetResult(final);
    }

    private RenderJob Update(Guid id, Func<RenderJob, RenderJob> change)
    {
        RenderJob? updated = null;
        lock (_gate)
        {
            var i = _jobs.FindIndex(j => j.Id == id);
            if (i < 0) throw new ArgumentException("Trabalho inexistente.", nameof(id));
            _jobs[i] = updated = change(_jobs[i]);
        }
        _ = SaveAsync();
        RaiseChanged();
        TaskCompletionSource<RenderJob>? waiter = null;
        if (!updated.IsActive)
            lock (_gate) _waiters.Remove(id, out waiter);
        waiter?.TrySetResult(updated);
        return updated;
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            string json;
            lock (_gate) json = _json.Serialize(_jobs);
            var path = Path.Combine(_root, QueueFile);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível gravar a fila de renderização.");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private string JobDirectory(Guid id) => Path.Combine(_root, id.ToString("N"));

    private void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Pasta da fila não removida: {Dir}", dir);
        }
    }

    /// <summary>Encerramento do aplicativo: o trabalho em andamento fica "Interrompido" e será retomável.</summary>
    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // O trabalho em andamento terminou com cancelamento.
        }
        SaveAsync().GetAwaiter().GetResult();
        _shutdown.Dispose();
    }

    private sealed class InlineProgress(Action<RenderProgress> report) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => report(value);
    }
}
