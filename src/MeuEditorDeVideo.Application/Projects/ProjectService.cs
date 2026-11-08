using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Application.Projects;

public sealed record NewProjectRequest(string Name, string Directory, ProjectSettings Settings, StorageMode StorageMode);

public interface IProjectService
{
    ProjectSession? Current { get; }

    /// <summary>Disparado ao abrir, criar, fechar ou trocar o arquivo do projeto.</summary>
    event EventHandler? SessionChanged;

    /// <summary>Disparado após salvar (manual ou "Salvar como").</summary>
    event EventHandler? Saved;

    string FileExtension { get; }

    /// <summary>Avisos da última abertura (migração, formato mais novo…).</summary>
    IReadOnlyList<string> LastOpenWarnings { get; }

    Task CreateAsync(NewProjectRequest request, CancellationToken cancellationToken = default);
    Task OpenAsync(string path, CancellationToken cancellationToken = default);
    Task OpenRecoveredAsync(RecoverableProject recoverable, CancellationToken cancellationToken = default);
    Task SaveAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task SaveAsAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Empacota todas as mídias no .med (RF018) e salva.</summary>
    Task PackageMediaAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Cria cópia de segurança do arquivo atual (RF015).</summary>
    Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Aplica um comando de edição. Lança <see cref="EditRejectedException"/> se recusado.</summary>
    void Execute(IEditCommand command);

    Task RelinkAsync(Guid assetId, string newPath, CancellationToken cancellationToken = default);

    /// <summary>Procura mídias ausentes numa pasta; retorna quantas foram relocalizadas.</summary>
    Task<int> RelinkFromFolderAsync(string folder, CancellationToken cancellationToken = default);

    Task CloseAsync();

    IReadOnlyList<RecoverableProject> FindRecoverable();

    void DiscardRecoverable(RecoverableProject recoverable);
}

public sealed class ProjectService(
    IProjectStore store,
    IMediaLocator locator,
    IAutosaveStore autosave,
    ISettingsService settings,
    ILogger<ProjectService> logger,
    TimeProvider? time = null) : IProjectService
{
    public const string Extension = ".med";
    private const int MaxRecent = 10;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public ProjectSession? Current { get; private set; }

    public event EventHandler? SessionChanged;
    public event EventHandler? Saved;

    public string FileExtension => Extension;

    public async Task CreateAsync(NewProjectRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new MedException(new MedError("MED-1101", "Informe um nome de projeto válido (sem os caracteres \\ / : * ? \" < > |)."));

        var path = Path.Combine(request.Directory, name + Extension);
        if (File.Exists(path))
            throw new MedException(new MedError("MED-1102", $"Já existe um projeto com esse nome nesta pasta:\n{path}"));

        var project = Project.CreateNew(name, request.Settings, request.StorageMode, _time.GetLocalNow());
        var saved = await store.SaveAsync(new SaveRequest(project, path, IncludeMedia: false, new Dictionary<Guid, string>()),
            cancellationToken: cancellationToken).ConfigureAwait(true);

        ReplaceSession(new ProjectSession(saved, path, MediaResolution.Empty, recovered: false, migrated: false));
        await AddRecentAsync(path).ConfigureAwait(true);
        logger.LogInformation("Novo projeto criado.");
    }

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var loaded = await store.LoadAsync(path, cancellationToken).ConfigureAwait(true);
        var media = await locator.ResolveAsync(loaded.Project, loaded.FilePath, cancellationToken).ConfigureAwait(true);
        var session = new ProjectSession(loaded.Project, loaded.FilePath, media, recovered: false, migrated: loaded.WasMigrated);
        LastOpenWarnings = loaded.Warnings;
        ReplaceSession(session);
        await AddRecentAsync(loaded.FilePath).ConfigureAwait(true);
    }

    /// <summary>Avisos da última abertura (migração, formato mais novo…), para exibir ao usuário.</summary>
    public IReadOnlyList<string> LastOpenWarnings { get; private set; } = [];

    public async Task OpenRecoveredAsync(RecoverableProject recoverable, CancellationToken cancellationToken = default)
    {
        var loaded = await store.LoadAsync(recoverable.AutosavePath, cancellationToken).ConfigureAwait(true);
        var target = recoverable.OriginalPath;
        var media = await locator.ResolveAsync(loaded.Project, target, cancellationToken).ConfigureAwait(true);
        var session = new ProjectSession(loaded.Project, target, media, recovered: true, migrated: false);
        ReplaceSession(session);
        logger.LogInformation("Projeto recuperado a partir do salvamento automático.");
    }

    public Task SaveAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        if (session.FilePath is null)
            throw new MedException(new MedError("MED-1103", "Escolha onde salvar o projeto (Salvar como)."));
        return SaveToAsync(session, session.FilePath, progress, cancellationToken);
    }

    public async Task SaveAsAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) path += Extension;
        await SaveToAsync(session, Path.GetFullPath(path), progress, cancellationToken).ConfigureAwait(true);
        await AddRecentAsync(session.FilePath!).ConfigureAwait(true);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task PackageMediaAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var missing = session.Project.Media.FirstOrDefault(m => session.Media.Locations.GetValueOrDefault(m.Id)?.ResolvedPath is null);
        if (missing is not null) throw new MedException(MedErrors.MediaUnavailableForPackaging(missing.Name));

        session.History.Execute(new SetStorageModeCommand(StorageMode.Packaged));
        return SaveAsync(progress, cancellationToken);
    }

    public Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        return session.FilePath is null
            ? Task.FromResult<string?>(null)
            : store.CreateBackupAsync(session.FilePath, session.Project.Id, cancellationToken);
    }

    private async Task SaveToAsync(ProjectSession session, string path, IProgress<double>? progress, CancellationToken ct)
    {
        var project = session.Project with { ModifiedAt = _time.GetLocalNow() };
        var sameFile = string.Equals(session.FilePath, path, StringComparison.OrdinalIgnoreCase);

        // Projeto convertido de formato antigo: preservar o original antes de sobrescrever (§9.5).
        if (sameFile && session.WasMigrated)
            await store.CreateBackupAsync(path, project.Id, ct).ConfigureAwait(true);

        var saved = await store.SaveAsync(
            new SaveRequest(project, path, project.StorageMode == StorageMode.Packaged, session.Media.AvailablePaths),
            progress, ct).ConfigureAwait(true);

        session.History.ReplaceCurrentSilently(saved);
        session.History.MarkSaved();
        session.Publish();
        session.FilePath = path;
        session.IsRecovered = false;
        session.WasMigrated = false;

        // O autosave anterior não é mais necessário.
        autosave.EndSession(saved.Id);
        autosave.BeginSession(saved.Id, saved.Name, path);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public void Execute(IEditCommand command) => RequireSession().History.Execute(command);

    public async Task RelinkAsync(Guid assetId, string newPath, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var info = new FileInfo(newPath);
        if (!info.Exists) throw new MedException(MedErrors.ProjectFileNotFound(newPath));
        session.History.Execute(BuildRelink(session, assetId, info));
        await RefreshMediaAsync(session, cancellationToken).ConfigureAwait(true);
    }

    public async Task<int> RelinkFromFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var missingIds = session.Media.Missing.Select(m => m.AssetId).ToHashSet();
        var assets = session.Project.Media.Where(m => missingIds.Contains(m.Id)).ToList();
        var candidates = await locator.FindCandidatesAsync(assets, folder, cancellationToken).ConfigureAwait(true);

        var commands = candidates
            .GroupBy(c => c.AssetId)
            .Select(g => g.First()) // já ordenados: impressão digital confirmada primeiro
            .Where(c => c.FingerprintMatches || session.Project.FindMedia(c.AssetId)?.Fingerprint is null)
            .Select(c => (IEditCommand)BuildRelink(session, c.AssetId, new FileInfo(c.Path)))
            .ToList();

        if (commands.Count == 0) return 0;
        session.History.Execute(new CompositeCommand(commands.Count == 1 ? "Localizar mídia" : $"Localizar {commands.Count} mídias", commands));
        await RefreshMediaAsync(session, cancellationToken).ConfigureAwait(true);
        return commands.Count;
    }

    private static RelinkMediaCommand BuildRelink(ProjectSession session, Guid assetId, FileInfo file)
    {
        string? relative = null;
        if (session.FilePath is { } p
            && string.Equals(Path.GetPathRoot(p), Path.GetPathRoot(file.FullName), StringComparison.OrdinalIgnoreCase))
            relative = Path.GetRelativePath(Path.GetDirectoryName(p)!, file.FullName);
        return new RelinkMediaCommand(assetId, file.FullName, relative, file.Length, file.LastWriteTimeUtc);
    }

    private async Task RefreshMediaAsync(ProjectSession session, CancellationToken ct)
    {
        session.Media = await locator.ResolveAsync(session.Project, session.FilePath, ct).ConfigureAwait(true);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task CloseAsync()
    {
        if (Current is { } session) autosave.EndSession(session.Project.Id);
        Current = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public IReadOnlyList<RecoverableProject> FindRecoverable() => autosave.FindRecoverable();

    public void DiscardRecoverable(RecoverableProject recoverable) => autosave.Discard(recoverable.ProjectId);

    private void ReplaceSession(ProjectSession session)
    {
        if (Current is { } previous && previous.Project.Id != session.Project.Id)
            autosave.EndSession(previous.Project.Id);

        Current = session;
        autosave.BeginSession(session.Project.Id, session.Project.Name, session.FilePath);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddRecentAsync(string path)
    {
        var current = settings.Current;
        var recent = new[] { path }
            .Concat(current.RecentProjects.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxRecent)
            .ToList();
        await settings.SaveAsync(current with { RecentProjects = recent }).ConfigureAwait(true);
    }

    private ProjectSession RequireSession() =>
        Current ?? throw new InvalidOperationException("Nenhum projeto aberto.");
}
