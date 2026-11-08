using System.IO;
using System.Text.Json.Nodes;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.ProjectEngine;

public sealed class AutosaveStoreTests : IDisposable
{
    /// <summary>PID que nao corresponde a nenhum processo (Process.GetProcessById lanca ArgumentException).</summary>
    private const int DeadProcessId = int.MaxValue;

    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-autosave-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly AutosaveStore _autosave;

    public AutosaveStoreTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
        var store = new MedProjectStore(_paths, NullLogger<MedProjectStore>.Instance);
        _autosave = new AutosaveStore(_paths, store, NullLogger<AutosaveStore>.Instance);
    }

    private string FolderOf(Guid projectId) => Path.Combine(_paths.Autosave, projectId.ToString("N"));

    private static Project NewProject(string name = "Sessao de teste") =>
        Project.CreateNew(name, new ProjectSettings(), StorageMode.Linked, Now);

    /// <summary>Edita o session.json gravado (chaves em camelCase, como o MedJson.Options).</summary>
    private static void EditSession(string folder, Action<JsonObject> edit)
    {
        var path = Path.Combine(folder, "session.json");
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        File.WriteAllText(path, node.ToJsonString());
    }

    [Fact]
    public async Task Sessao_do_proprio_processo_nao_e_recuperavel()
    {
        var project = NewProject();
        _autosave.BeginSession(project.Id, project.Name, null);
        await _autosave.SaveAsync(project, null, new Dictionary<Guid, string>());

        Assert.True(File.Exists(Path.Combine(FolderOf(project.Id), "autosave.med")));
        Assert.Empty(_autosave.FindRecoverable());
    }

    [Fact]
    public async Task Sessao_de_processo_encerrado_com_autosave_e_recuperavel()
    {
        var project = NewProject("Obra");
        const string original = @"C:\Projetos\obra.med";
        _autosave.BeginSession(project.Id, project.Name, original);
        await _autosave.SaveAsync(project, original, new Dictionary<Guid, string>());
        EditSession(FolderOf(project.Id), node => node["processId"] = DeadProcessId);

        var recovered = Assert.Single(_autosave.FindRecoverable());

        Assert.Equal(project.Id, recovered.ProjectId);
        Assert.Equal("Obra", recovered.Name);
        Assert.Equal(original, recovered.OriginalPath);
        Assert.Equal(Path.Combine(FolderOf(project.Id), "autosave.med"), recovered.AutosavePath);
    }

    [Fact]
    public void Sessao_de_processo_encerrado_sem_LastSaved_tem_a_pasta_removida()
    {
        var project = NewProject("Rascunho");
        _autosave.BeginSession(project.Id, project.Name, null);
        var folder = FolderOf(project.Id);
        EditSession(folder, node =>
        {
            node["processId"] = DeadProcessId;
            node.Remove("lastSaved");
        });

        Assert.Empty(_autosave.FindRecoverable());
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Discard_remove_a_pasta_de_autosave()
    {
        var project = NewProject();
        _autosave.BeginSession(project.Id, project.Name, null);
        await _autosave.SaveAsync(project, null, new Dictionary<Guid, string>());
        Assert.True(Directory.Exists(FolderOf(project.Id)));

        _autosave.Discard(project.Id);

        Assert.False(Directory.Exists(FolderOf(project.Id)));
    }

    [Fact]
    public async Task BeginSession_em_pasta_com_autosave_preserva_LastSaved()
    {
        var project = NewProject();
        _autosave.BeginSession(project.Id, project.Name, null);
        await _autosave.SaveAsync(project, null, new Dictionary<Guid, string>());
        var folder = FolderOf(project.Id);

        EditSession(folder, node => node["processId"] = DeadProcessId);
        var savedAt = Assert.Single(_autosave.FindRecoverable()).SavedAt;

        // Reabrir a sessao (sobrescreve session.json com o processo atual) nao pode apagar o LastSaved.
        _autosave.BeginSession(project.Id, project.Name, null);
        EditSession(folder, node => node["processId"] = DeadProcessId);

        var recovered = Assert.Single(_autosave.FindRecoverable());
        Assert.Equal(savedAt, recovered.SavedAt);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
