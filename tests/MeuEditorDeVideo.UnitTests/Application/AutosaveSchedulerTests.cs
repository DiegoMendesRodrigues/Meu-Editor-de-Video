using System.IO;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Infrastructure.Settings;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Application;

public sealed class AutosaveSchedulerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-scheduler-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly MedProjectStore _store;
    private readonly AutosaveStore _autosave;
    private readonly JsonSettingsService _settings;

    public AutosaveSchedulerTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
        _store = new MedProjectStore(_paths, NullLogger<MedProjectStore>.Instance);
        _autosave = new AutosaveStore(_paths, _store, NullLogger<AutosaveStore>.Instance);
        _settings = new JsonSettingsService(_paths, NullLogger<JsonSettingsService>.Instance);
    }

    private ProjectService CreateService() => new(
        _store,
        new MediaLocator(_paths, NullLogger<MediaLocator>.Instance),
        _autosave,
        _settings,
        NullLogger<ProjectService>.Instance);

    private AutosaveScheduler CreateScheduler(IProjectService service) => new(
        service,
        _autosave,
        _settings,
        NullLogger<AutosaveScheduler>.Instance);

    [Fact]
    public async Task SaveNowAsync_sem_sessao_retorna_false()
    {
        var service = CreateService();
        await using var scheduler = CreateScheduler(service);

        Assert.False(await scheduler.SaveNowAsync());
    }

    [Fact]
    public async Task SaveNowAsync_sem_alteracoes_retorna_false()
    {
        var projects = Path.Combine(_root, "projetos");
        var service = CreateService();
        await service.CreateAsync(new NewProjectRequest("Limpo", projects, new ProjectSettings(), StorageMode.Linked));
        await using var scheduler = CreateScheduler(service);

        Assert.False(await scheduler.SaveNowAsync());
    }

    [Fact]
    public async Task SaveNowAsync_grava_apos_uma_edicao_e_nao_repete_sem_nova_edicao()
    {
        var projects = Path.Combine(_root, "projetos");
        var service = CreateService();
        await service.CreateAsync(new NewProjectRequest("Editado", projects, new ProjectSettings(), StorageMode.Linked));
        await using var scheduler = CreateScheduler(service);

        service.Execute(new RenameProjectCommand("Editado 2"));
        Assert.True(await scheduler.SaveNowAsync());
        Assert.NotNull(scheduler.LastAutosaveAt);

        Assert.False(await scheduler.SaveNowAsync());
    }

    [Fact]
    public async Task FindRecoverable_do_processo_atual_fica_vazio_enquanto_a_sessao_esta_viva()
    {
        var projects = Path.Combine(_root, "projetos");
        var service = CreateService();
        await service.CreateAsync(new NewProjectRequest("Vivo", projects, new ProjectSettings(), StorageMode.Linked));
        await using var scheduler = CreateScheduler(service);
        service.Execute(new RenameProjectCommand("Vivo editado"));
        Assert.True(await scheduler.SaveNowAsync());

        Assert.Empty(service.FindRecoverable());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
