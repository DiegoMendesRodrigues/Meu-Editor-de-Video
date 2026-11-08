using System.Diagnostics;
using System.Text.Json;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ProjectEngine.Format;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.ProjectEngine;

/// <summary>
/// Salvamento automático e recuperação (RF010, RF011, RNF012).
/// Cada projeto aberto tem a pasta autosave\{id} com session.json (processo dono) e autosave.med.
/// Encerramento normal apaga a pasta; se o processo dono não existe mais, o projeto é recuperável.
/// </summary>
public sealed class AutosaveStore(IAppPaths paths, IProjectStore store, ILogger<AutosaveStore> logger) : IAutosaveStore
{
    private const string SessionFile = "session.json";
    private const string AutosaveFile = "autosave.med";

    private sealed record SessionInfo(
        Guid ProjectId, string Name, string? OriginalPath, int ProcessId, DateTime ProcessStartUtc, DateTimeOffset? LastSaved);

    private string Folder(Guid id) => Path.Combine(paths.Autosave, id.ToString("N"));

    public void BeginSession(Guid projectId, string projectName, string? originalPath)
    {
        var folder = Folder(projectId);
        Directory.CreateDirectory(folder);

        // Um autosave existente (ex.: projeto recuperado) continua válido até o próximo salvamento.
        DateTimeOffset? lastSaved = null;
        try
        {
            var sessionPath = Path.Combine(folder, SessionFile);
            if (File.Exists(sessionPath) && File.Exists(Path.Combine(folder, AutosaveFile)))
                lastSaved = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(sessionPath), MedJson.Options)?.LastSaved;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }

        using var me = Process.GetCurrentProcess();
        WriteSession(new SessionInfo(projectId, projectName, originalPath, me.Id, me.StartTime.ToUniversalTime(), lastSaved));
    }

    public void EndSession(Guid projectId) => Discard(projectId);

    public async Task SaveAsync(Project project, string? originalPath, IReadOnlyDictionary<Guid, string> mediaSources, CancellationToken cancellationToken = default)
    {
        var folder = Folder(project.Id);
        Directory.CreateDirectory(folder);
        await store.SaveAsync(new SaveRequest(project, Path.Combine(folder, AutosaveFile), IncludeMedia: false, mediaSources, PreservePaths: true),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        using var me = Process.GetCurrentProcess();
        WriteSession(new SessionInfo(project.Id, project.Name, originalPath, me.Id, me.StartTime.ToUniversalTime(), DateTimeOffset.Now));
    }

    public IReadOnlyList<RecoverableProject> FindRecoverable()
    {
        var result = new List<RecoverableProject>();
        if (!Directory.Exists(paths.Autosave)) return result;

        foreach (var dir in Directory.EnumerateDirectories(paths.Autosave))
        {
            var sessionPath = Path.Combine(dir, SessionFile);
            var autosavePath = Path.Combine(dir, AutosaveFile);
            try
            {
                if (!File.Exists(sessionPath)) continue;
                var session = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(sessionPath), MedJson.Options);
                if (session is null || IsAlive(session)) continue;

                if (!File.Exists(autosavePath) || session.LastSaved is null)
                {
                    // Sessão encerrada de forma inesperada antes do primeiro autosave: nada a recuperar.
                    Directory.Delete(dir, recursive: true);
                    continue;
                }

                result.Add(new RecoverableProject(session.ProjectId, session.Name, session.OriginalPath, autosavePath, session.LastSaved.Value));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Pasta de autosave ilegível: {Dir}", dir);
            }
        }

        return result.OrderByDescending(r => r.SavedAt).ToList();
    }

    public void Discard(Guid projectId)
    {
        try
        {
            var folder = Folder(projectId);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Não foi possível remover o autosave do projeto {Id}.", projectId);
        }
    }

    private void WriteSession(SessionInfo info)
    {
        var path = Path.Combine(Folder(info.ProjectId), SessionFile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(info, MedJson.Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>O processo dono da sessão ainda está rodando? (PID + horário de início evitam reuso de PID.)</summary>
    private static bool IsAlive(SessionInfo session)
    {
        try
        {
            using var p = Process.GetProcessById(session.ProcessId);
            return !p.HasExited && Math.Abs((p.StartTime.ToUniversalTime() - session.ProcessStartUtc).TotalSeconds) < 2;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // sem acesso ao processo: assumir vivo
    }
}
