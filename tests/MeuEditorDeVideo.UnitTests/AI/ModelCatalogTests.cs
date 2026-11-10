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
        Assert.Equal(["whisper-large-v3-turbo", "gtcrn-voz", "silero-vad", "transnetv2", "swin2sr-x2"], catalog.Entries.Select(e => e.Id));
        Assert.All(catalog.Entries, e =>
        {
            Assert.True(File.Exists(catalog.LicensePath(e)), e.LicenseFile);
            Assert.NotEmpty(e.Features);
            Assert.Equal(Uri.UriSchemeHttps, e.Url.Scheme);
            Assert.DoesNotContain("/main/", e.Url.AbsolutePath); // endereço fixado numa revisão
            Assert.DoesNotContain("/master/", e.Url.AbsolutePath);
        });
        Assert.Equal(["silero-vad"], catalog.Find("whisper-large-v3-turbo")!.DependsOn);
    }

    [Fact]
    public void Ordem_de_instalacao_poe_dependencias_primeiro_sem_repetir()
    {
        var catalog = Catalog(Entry("a", Bytes(3), "b"), Entry("b", Bytes(5), "c"), Entry("c", Bytes(7)), Entry("d", Bytes(11)));
        Assert.Equal(["c", "b", "a", "d"], catalog.InstallOrder(["a", "d", "b"]).Select(e => e.Id));
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
        Assert.Throws<InvalidDataException>(() => circular.InstallOrder(["a"]));
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

    [Fact]
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
