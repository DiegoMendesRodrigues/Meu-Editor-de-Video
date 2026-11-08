using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Application.Projects;

/// <summary>
/// Salvamento automático periódico (RF010). Grava o último estado publicado da sessão
/// (imutável, portanto seguro fora da thread da interface) quando houver alterações.
/// </summary>
public sealed class AutosaveScheduler(
    IProjectService projects,
    IAutosaveStore autosave,
    ISettingsService settings,
    ILogger<AutosaveScheduler> logger,
    TimeProvider? time = null) : IAsyncDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Project? _lastSaved;

    /// <summary>Intervalo de verificação; o intervalo efetivo de gravação vem das configurações.</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromSeconds(15);

    public DateTimeOffset? LastAutosaveAt { get; private set; }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Tick, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var cfg = settings.Current;
                if (!cfg.AutosaveEnabled) continue;
                var interval = TimeSpan.FromMinutes(Math.Clamp(cfg.AutosaveIntervalMinutes, 1, 60));
                if (LastAutosaveAt is { } last && _time.GetUtcNow() - last < interval) continue;
                await SaveNowAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Grava imediatamente, se houver alterações não salvas. Retorna true se gravou.</summary>
    public async Task<bool> SaveNowAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = projects.Current;
            if (session is null || !session.IsDirty) return false;
            var snapshot = session.Snapshot;
            if (ReferenceEquals(snapshot, _lastSaved)) return false;

            await autosave.SaveAsync(snapshot, session.FilePath, session.Media.AvailablePaths, ct).ConfigureAwait(false);
            _lastSaved = snapshot;
            LastAutosaveAt = _time.GetUtcNow();
            logger.LogDebug("Salvamento automático concluído.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falha no autosave nunca deve derrubar a edição; tenta de novo no próximo ciclo.
            logger.LogWarning(ex, "Falha no salvamento automático.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts.Dispose();
        _gate.Dispose();
    }
}
