using System.IO;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Infrastructure.Settings;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

[Collection(SampleMediaCollection.Name)]
public sealed class MediaImportServiceTests : IDisposable
{
    private readonly SampleMedia _media;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-import-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectService _projects;
    private readonly MediaImportService _import;

    public MediaImportServiceTests(SampleMedia media)
    {
        _media = media;
        var paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        paths.EnsureCreated();
        var store = new MedProjectStore(paths, NullLogger<MedProjectStore>.Instance);
        var locator = new MediaLocator(paths, NullLogger<MediaLocator>.Instance);
        var autosave = new AutosaveStore(paths, store, NullLogger<AutosaveStore>.Instance);
        var settings = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        _projects = new ProjectService(store, locator, autosave, settings, NullLogger<ProjectService>.Instance);
        _import = new MediaImportService(_projects, new MediaProbe(NullLogger<MediaProbe>.Instance), locator, NullLogger<MediaImportService>.Instance);
    }

    private Task CreateProjectAsync(StorageMode mode = StorageMode.Linked) =>
        _projects.CreateAsync(new NewProjectRequest("Importação " + mode, Path.Combine(_root, "projetos"), new ProjectSettings(), mode));

    [Fact]
    public async Task Imports_several_files_in_one_undoable_step_and_reports_failures()
    {
        await CreateProjectAsync();
        var progress = new List<ImportProgress>();

        var result = await _import.ImportAsync(
            [_media.Video, _media.Audio, _media.Image, _media.AnimatedGif, _media.Garbage, _media.TextFile],
            progress: new SyncProgress<ImportProgress>(progress.Add));

        Assert.Equal(4, result.Imported.Count);
        Assert.Equal(2, result.Failed.Count);
        Assert.Contains(result.Failed, f => f.Path == Path.GetFullPath(_media.Garbage) && f.Reason.Contains("danificado"));
        Assert.Contains(result.Failed, f => f.Path == Path.GetFullPath(_media.TextFile) && f.Reason.Contains("não suportado"));

        var project = _projects.Current!.Project;
        Assert.Equal(4, project.Media.Count);
        Assert.All(project.Media, m => Assert.False(string.IsNullOrEmpty(m.Fingerprint)));
        var video = project.Media.Single(m => m.Kind == MediaKind.Video);
        Assert.Equal(640, video.Width);
        Assert.Equal("h264", video.Info?.VideoCodec);
        Assert.All(project.Media, m => Assert.NotNull(_projects.Current.Media.Locations[m.Id].ResolvedPath));

        Assert.Equal(5, progress.Last().Done); // .xyz é recusado antes da análise
        Assert.True(_projects.Current.History.CanUndo);
        _projects.Current.History.Undo();
        Assert.Empty(_projects.Current.Project.Media);
    }

    [Fact]
    public async Task Reimporting_the_same_file_is_skipped()
    {
        await CreateProjectAsync();
        await _import.ImportAsync([_media.Video]);
        var again = await _import.ImportAsync([_media.Video]);
        Assert.Empty(again.Imported);
        Assert.Equal(1, again.SkippedDuplicates);
        Assert.Single(_projects.Current!.Project.Media);
    }

    [Fact]
    public async Task Folder_import_finds_media_recursively_and_ignores_other_files()
    {
        await CreateProjectAsync();
        var result = await _import.ImportAsync([_media.Root]);
        // Vídeos, áudio, imagens, GIFs e as 12 imagens da sequência (como imagens soltas); danificados falham; .xyz ignorado.
        Assert.DoesNotContain(result.Failed, f => f.Path.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Imported, m => m.Kind == MediaKind.Audio);
        Assert.True(result.Imported.Count(m => m.Kind == MediaKind.Image) >= 13);
    }

    [Fact]
    public async Task Sequence_import_creates_a_single_asset()
    {
        await CreateProjectAsync();
        var result = await _import.ImportSequenceAsync(_media.SequenceFirstFrame);
        var asset = Assert.Single(result.Imported);
        Assert.Equal(MediaKind.ImageSequence, asset.Kind);
        Assert.Equal(12, asset.Sequence!.FrameCount);
        Assert.Contains("12 imagens", asset.Name);
        Assert.Equal(_projects.Current!.Project.Settings.FrameRate, asset.FrameRate);
    }

    [Fact]
    public async Task Sequence_import_is_refused_in_packaged_projects()
    {
        await CreateProjectAsync(StorageMode.Packaged);
        var ex = await Assert.ThrowsAsync<MedException>(() => _import.ImportSequenceAsync(_media.SequenceFirstFrame));
        Assert.Equal("MED-1011", ex.Error.Code);
    }

    [Fact]
    public async Task Imported_project_round_trips_technical_info()
    {
        await CreateProjectAsync();
        await _import.ImportAsync([_media.Video]);
        await _projects.SaveAsync();
        var path = _projects.Current!.FilePath!;
        await _projects.CloseAsync();

        await _projects.OpenAsync(path);
        var video = Assert.Single(_projects.Current!.Project.Media);
        Assert.Equal("aac", video.Info?.AudioCodec);
        Assert.Equal(48000, video.Info?.SampleRate);
        Assert.False(_projects.Current.HasMissingMedia);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>IProgress síncrono (Progress&lt;T&gt; posta no contexto e chegaria depois das asserções).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
