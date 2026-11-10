using System.IO;
using System.Windows;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Remoção de projetos da lista de recentes da tela inicial (só da lista ou também o arquivo).</summary>
public sealed class RecentProjectRemovalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-recentes-" + Guid.NewGuid().ToString("N"));

    public RecentProjectRemovalTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeSettings(AppSettings initial) : ISettingsService
    {
        public AppSettings Current { get; private set; } = initial;
        public event EventHandler<AppSettings>? Changed;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDialogs(RecentRemovalChoice answer) : IDialogService
    {
        public bool? AskedWithFileExists { get; private set; }
        public List<string> Infos { get; } = [];

        public RecentRemovalChoice AskRemoveRecent(string projectName, string path, bool fileExists)
        {
            AskedWithFileExists = fileExists;
            return answer;
        }

        public void ShowInfo(string title, string message) => Infos.Add(message);
        public bool Confirm(string title, string message) => true;
        public string? PickFolder(string title, string? initialDirectory) => null;
        public void ShowError(MedError error) { }
        public UnsavedChoice AskSaveChanges(string projectName) => throw new NotSupportedException();
        public string? PickOpenProject(string? initialDirectory) => null;
        public string? PickSaveProject(string suggestedName, string? initialDirectory) => null;
        public string? PickFile(string title, string filter, string? initialDirectory) => null;
        public string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory) => null;
        public IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory) => [];
        public string? Prompt(string title, string label, string initialValue) => null;
        public bool? ShowModal(Window dialog) => null;
        public bool ShowSettings() => false;
    }

    private sealed class FakeAutosave(params RecoverableProject[] recoverable) : IAutosaveStore
    {
        public List<Guid> Discarded { get; } = [];

        public IReadOnlyList<RecoverableProject> FindRecoverable() => recoverable;
        public void Discard(Guid projectId) => Discarded.Add(projectId);
        public void BeginSession(Guid projectId, string projectName, string? originalPath) { }
        public void EndSession(Guid projectId) { }
        public Task SaveAsync(Project project, string? originalPath, IReadOnlyDictionary<Guid, string> mediaSources, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Apaga de verdade (sem Lixeira) para o teste conferir o efeito.</summary>
    private sealed class FakeRecycler(Exception? failure = null) : IFileRecycler
    {
        public List<string> Recycled { get; } = [];

        public void Recycle(string path)
        {
            if (failure is not null) throw failure;
            Recycled.Add(path);
            File.Delete(path);
        }
    }

    private string Project(string name)
    {
        var path = Path.Combine(_dir, name + ".med");
        File.WriteAllText(path, "x");
        return path;
    }

    private static FakeSettings Settings(params string[] recent) => new(new AppSettings { RecentProjects = [.. recent] });

    [Fact]
    public async Task RemoverDaLista_MantemOArquivo()
    {
        var a = Project("A");
        var b = Project("B");
        var settings = Settings(a, b);
        var recycler = new FakeRecycler();
        var sut = new RecentProjectRemoval(settings, new FakeDialogs(RecentRemovalChoice.RemoveFromList), new FakeAutosave(), recycler);

        Assert.True(await sut.RemoveAsync(a));

        Assert.Equal([b], settings.Current.RecentProjects);
        Assert.True(File.Exists(a));
        Assert.Empty(recycler.Recycled);
    }

    [Fact]
    public async Task ExcluirArquivos_MandaParaLixeira_ERemoveDaLista_EDescartaAutosave()
    {
        var a = Project("A");
        var settings = Settings(a);
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var autosave = new FakeAutosave(
            new RecoverableProject(id, "A", a.ToUpperInvariant(), "auto.med", DateTimeOffset.Now),
            new RecoverableProject(other, "Outro", Path.Combine(_dir, "Outro.med"), "auto2.med", DateTimeOffset.Now));
        var recycler = new FakeRecycler();
        var sut = new RecentProjectRemoval(settings, new FakeDialogs(RecentRemovalChoice.DeleteFiles), autosave, recycler);

        Assert.True(await sut.RemoveAsync(a));

        Assert.Empty(settings.Current.RecentProjects);
        Assert.Equal([a], recycler.Recycled);
        Assert.False(File.Exists(a));
        Assert.Equal([id], autosave.Discarded);
    }

    [Fact]
    public async Task Cancelar_NaoMudaNada()
    {
        var a = Project("A");
        var settings = Settings(a);
        var recycler = new FakeRecycler();
        var sut = new RecentProjectRemoval(settings, new FakeDialogs(RecentRemovalChoice.Cancel), new FakeAutosave(), recycler);

        Assert.False(await sut.RemoveAsync(a));

        Assert.Equal([a], settings.Current.RecentProjects);
        Assert.True(File.Exists(a));
        Assert.Empty(recycler.Recycled);
    }

    [Fact]
    public async Task ArquivoInexistente_DialogoSabe_ESoRemoveDaLista()
    {
        var missing = Path.Combine(_dir, "Sumiu.med");
        var settings = Settings(missing);
        var dialogs = new FakeDialogs(RecentRemovalChoice.DeleteFiles);
        var recycler = new FakeRecycler();
        var sut = new RecentProjectRemoval(settings, dialogs, new FakeAutosave(), recycler);

        Assert.True(await sut.RemoveAsync(missing));

        Assert.False(dialogs.AskedWithFileExists);
        Assert.Empty(settings.Current.RecentProjects);
        Assert.Empty(recycler.Recycled);
    }

    [Fact]
    public async Task ProjetoAberto_NaoExclui()
    {
        var a = Project("A");
        var settings = Settings(a);
        var dialogs = new FakeDialogs(RecentRemovalChoice.DeleteFiles);
        var recycler = new FakeRecycler();
        var sut = new RecentProjectRemoval(settings, dialogs, new FakeAutosave(), recycler);

        Assert.False(await sut.RemoveAsync(a, openProjectPath: a));

        Assert.True(File.Exists(a));
        Assert.Equal([a], settings.Current.RecentProjects);
        Assert.Single(dialogs.Infos);
    }

    [Fact]
    public async Task FalhaAoExcluir_AvisaEMantemNaLista()
    {
        var a = Project("A");
        var settings = Settings(a);
        var dialogs = new FakeDialogs(RecentRemovalChoice.DeleteFiles);
        var sut = new RecentProjectRemoval(settings, dialogs, new FakeAutosave(), new FakeRecycler(new IOException("em uso")));

        Assert.False(await sut.RemoveAsync(a));

        Assert.Equal([a], settings.Current.RecentProjects);
        Assert.Contains("em uso", Assert.Single(dialogs.Infos));
    }
}
