// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.UnitTests.AI;

/// <summary>Catálogo oficial, geração de pacotes .medmodel e instalação com dependências (ferramenta "Baixar modelos").</summary>
public sealed class ModelCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-catalogo-" + Guid.NewGuid().ToString("N")[..8]);

    public ModelCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string RepoCatalog()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "resources", "ai-models");
            if (File.Exists(Path.Combine(c, "modelos.json"))) return c;
        }
        throw new InvalidOperationException("resources/ai-models não encontrado");
    }

    private static ModelCatalogEntry Entry(string id, byte[] content, params string[] dependsOn) => new()
    {
        Id = id, Name = "Modelo " + id, Version = "1.0", Source = "teste", Capability = ModelCapability.VoiceActivity,
        Purpose = "teste", Features = ["Recurso de teste"], File = id + ".onnx", License = "MIT", LicenseFile = "lic.txt",
        MemoryMb = 100, DependsOn = dependsOn, Url = new Uri($"https://huggingface.co/x/{id}/resolve/abc/{id}.onnx"),
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), SizeBytes = content.Length,
    };

    private ModelCatalog Catalog(params ModelCatalogEntry[] entries)
    {
        var lic = Path.Combine(_dir, "licencas");
        Directory.CreateDirectory(lic);
        File.WriteAllText(Path.Combine(lic, "lic.txt"), "MIT License");
        return new ModelCatalog(entries, lic);
    }

    private static byte[] Bytes(int seed, int n = 4096) => Enumerable.Range(0, n).Select(i => (byte)(i * seed % 251)).ToArray();

    [Fact]
    public void Catalogo_do_repositorio_e_valido_e_tem_licencas()
    {
        var catalog = ModelCatalog.Load(RepoCatalog());
        Assert.Equal(["whisper-large-v3-turbo", "gtcrn-voz", "silero-vad", "transnetv2", "swin2sr-x2", "facemesh-v2", "phi4-mini", "phi4-mini-cpu"], catalog.Entries.Select(e => e.Id));
        Assert.All(catalog.Entries, e =>
        {
            Assert.True(File.Exists(catalog.LicensePath(e)), e.LicenseFile);
            Assert.NotEmpty(e.Features);
            Assert.Equal(Uri.UriSchemeHttps, e.Url.Scheme);
            Assert.DoesNotContain("/main/", e.Url.AbsolutePath); // endereço fixado numa revisão
            Assert.DoesNotContain("/master/", e.Url.AbsolutePath);
        });
        Assert.Equal(["silero-vad"], catalog.Find("whisper-large-v3-turbo")!.DependsOn);

        // Assistente virtual: Phi-4-mini em vários arquivos, todos no mesmo commit da origem oficial.
        var phi = catalog.Find("phi4-mini")!;
        Assert.Equal(ModelCapability.HelpAssistant, phi.Capability);
        Assert.Equal("model.onnx", phi.File);
        Assert.Contains(phi.ExtraFiles, f => f.File == "model.onnx.data" && f.SizeBytes > 3_000_000_000L);
        Assert.Contains(phi.ExtraFiles, f => f.File == "genai_config.json");
        Assert.Contains(phi.ExtraFiles, f => f.File == "tokenizer.json");
        var commit = phi.Url.Segments[4];
        Assert.All(phi.ExtraFiles, f => Assert.Equal(commit, f.Url.Segments[4]));
        Assert.True(phi.TotalBytes > phi.SizeBytes);
        Assert.Equal(["windows"], phi.Plataformas); // versão para DirectML
        Assert.Contains("/gpu/", phi.Url.AbsolutePath);

        // Mesma capacidade, versão para CPU (Linux e macOS), no mesmo commit da origem oficial.
        var cpu = catalog.Find("phi4-mini-cpu")!;
        Assert.Equal(ModelCapability.HelpAssistant, cpu.Capability);
        Assert.Equal(["linux", "macos"], cpu.Plataformas);
        Assert.False(cpu.Gpu);
        Assert.Contains("/cpu_and_mobile/", cpu.Url.AbsolutePath);
        Assert.Equal(commit, cpu.Url.Segments[4]);
        Assert.All(cpu.ExtraFiles, f => Assert.Contains("/cpu_and_mobile/", f.Url.AbsolutePath));
        Assert.Contains(cpu.ExtraFiles, f => f.File == "model.onnx.data" && f.SizeBytes > 4_000_000_000L);
    }

    [Fact]
    public void No_macOS_Intel_o_catalogo_oferece_so_o_Whisper_e_sem_o_VAD()
    {
        var catalog = ModelCatalog.Load(RepoCatalog());
        var intel = catalog.EntriesFor(AiOs.MacOs, System.Runtime.InteropServices.Architecture.X64);
        Assert.Equal(["whisper-large-v3-turbo"], intel.Select(e => e.Id));
        Assert.Equal(catalog.EntriesFor(AiOs.MacOs).Select(e => e.Id),
            catalog.EntriesFor(AiOs.MacOs, System.Runtime.InteropServices.Architecture.Arm64).Select(e => e.Id));

        Assert.Equal(["whisper-large-v3-turbo"], catalog.InstallOrder(["whisper-large-v3-turbo"], onnxRuntime: false).Select(e => e.Id));
        Assert.Equal(["silero-vad", "whisper-large-v3-turbo"], catalog.InstallOrder(["whisper-large-v3-turbo"], onnxRuntime: true).Select(e => e.Id));
        Assert.Empty(catalog.DependenciesOf(intel[0], onnxRuntime: false));
        Assert.All(catalog.EntriesForCurrentOs, e => Assert.True(AiPlatform.Supports(e.Capability), e.Id));
    }

    [Fact]
    public void Catalogo_filtra_as_entradas_pelo_sistema()
    {
        var catalog = ModelCatalog.Load(RepoCatalog());
        var win = catalog.EntriesFor(AiOs.Windows).Select(e => e.Id).ToList();
        var linux = catalog.EntriesFor(AiOs.Linux).Select(e => e.Id).ToList();
        var mac = catalog.EntriesFor(AiOs.MacOs).Select(e => e.Id).ToList();
        Assert.Contains("phi4-mini", win);
        Assert.DoesNotContain("phi4-mini-cpu", win);
        Assert.Contains("phi4-mini-cpu", linux);
        Assert.DoesNotContain("phi4-mini", linux);
        Assert.Contains("phi4-mini-cpu", mac);
        Assert.DoesNotContain("phi4-mini", mac);
        // Cada sistema oferece exatamente um modelo do assistente virtual; os demais modelos valem para todos.
        Assert.All(new[] { win, linux, mac }, ids => Assert.Single(ids, i => i.StartsWith("phi4-mini", StringComparison.Ordinal)));
        Assert.All(catalog.Entries.Where(e => !e.Id.StartsWith("phi4-mini", StringComparison.Ordinal)), e => Assert.Empty(e.Plataformas));
        Assert.Equal(catalog.Entries.Count - 1, win.Count);
    }

    [Fact]
    public void Plataformas_validas_vazio_vale_para_todos_e_dependencia_precisa_existir_no_sistema()
    {
        var a = Entry("a", Bytes(1));
        Assert.True(a.AvailableOn("windows") && a.AvailableOn("linux") && a.AvailableOn("macos"));
        var onlyMac = Entry("m", Bytes(2)) with { Plataformas = ["MacOS"] };
        Assert.True(onlyMac.AvailableOn("macos"));
        Assert.False(onlyMac.AvailableOn("linux"));
        Assert.Equal(["m"], Catalog(a, onlyMac).EntriesFor(AiOs.MacOs).Where(e => e.Id == "m").Select(e => e.Id));
        Assert.Throws<InvalidDataException>(() => Catalog(a with { Plataformas = ["amiga"] }));
        // 'x' vale em todos os sistemas, mas depende de 'm', que só existe no macOS.
        Assert.Throws<InvalidDataException>(() => Catalog(onlyMac, Entry("x", Bytes(3), "m")));
        Assert.NotNull(Catalog(onlyMac, Entry("x", Bytes(3), "m") with { Plataformas = ["macos"] }));
    }

    [Fact]
    public void Plataformas_do_json_sao_lidas()
    {
        var entry = Entry("j", Bytes(4)) with { Plataformas = ["linux"] };
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { entry }, ModelManifestJson.Options);
        Assert.Contains("\"plataformas\"", json);
        File.WriteAllText(Path.Combine(_dir, "modelos.json"), json);
        var catalog = ModelCatalog.Load(_dir);
        Assert.Equal(["linux"], catalog.Entries[0].Plataformas);
        Assert.Empty(catalog.EntriesFor(AiOs.Windows));
        Assert.Single(catalog.EntriesFor(AiOs.Linux));
    }

    private static ModelCatalogFile Extra(string id, string name, byte[] content) => new()
    {
        File = name, Url = new Uri($"https://huggingface.co/x/{id}/resolve/abc/{name}"),
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), SizeBytes = content.Length,
    };

    [Fact]
    public void Arquivos_adicionais_invalidos_sao_recusados()
    {
        var entry = Entry("a", Bytes(3));
        Assert.Throws<InvalidDataException>(() => Catalog(entry with { ExtraFiles = [Extra("a", "a.onnx", Bytes(5))] })); // repete o principal
        Assert.Throws<InvalidDataException>(() => Catalog(entry with { ExtraFiles = [Extra("a", "manifest.json", Bytes(5))] }));
        Assert.Throws<InvalidDataException>(() => Catalog(entry with { ExtraFiles = [Extra("a", "x.data", Bytes(5)) with { Url = new Uri("https://exemplo.com/x.data") }] }));
        Assert.Throws<InvalidDataException>(() => Catalog(entry with { ExtraFiles = [Extra("a", @"..\x.data", Bytes(5))] }));
        Assert.Throws<InvalidDataException>(() => Catalog(entry with { ExtraFiles = [Extra("a", "x.data", Bytes(5)), Extra("a", "X.DATA", Bytes(7))] }));
    }

    [Fact]
    public async Task Modelo_de_varios_arquivos_e_baixado_empacotado_instalado_e_verificado()
    {
        byte[] main = Bytes(3), data = Bytes(5, 9000), config = Bytes(7, 300);
        var entry = Entry("lm", main) with { ExtraFiles = [Extra("lm", "model.onnx.data", data), Extra("lm", "genai_config.json", config)] };
        var catalog = Catalog(entry);
        var downloader = new FakeDownloader(new()
        {
            [entry.Url] = main, [entry.ExtraFiles[0].Url] = data, [entry.ExtraFiles[1].Url] = config,
        });
        var registry = new ModelRegistry(Path.Combine(_dir, "instalados"));
        var installer = new CatalogModelInstaller(catalog, registry, downloader, Path.Combine(_dir, "trabalho"));
        var plan = installer.Plan(["lm"]);
        Assert.Equal(main.Length + data.Length + config.Length, installer.DownloadBytes(plan));
        var progress = new List<ModelInstallProgress>();

        await installer.InstallAsync(plan, new SyncProgress(progress.Add));

        Assert.Equal(3, downloader.Calls.Count);
        Assert.All(progress, p => Assert.Equal(entry.TotalBytes, p.TotalBytes));
        var installed = Assert.Single(registry.Installed);
        Assert.Equal(["model.onnx.data", "genai_config.json"], installed.Manifest.ExtraFiles.Select(f => f.File));
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(installed.Directory, "model.onnx.data")));
        Assert.Equal(entry.TotalBytes, installed.Manifest.TotalBytes);
        Assert.True(await registry.VerifyAsync("lm"));
        Assert.False(Directory.Exists(Path.Combine(_dir, "trabalho", "downloads", "lm")));

        // Um arquivo adicional alterado depois de instalado é detectado.
        await File.WriteAllBytesAsync(Path.Combine(installed.Directory, "genai_config.json"), Bytes(11, 300));
        Assert.False(await registry.VerifyAsync("lm"));
    }

    [Fact]
    public async Task Pacote_com_arquivo_adicional_corrompido_e_recusado()
    {
        byte[] main = Bytes(3), data = Bytes(5, 9000);
        var entry = Entry("lm", main) with { ExtraFiles = [Extra("lm", "model.onnx.data", data)] };
        var catalog = Catalog(entry);
        await File.WriteAllBytesAsync(Path.Combine(_dir, entry.File), main);
        await File.WriteAllBytesAsync(Path.Combine(_dir, "model.onnx.data"), data);
        var package = Path.Combine(_dir, "lm.medmodel");
        await ModelPackageBuilder.BuildAsync(entry, Path.Combine(_dir, entry.File), catalog.LicensePath(entry), package);
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            zip.GetEntry("model.onnx.data")!.Delete();
            using var w = zip.CreateEntry("model.onnx.data").Open();
            w.Write(Bytes(13, 9000));
        }

        var registry = new ModelRegistry(Path.Combine(_dir, "instalados"));
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(package));
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public void Ordem_de_instalacao_poe_dependencias_primeiro_sem_repetir()
    {
        var catalog = Catalog(Entry("a", Bytes(3), "b"), Entry("b", Bytes(5), "c"), Entry("c", Bytes(7)), Entry("d", Bytes(11)));
        Assert.Equal(["c", "b", "a", "d"], catalog.InstallOrder(["a", "d", "b"], onnxRuntime: true).Select(e => e.Id));
    }

    [Fact]
    public void Catalogo_invalido_e_recusado()
    {
        Assert.Throws<InvalidDataException>(() => Catalog(Entry("a", Bytes(3), "inexistente")));
        Assert.Throws<InvalidDataException>(() => Catalog(Entry("a", Bytes(3)) with { Url = new Uri("http://huggingface.co/a.onnx") }));
        Assert.Throws<InvalidDataException>(() => Catalog(Entry("a", Bytes(3)) with { Url = new Uri("https://exemplo.com/a.onnx") }));
        Assert.Throws<InvalidDataException>(() => Catalog(Entry("a", Bytes(3)) with { File = @"..\a.onnx" }));
        Assert.Throws<InvalidDataException>(() => Catalog(Entry("a", Bytes(3)), Entry("a", Bytes(5))));
        var circular = Catalog(Entry("a", Bytes(3), "b"), Entry("b", Bytes(5), "a"));
        Assert.Throws<InvalidDataException>(() => circular.InstallOrder(["a"], onnxRuntime: true));
    }

    [Theory]
    [InlineData("3.1", "3.0", 1)]
    [InlineData("1.0", "1.0", 0)]
    [InlineData("1", "1.0", 0)]
    [InlineData("1.9", "1.10", -1)]
    public void Versoes_sao_comparadas_numericamente(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(ModelCatalog.CompareVersions(a, b)));

    [Fact]
    public async Task Pacote_gerado_e_aceito_pelo_gerenciador()
    {
        var content = Bytes(13);
        var entry = Entry("pacote", content);
        var catalog = Catalog(entry);
        var model = Path.Combine(_dir, entry.File);
        await File.WriteAllBytesAsync(model, content);
        var package = Path.Combine(_dir, "saida", "pacote.medmodel");

        var manifest = await ModelPackageBuilder.BuildAsync(entry, model, catalog.LicensePath(entry), package);

        Assert.Equal(entry.Sha256, manifest.Sha256);
        Assert.Equal("ONNX", manifest.Format);
        using (var zip = ZipFile.OpenRead(package))
            Assert.Equal(["manifest.json", "pacote.onnx", "LICENSE.txt", "README.md"], zip.Entries.Select(e => e.FullName));
        var registry = new ModelRegistry(Path.Combine(_dir, "instalados"));
        var installed = await registry.InstallAsync(package);
        Assert.Equal("pacote", installed.Manifest.Id);
        Assert.True(await registry.VerifyAsync("pacote"));
    }

    [Fact]
    public async Task Arquivo_diferente_do_catalogo_nao_vira_pacote()
    {
        var entry = Entry("x", Bytes(13));
        var catalog = Catalog(entry);
        var model = Path.Combine(_dir, entry.File);
        await File.WriteAllBytesAsync(model, Bytes(17));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ModelPackageBuilder.BuildAsync(entry, model, catalog.LicensePath(entry), Path.Combine(_dir, "x.medmodel")));
    }

    /// <summary>"Baixa" gravando o conteúdo conhecido de cada endereço.</summary>
    private sealed class FakeDownloader(Dictionary<Uri, byte[]> files) : IModelDownloader
    {
        public List<Uri> Calls { get; } = [];

        public async Task DownloadAsync(Uri url, string destination, long expectedBytes, string sha256, IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(url);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(destination, files[url], cancellationToken);
            progress?.Report(expectedBytes);
        }
    }

    private (CatalogModelInstaller Installer, FakeDownloader Downloader, ModelRegistry Registry) Installer(params (ModelCatalogEntry Entry, byte[] Content)[] models)
    {
        var catalog = Catalog(models.Select(m => m.Entry).ToArray());
        var downloader = new FakeDownloader(models.ToDictionary(m => m.Entry.Url, m => m.Content));
        var registry = new ModelRegistry(Path.Combine(_dir, "instalados"));
        return (new CatalogModelInstaller(catalog, registry, downloader, Path.Combine(_dir, "trabalho")), downloader, registry);
    }

    [ComOnnxRuntimeFact] // sem ONNX Runtime a dependência fica de fora (No_macOS_Intel_o_catalogo_oferece_so_o_Whisper_e_sem_o_VAD)
    public async Task Instalar_baixa_empacota_e_instala_as_dependencias_antes()
    {
        byte[] vad = Bytes(3), fala = Bytes(5);
        var (installer, downloader, registry) = Installer((Entry("vad", vad), vad), (Entry("fala", fala, "vad"), fala));
        var stages = new List<string>();
        var plan = installer.Plan(["fala"]);
        Assert.Equal(["vad", "fala"], plan.Select(e => e.Id));
        Assert.Equal(vad.Length + fala.Length, installer.DownloadBytes(plan));

        await installer.InstallAsync(plan, new SyncProgress(p => stages.Add($"{p.Id}:{p.Stage}")));

        Assert.Equal(["fala", "vad"], registry.Installed.Select(m => m.Manifest.Id).Order(StringComparer.Ordinal));
        Assert.Equal(2, downloader.Calls.Count);
        Assert.Equal("vad:Downloading", stages[0]);
        Assert.Equal("fala:Done", stages[^1]);
        Assert.True(stages.IndexOf("vad:Done") < stages.IndexOf("fala:Downloading"));
        Assert.True(File.Exists(installer.PackagePath(plan[0])));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "trabalho", "downloads"))); // arquivos brutos apagados
        Assert.Equal(CatalogModelState.Installed, installer.StateOf(plan[1]));
    }

    [Fact]
    public async Task Dependencia_ja_instalada_nao_e_repetida_e_pacote_guardado_e_reaproveitado()
    {
        byte[] vad = Bytes(3), fala = Bytes(5);
        var (installer, downloader, registry) = Installer((Entry("vad", vad), vad), (Entry("fala", fala, "vad"), fala));
        await installer.InstallAsync(installer.Plan(["vad"]));
        Assert.Equal(["fala"], installer.Plan(["fala"]).Select(e => e.Id));

        await registry.RemoveAsync("vad");
        var again = installer.Plan(["vad"]);
        Assert.Equal(0, installer.DownloadBytes(again));
        await installer.InstallAsync(again);
        Assert.Single(downloader.Calls); // reinstalado a partir do pacote guardado
        Assert.Contains(registry.Installed, m => m.Manifest.Id == "vad");
    }

    [Fact]
    public async Task Versao_nova_no_catalogo_aparece_como_atualizacao()
    {
        var vad = Bytes(3);
        var (installer, _, registry) = Installer((Entry("vad", vad), vad));
        await installer.InstallAsync(installer.Plan(["vad"]));
        var newer = new CatalogModelInstaller(Catalog(Entry("vad", vad) with { Version = "2.0" }), registry,
            new FakeDownloader([]), Path.Combine(_dir, "trabalho"));
        Assert.Equal(CatalogModelState.UpdateAvailable, newer.StateOf(newer.Catalog.Entries[0]));
    }

    [Fact]
    public async Task Aviso_de_download_nao_chega_depois_do_fim()
    {
        var vad = Bytes(3);
        var entry = Entry("vad", vad);
        var delayed = new DelayedContext();
        var installer = new CatalogModelInstaller(Catalog(entry), new ModelRegistry(Path.Combine(_dir, "instalados")),
            new ImmediateDownloader(vad), Path.Combine(_dir, "trabalho"));
        var reports = new List<ModelInstallProgress>();

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(delayed);
        Task install;
        try { install = installer.InstallAsync([entry], new SyncProgress(reports.Add)); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await install;
        delayed.Deliver();

        Assert.Equal(ModelInstallStage.Done, reports[^1].Stage);
        Assert.Contains(reports, p => p.Stage == ModelInstallStage.Downloading && p.Bytes == vad.Length);
    }

    /// <summary>Conclui na própria chamada e tira o contexto, para só o progresso criado antes dela ficar retido.</summary>
    private sealed class ImmediateDownloader(byte[] content) : IModelDownloader
    {
        public Task DownloadAsync(Uri url, string destination, long expectedBytes, string sha256, IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllBytes(destination, content);
            SynchronizationContext.SetSynchronizationContext(null);
            progress?.Report(expectedBytes);
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _held = [];

        public override void Post(SendOrPostCallback d, object? state) => _held.Add((d, state));

        public void Deliver() => _held.ForEach(h => h.Callback(h.State));
    }

    [Fact]
    public async Task Cancelamento_interrompe_sem_instalar()
    {
        var vad = Bytes(3);
        var (installer, _, registry) = Installer((Entry("vad", vad), vad));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(installer.Plan(["vad"]), null, cts.Token));
        Assert.Empty(registry.Installed);
    }

    private sealed class SyncProgress(Action<ModelInstallProgress> action) : IProgress<ModelInstallProgress>
    {
        public void Report(ModelInstallProgress value) => action(value);
    }
}
