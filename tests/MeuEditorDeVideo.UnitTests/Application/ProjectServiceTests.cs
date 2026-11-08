using System.IO;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Infrastructure.Settings;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Application;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-service-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly MedProjectStore _store;
    private readonly MediaLocator _locator;
    private readonly AutosaveStore _autosave;
    private readonly JsonSettingsService _settings;

    public ProjectServiceTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
        _store = new MedProjectStore(_paths, NullLogger<MedProjectStore>.Instance);
        _locator = new MediaLocator(_paths, NullLogger<MediaLocator>.Instance);
        _autosave = new AutosaveStore(_paths, _store, NullLogger<AutosaveStore>.Instance);
        _settings = new JsonSettingsService(_paths, NullLogger<JsonSettingsService>.Instance);
    }

    private string ProjectsDir => Path.Combine(_root, "projetos");

    private ProjectService CreateService() =>
        new(_store, _locator, _autosave, _settings, NullLogger<ProjectService>.Instance);

    private NewProjectRequest Request(string name) =>
        new(name, ProjectsDir, new ProjectSettings(), StorageMode.Linked);

    private static byte[] RandomBytes(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static string WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task CreateAsync_cria_o_med_define_a_sessao_atual_e_registra_nos_recentes()
    {
        var service = CreateService();
        Directory.CreateDirectory(ProjectsDir);

        await service.CreateAsync(Request("Meu filme"));

        var expected = Path.Combine(ProjectsDir, "Meu filme.med");
        Assert.True(File.Exists(expected));
        Assert.NotNull(service.Current);
        Assert.Equal("Meu filme", service.Current!.Project.Name);
        Assert.Equal(expected, service.Current.FilePath);
        Assert.False(service.Current.IsDirty);
        Assert.Contains(expected, _settings.Current.RecentProjects);
    }

    [Fact]
    public async Task CreateAsync_com_nome_existente_lanca_MedException()
    {
        var service = CreateService();
        Directory.CreateDirectory(ProjectsDir);
        await service.CreateAsync(Request("Repetido"));

        var ex = await Assert.ThrowsAsync<MedException>(() => service.CreateAsync(Request("Repetido")));

        Assert.Equal("MED-1102", ex.Error.Code);
    }

    [Fact]
    public async Task Renomear_deixa_dirty_e_salvar_limpa_e_o_recarregamento_mostra_o_novo_nome()
    {
        var service = CreateService();
        Directory.CreateDirectory(ProjectsDir);
        await service.CreateAsync(Request("Original"));
        var path = service.Current!.FilePath!;

        service.Execute(new RenameProjectCommand("Novo nome"));
        Assert.True(service.Current.IsDirty);

        await service.SaveAsync();
        Assert.False(service.Current.IsDirty);

        var reopened = CreateService();
        await reopened.OpenAsync(path);
        Assert.Equal("Novo nome", reopened.Current!.Project.Name);
    }

    [Fact]
    public async Task SaveAsAsync_acrescenta_extensao_med_e_troca_o_caminho_do_arquivo()
    {
        var service = CreateService();
        Directory.CreateDirectory(ProjectsDir);
        await service.CreateAsync(Request("Base"));

        var requested = Path.Combine(ProjectsDir, "copia");
        await service.SaveAsAsync(requested);

        var expected = Path.Combine(ProjectsDir, "copia.med");
        Assert.True(File.Exists(expected));
        Assert.Equal(expected, service.Current!.FilePath);
    }

    [Fact]
    public async Task OpenAsync_abre_um_projeto_salvo_em_outra_instancia()
    {
        var creator = CreateService();
        Directory.CreateDirectory(ProjectsDir);
        await creator.CreateAsync(Request("Abrir"));
        var id = creator.Current!.Project.Id;
        var path = creator.Current.FilePath!;

        var service = CreateService();
        await service.OpenAsync(path);

        Assert.NotNull(service.Current);
        Assert.Equal(id, service.Current.Project.Id);
        Assert.Equal(Path.GetFullPath(path), service.Current.FilePath);
        Assert.False(service.Current.IsDirty);
    }

    [Fact]
    public async Task CloseAsync_limpa_a_sessao_atual()
    {
        var service = CreateService();
        Directory.CreateDirectory(ProjectsDir);
        await service.CreateAsync(Request("Fechar"));

        await service.CloseAsync();

        Assert.Null(service.Current);
    }

    [Fact]
    public async Task RelinkFromFolderAsync_relocaliza_midia_ausente_encontrada_em_pasta()
    {
        Directory.CreateDirectory(ProjectsDir);
        var service = CreateService();
        await service.CreateAsync(Request("Relocalizar"));
        var projectPath = service.Current!.FilePath!;

        var bytes = RandomBytes(150_000);
        var original = WriteFile(Path.Combine(_root, "midias-antigas", "take.mp4"), bytes);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Kind = MediaKind.Video,
            Name = "take.mp4",
            OriginalPath = original,
            FileSize = bytes.Length,
            LastWriteUtc = File.GetLastWriteTimeUtc(original),
            Fingerprint = await _locator.ComputeFingerprintAsync(original),
        };
        service.Execute(new AddMediaAssetsCommand([asset]));
        await service.SaveAsync();

        var moved = Path.Combine(_root, "midias-novas", "take.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(original, moved);

        var reopened = CreateService();
        await reopened.OpenAsync(projectPath);
        Assert.True(reopened.Current!.HasMissingMedia);

        var relinked = await reopened.RelinkFromFolderAsync(Path.Combine(_root, "midias-novas"));

        Assert.Equal(1, relinked);
        Assert.False(reopened.Current!.HasMissingMedia);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
