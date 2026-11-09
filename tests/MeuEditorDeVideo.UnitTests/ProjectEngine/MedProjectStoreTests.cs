using System.IO;
using System.IO.Compression;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.ProjectEngine;

public sealed class MedProjectStoreTests : IDisposable
{
    private const string MedJsonVersion = "1.1";
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-store-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly MedProjectStore _store;

    public MedProjectStoreTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
        _store = new MedProjectStore(_paths, NullLogger<MedProjectStore>.Instance);
        Directory.CreateDirectory(ProjectsDir);
        Directory.CreateDirectory(MediaDir);
    }

    private string ProjectsDir => Path.Combine(_root, "projetos");

    private string MediaDir => Path.Combine(_root, "midias");

    private static MediaAsset AssetFor(string path, long size) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = Path.GetFileName(path),
        OriginalPath = path,
        FileSize = size,
        LastWriteUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        Fingerprint = "fingerprint-de-teste",
        Duration = MediaTime.FromSeconds(3),
        Width = 1280,
        Height = 720,
        FrameRate = FrameRate.Fps25,
    };

    /// <summary>Projeto com uma mídia, um clipe com transformações, um marcador e uma trilha bloqueada.</summary>
    private static Project BuildProject(string name, StorageMode mode, MediaAsset asset)
    {
        var project = Project.CreateNew(
            name,
            new ProjectSettings { Width = 1280, Height = 720, FrameRate = FrameRate.Fps25 },
            mode,
            Now) with { Media = [asset] };

        var seq = project.MainSequence;
        var clip = new Clip
        {
            Id = Guid.NewGuid(),
            MediaAssetId = asset.Id,
            Start = MediaTime.Zero,
            Duration = MediaTime.FromSeconds(2),
            SourceIn = MediaTime.FromSeconds(1),
            Speed = new Rational(1, 2),
            Opacity = 0.5,
            Transform = new Transform { X = 12.5, ScaleX = 1.5, CropLeft = 3 },
        };

        var tracks = seq.Tracks
            .SetItem(0, seq.Tracks[0] with { Clips = [clip] })
            .SetItem(1, seq.Tracks[1] with { IsLocked = true });
        var markers = new[]
        {
            new Marker { Id = Guid.NewGuid(), Time = FrameRate.Fps25.FrameToTime(50), Name = "Corte" },
        };

        return project.WithSequence(seq with { Tracks = tracks, Markers = [.. markers] });
    }

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

    /// <summary>Reescreve uma entrada do ZIP (.med) aplicando a transformação ao texto.</summary>
    private static void RewriteEntry(string zipPath, string entryName, Func<string, string> transform)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"Entrada ausente: {entryName}");

        string original;
        using (var reader = new StreamReader(entry.Open()))
            original = reader.ReadToEnd();

        var updated = transform(original);
        Assert.NotEqual(original, updated);

        entry.Delete();
        var replacement = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(replacement.Open());
        writer.Write(updated);
    }

    private async Task<string> SaveLinkedAsync(string fileName)
    {
        var media = WriteFile(Path.Combine(MediaDir, "clip.mp4"), RandomBytes(2048));
        var project = BuildProject("Projeto teste", StorageMode.Linked, AssetFor(media, 2048));
        var target = Path.Combine(ProjectsDir, fileName);

        await _store.SaveAsync(new SaveRequest(project, target, IncludeMedia: false, new Dictionary<Guid, string>()));
        return target;
    }

    [Fact]
    public async Task Salvar_e_recarregar_projeto_vinculado_preserva_o_conteudo()
    {
        var media = WriteFile(Path.Combine(MediaDir, "clip.mp4"), RandomBytes(4096));
        var project = BuildProject("Projeto com acentuação", StorageMode.Linked, AssetFor(media, 4096));
        var target = Path.Combine(ProjectsDir, "linked.med");

        var saved = await _store.SaveAsync(new SaveRequest(project, target, IncludeMedia: false, new Dictionary<Guid, string>()));
        var loaded = await _store.LoadAsync(target);

        Assert.Equal(Path.GetFullPath(target), loaded.FilePath);
        Assert.Equal(MedJsonVersion, loaded.FormatVersionOnDisk);
        Assert.False(loaded.WasMigrated);

        var reloaded = loaded.Project;
        Assert.Equal(project.Id, reloaded.Id);
        Assert.Equal("Projeto com acentuação", reloaded.Name);
        Assert.Equal(project.Settings, reloaded.Settings);
        Assert.Equal(StorageMode.Linked, reloaded.StorageMode);

        var expectedSeq = saved.MainSequence;
        var actualSeq = reloaded.MainSequence;
        Assert.Equal(expectedSeq.Name, actualSeq.Name);
        Assert.Equal(
            expectedSeq.Tracks.Select(t => (t.Id, t.Kind, t.Name, t.IsLocked, t.IsHidden, t.IsMuted)).ToList(),
            actualSeq.Tracks.Select(t => (t.Id, t.Kind, t.Name, t.IsLocked, t.IsHidden, t.IsMuted)).ToList());
        Assert.Equal(
            expectedSeq.Tracks.SelectMany(t => t.Clips).ToList(),
            actualSeq.Tracks.SelectMany(t => t.Clips).ToList());
        Assert.Equal(expectedSeq.Markers.ToList(), actualSeq.Markers.ToList());

        Assert.Equal(saved.Media.ToList(), reloaded.Media.ToList());
        var asset = Assert.Single(reloaded.Media);
        Assert.NotNull(asset.RelativePath);
        Assert.Null(asset.PackagedEntry);
    }

    [Fact]
    public async Task Salvar_empacotado_grava_a_midia_dentro_do_med_e_carrega()
    {
        var source = WriteFile(Path.Combine(MediaDir, "take.mp4"), RandomBytes(300_000));
        var asset = AssetFor(source, 300_000);
        var project = BuildProject("Empacotado", StorageMode.Packaged, asset);
        var target = Path.Combine(ProjectsDir, "pack.med");

        var saved = await _store.SaveAsync(new SaveRequest(
            project, target, IncludeMedia: true, new Dictionary<Guid, string> { [asset.Id] = source }));

        using (var zip = ZipFile.OpenRead(target))
        {
            var mediaEntry = Assert.Single(zip.Entries, e => e.FullName.StartsWith("media/", StringComparison.Ordinal));
            Assert.StartsWith("media/videos/", mediaEntry.FullName, StringComparison.Ordinal);
            Assert.Equal(300_000L, mediaEntry.Length);
        }

        var loaded = await _store.LoadAsync(target);
        var packaged = Assert.Single(loaded.Project.Media);
        var entry = packaged.PackagedEntry;
        Assert.NotNull(entry);
        Assert.StartsWith("media/videos/", entry, StringComparison.Ordinal);
        Assert.Equal(saved.Media[0].PackagedEntry, entry);
        Assert.Equal(StorageMode.Packaged, loaded.Project.StorageMode);
    }

    [Fact]
    public async Task Projeto_com_conteudo_adulterado_falha_na_verificacao_de_integridade()
    {
        var target = await SaveLinkedAsync("tampered.med");
        RewriteEntry(target, "project.json", text => text.Replace("Projeto teste", "Adulterado"));

        var ex = await Assert.ThrowsAsync<MedException>(() => _store.LoadAsync(target));

        Assert.Equal(MedErrors.IntegrityFailed("project.json").Code, ex.Error.Code);
    }

    [Fact]
    public async Task Manifesto_com_major_mais_novo_e_recusado()
    {
        var target = await SaveLinkedAsync("future.med");
        RewriteEntry(target, "manifest.json", text => text.Replace("\"formatVersion\": \"1.1\"", "\"formatVersion\": \"2.0\""));

        var ex = await Assert.ThrowsAsync<MedException>(() => _store.LoadAsync(target));

        Assert.Equal(MedErrors.NewerFormat("2.0", "1.1").Code, ex.Error.Code);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_zip_e_recusado_como_nao_projeto()
    {
        var path = WriteFile(Path.Combine(ProjectsDir, "fake.med"), "isto nao e um zip"u8.ToArray());

        var ex = await Assert.ThrowsAsync<MedException>(() => _store.LoadAsync(path));

        Assert.Equal(MedErrors.NotAProject(path).Code, ex.Error.Code);
    }

    [Fact]
    public async Task Arquivo_inexistente_retorna_erro_de_arquivo_nao_encontrado()
    {
        var path = Path.Combine(ProjectsDir, "nao-existe.med");

        var ex = await Assert.ThrowsAsync<MedException>(() => _store.LoadAsync(path));

        Assert.Equal(MedErrors.ProjectFileNotFound(path).Code, ex.Error.Code);
    }

    [Fact]
    public async Task Falha_ao_salvar_preserva_o_arquivo_anterior_byte_a_byte()
    {
        var target = await SaveLinkedAsync("atomic.med");
        var before = await File.ReadAllBytesAsync(target);

        var missingSource = Path.Combine(MediaDir, "sumiu.mp4");
        var asset = AssetFor(missingSource, 10);
        var project = BuildProject("Atomico", StorageMode.Packaged, asset);

        var ex = await Assert.ThrowsAsync<MedException>(() => _store.SaveAsync(new SaveRequest(
            project, target, IncludeMedia: true, new Dictionary<Guid, string> { [asset.Id] = missingSource })));

        Assert.Equal(MedErrors.MediaUnavailableForPackaging(asset.Name).Code, ex.Error.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(target));
        Assert.Empty(Directory.GetFiles(ProjectsDir, "*.tmp"));
    }

    [Fact]
    public async Task CreateBackupAsync_grava_na_pasta_de_backups_e_mantem_no_maximo_10()
    {
        var target = await SaveLinkedAsync("backup.med");
        var projectId = (await _store.LoadAsync(target)).Project.Id;

        var created = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var backup = await _store.CreateBackupAsync(target, projectId);
            Assert.NotNull(backup);
            created.Add(backup);
            await Task.Delay(5); // nomes com precisão de milissegundos: evita colisão entre cópias
        }

        Assert.StartsWith(_paths.Backups, created[^1], StringComparison.OrdinalIgnoreCase);
        var dir = Path.Combine(_paths.Backups, projectId.ToString("N"));
        var files = Directory.GetFiles(dir, "*.med");
        Assert.Equal(10, files.Length);
    }

    [Fact]
    public async Task CreateBackupAsync_retorna_nulo_quando_o_arquivo_nao_existe()
    {
        var result = await _store.CreateBackupAsync(Path.Combine(ProjectsDir, "nada.med"), Guid.NewGuid());

        Assert.Null(result);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
