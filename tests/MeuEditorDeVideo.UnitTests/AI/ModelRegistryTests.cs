using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.UnitTests.AI;

/// <summary>Testes do gerenciador de modelos (instalação de pacotes .medmodel, consulta e remoção).</summary>
public sealed class ModelRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly string _modelsDir;
    private readonly string _packagesDir;

    public ModelRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeuEditorDeVideo-modelos-" + Guid.NewGuid().ToString("N"));
        _modelsDir = Path.Combine(_root, "models");
        _packagesDir = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_packagesDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária de teste: falha ao limpar não invalida o teste.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }

    [Fact]
    public async Task Install_valid_package_installs_files_and_raises_Changed()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(1);
        var package = Package("valid.medmodel", Manifest(model), model, Standard());
        var changes = 0;
        registry.Changed += (_, _) => Interlocked.Increment(ref changes);

        var installed = await registry.InstallAsync(package);

        var folder = Path.Combine(_modelsDir, "modnet-photographic");
        Assert.Equal(folder, installed.Directory);
        Assert.True(File.Exists(Path.Combine(folder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(folder, "model.onnx")));
        Assert.True(File.Exists(Path.Combine(folder, "LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "README.md")));
        Assert.Single(registry.Installed);
        Assert.NotNull(registry.Find(ModelCapability.PersonSegmentation));
        Assert.True(registry.IsAvailable(ModelCapability.PersonSegmentation));
        Assert.False(registry.IsAvailable(ModelCapability.SpeechRecognition));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Install_with_wrong_hash_is_rejected_and_leaves_nothing_behind()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(2);
        var manifest = Manifest(model) with { Sha256 = new string('0', 64) };
        var package = Package("hash.medmodel", manifest, model, Standard());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(package));

        Assert.Contains("corrompido", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_modelsDir));
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public async Task Install_without_manifest_is_rejected()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(3);
        var package = BuildPackage("sem-manifesto.medmodel", [("model.onnx", model), ("LICENSE.txt", Bytes("MIT"))]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(package));

        Assert.Contains("manifest.json", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_modelsDir));
    }

    [Fact]
    public async Task Install_with_path_traversal_entry_is_rejected()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(4);
        var package = Package("traversal.medmodel", Manifest(model), model, Standard().Append(("../evil.txt", Bytes("x"))).ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(package));

        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_modelsDir));
    }

    [Fact]
    public async Task Install_with_newer_package_format_is_rejected()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(5);
        var package = Package("futuro.medmodel", Manifest(model) with { PackageFormat = 2 }, model, Standard());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(package));

        Assert.Contains("versão mais nova", ex.Message);
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public async Task Install_without_license_is_rejected()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(6);
        var package = Package("sem-licenca.medmodel", Manifest(model) with { License = " " }, model, Standard());

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(package));

        Assert.Contains("license", ex.Message);
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public async Task Install_of_file_that_is_not_a_zip_is_rejected()
    {
        var registry = new ModelRegistry(_modelsDir);
        var path = Path.Combine(_packagesDir, "falso.medmodel");
        File.WriteAllText(path, "isto não é um zip");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => registry.InstallAsync(path));

        Assert.Contains(".medmodel", ex.Message);
    }

    [Fact]
    public async Task Reinstall_with_same_id_replaces_the_files()
    {
        var registry = new ModelRegistry(_modelsDir);
        var first = FakeModel(7);
        var firstPackage = Package("v1.medmodel", Manifest(first, version: "1.0.0"), first, Standard().Append(("notas.txt", Bytes("antigo"))).ToArray());
        await registry.InstallAsync(firstPackage);

        var second = FakeModel(8);
        var secondPackage = Package("v2.medmodel", Manifest(second, version: "2.0.0"), second, Standard());
        await registry.InstallAsync(secondPackage);

        var folder = Path.Combine(_modelsDir, "modnet-photographic");
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(folder, "model.onnx")));
        Assert.False(File.Exists(Path.Combine(folder, "notas.txt")));
        var current = Assert.Single(registry.Installed);
        Assert.Equal("2.0.0", current.Manifest.Version);
    }

    [Fact]
    public async Task Find_prefers_the_higher_version_among_models_with_the_same_capability()
    {
        var registry = new ModelRegistry(_modelsDir);
        var a = FakeModel(9);
        var b = FakeModel(10);
        await registry.InstallAsync(Package("a.medmodel", Manifest(a, id: "fala-a", version: "1.9.0", capability: ModelCapability.SpeechRecognition, file: "a.bin"), a, Standard()));
        await registry.InstallAsync(Package("b.medmodel", Manifest(b, id: "fala-b", version: "1.10.0", capability: ModelCapability.SpeechRecognition, file: "b.bin"), b, Standard()));

        var found = registry.Find(ModelCapability.SpeechRecognition);

        Assert.NotNull(found);
        Assert.Equal("fala-b", found.Manifest.Id);
    }

    [Fact]
    public async Task RemoveAsync_deletes_the_folder_and_raises_Changed()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(11);
        await registry.InstallAsync(Package("remover.medmodel", Manifest(model), model, Standard()));
        var changes = 0;
        registry.Changed += (_, _) => Interlocked.Increment(ref changes);

        await registry.RemoveAsync("modnet-photographic");

        Assert.False(Directory.Exists(Path.Combine(_modelsDir, "modnet-photographic")));
        Assert.Empty(registry.Installed);
        Assert.False(registry.IsAvailable(ModelCapability.PersonSegmentation));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task VerifyAsync_is_true_for_intact_model_and_false_after_tampering()
    {
        var registry = new ModelRegistry(_modelsDir);
        var model = FakeModel(12);
        var installed = await registry.InstallAsync(Package("verificar.medmodel", Manifest(model), model, Standard()));

        Assert.True(await registry.VerifyAsync("modnet-photographic"));

        File.WriteAllText(installed.ModelPath, "adulterado");

        Assert.False(await registry.VerifyAsync("modnet-photographic"));
        Assert.False(await registry.VerifyAsync("nao-existe"));
    }

    [Fact]
    public async Task Constructor_rescans_existing_folders()
    {
        var first = new ModelRegistry(_modelsDir);
        var model = FakeModel(13);
        await first.InstallAsync(Package("rescan.medmodel", Manifest(model), model, Standard()));

        var second = new ModelRegistry(_modelsDir);

        var found = second.Find(ModelCapability.PersonSegmentation);
        Assert.NotNull(found);
        Assert.Equal("modnet-photographic", found.Manifest.Id);
        Assert.True(second.IsAvailable(ModelCapability.PersonSegmentation));
    }

    [Fact]
    public async Task Folder_with_invalid_manifest_is_ignored()
    {
        var first = new ModelRegistry(_modelsDir);
        var model = FakeModel(14);
        await first.InstallAsync(Package("ok.medmodel", Manifest(model), model, Standard()));
        var broken = Path.Combine(_modelsDir, "quebrado");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "manifest.json"), "{ isto não é json");

        var registry = new ModelRegistry(_modelsDir);

        var installed = Assert.Single(registry.Installed);
        Assert.Equal("modnet-photographic", installed.Manifest.Id);
    }

    [Fact]
    public void Manifest_json_round_trip_keeps_parameters_and_writes_enum_as_text()
    {
        var manifest = new ModelManifest
        {
            Id = "whisper-small",
            Name = "Whisper Small",
            Version = "1.0.0",
            Source = "OpenAI (Whisper)",
            Capability = ModelCapability.SpeechRecognition,
            Purpose = "Legendas automáticas",
            Format = "GGML",
            File = "ggml-small.bin",
            SizeBytes = 487601967,
            MemoryMb = 1024,
            Gpu = true,
            License = "MIT",
            LicenseFile = "LICENSE.txt",
            Sha256 = new string('a', 64),
            Documentation = "README.md",
            Parameters = new Dictionary<string, string> { ["language"] = "pt", ["sampleRate"] = "16000" },
        };

        var json = ModelManifestJson.Serialize(manifest);
        var back = ModelManifestJson.Parse(json);

        Assert.Contains("\"capability\": \"SpeechRecognition\"", json);
        Assert.Contains("\"packageFormat\": 1", json);
        Assert.Equal(ModelCapability.SpeechRecognition, back.Capability);
        Assert.Equal(manifest.Id, back.Id);
        Assert.Equal(manifest.SizeBytes, back.SizeBytes);
        Assert.Equal(manifest.MemoryMb, back.MemoryMb);
        Assert.True(back.Gpu);
        Assert.Equal(manifest.LicenseFile, back.LicenseFile);
        Assert.Equal(manifest.Sha256, back.Sha256);
        Assert.Equal(2, back.Parameters.Count);
        Assert.Equal("pt", back.Parameters["language"]);
        Assert.Equal("16000", back.Parameters["sampleRate"]);
    }

    [Fact]
    public void Parse_rejects_unknown_capability()
    {
        var json = ModelManifestJson.Serialize(Manifest(FakeModel(15))).Replace("PersonSegmentation", "Desconhecida");

        Assert.Throws<InvalidDataException>(() => ModelManifestJson.Parse(json));
    }

    [Fact]
    public void Parse_rejects_missing_required_field()
    {
        const string json = """
            {
              "id": "sem-nome",
              "version": "1.0.0",
              "source": "Teste",
              "capability": "VoiceActivity",
              "purpose": "Teste",
              "format": "ONNX",
              "file": "model.onnx",
              "license": "MIT",
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
            }
            """;

        Assert.Throws<InvalidDataException>(() => ModelManifestJson.Parse(json));
    }

    // ---------- auxiliares ----------

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Conteúdo fictício e determinístico do modelo (varia com a semente).</summary>
    private static byte[] FakeModel(int seed)
    {
        var data = new byte[4096];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static (string Entry, byte[] Data)[] Standard() =>
    [
        ("LICENSE.txt", Bytes("Licença MIT de teste.")),
        ("README.md", Bytes("# Modelo de teste")),
    ];

    private static ModelManifest Manifest(
        byte[] model,
        string id = "modnet-photographic",
        string version = "1.0.0",
        ModelCapability capability = ModelCapability.PersonSegmentation,
        string file = "model.onnx") => new()
    {
        Id = id,
        Name = "Modelo de teste " + id,
        Version = version,
        Source = "Teste automatizado",
        Capability = capability,
        Purpose = "Finalidade de teste",
        Format = "ONNX",
        File = file,
        SizeBytes = model.Length,
        MemoryMb = 256,
        Cpu = true,
        Gpu = false,
        License = "MIT",
        LicenseFile = "LICENSE.txt",
        Sha256 = Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant(),
        Documentation = "README.md",
        PackageFormat = 1,
        Parameters = new Dictionary<string, string> { ["inputSize"] = "512" },
    };

    private string Package(string name, ModelManifest manifest, byte[] model, params (string Entry, byte[] Data)[] extra)
    {
        var entries = new List<(string Entry, byte[] Data)>
        {
            ("manifest.json", Bytes(ModelManifestJson.Serialize(manifest))),
            (manifest.File, model),
        };
        entries.AddRange(extra);
        return BuildPackage(name, entries);
    }

    private string BuildPackage(string name, IEnumerable<(string Entry, byte[] Data)> entries)
    {
        var path = Path.Combine(_packagesDir, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entry, data) in entries)
            {
                var item = zip.CreateEntry(entry);
                using var stream = item.Open();
                stream.Write(data, 0, data.Length);
            }
        }
        return path;
    }
}
