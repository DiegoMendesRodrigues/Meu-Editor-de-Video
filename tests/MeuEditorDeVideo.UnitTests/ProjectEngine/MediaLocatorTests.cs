// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.ProjectEngine;

public sealed class MediaLocatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-locator-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly MediaLocator _locator;
    private readonly MedProjectStore _store;

    public MediaLocatorTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
        _locator = new MediaLocator(_paths, NullLogger<MediaLocator>.Instance);
        _store = new MedProjectStore(_paths, NullLogger<MedProjectStore>.Instance);
    }

    private static MediaAsset Asset(string originalPath, long size = 0, string? relative = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = Path.GetFileName(originalPath),
        OriginalPath = originalPath,
        RelativePath = relative,
        FileSize = size,
    };

    private static Project ProjectWith(params MediaAsset[] assets) =>
        Project.CreateNew("Teste", new ProjectSettings(), StorageMode.Linked, Now) with { Media = [.. assets] };

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
    public async Task Caminho_absoluto_existente_fica_disponivel()
    {
        var path = WriteFile(Path.Combine(_root, "midia", "a.mp4"), RandomBytes(1000));
        var asset = Asset(path, 1000);

        var resolution = await _locator.ResolveAsync(ProjectWith(asset), null);

        var location = resolution.Locations[asset.Id];
        Assert.Equal(MediaStatus.Available, location.Status);
        Assert.Equal(path, location.ResolvedPath);
    }

    [Fact]
    public async Task Arquivo_apagado_fica_ausente()
    {
        var path = WriteFile(Path.Combine(_root, "midia", "b.mp4"), RandomBytes(500));
        var asset = Asset(path, 500);
        File.Delete(path);

        var resolution = await _locator.ResolveAsync(ProjectWith(asset), null);

        var location = resolution.Locations[asset.Id];
        Assert.Equal(MediaStatus.Missing, location.Status);
        Assert.Null(location.ResolvedPath);
        Assert.Contains(resolution.Missing, l => l.AssetId == asset.Id);
    }

    [Fact]
    public async Task Arquivo_movido_para_junto_do_med_e_encontrado_pelo_caminho_relativo()
    {
        var projectDir = Path.Combine(_root, "projeto");
        var projectFile = Path.Combine(projectDir, "obra.med");
        var moved = WriteFile(Path.Combine(projectDir, "media", "clip.mp4"), RandomBytes(777));
        var asset = Asset(Path.Combine(_root, "antigo", "clip.mp4"), 777, relative: Path.Combine("media", "clip.mp4"));

        var resolution = await _locator.ResolveAsync(ProjectWith(asset), projectFile);

        var location = resolution.Locations[asset.Id];
        Assert.Equal(MediaStatus.Available, location.Status);
        Assert.Equal(Path.GetFullPath(moved), location.ResolvedPath);
    }

    [Fact]
    public async Task Projeto_empacotado_extrai_a_midia_para_o_cache_e_retorna_caminho_existente()
    {
        var source = WriteFile(Path.Combine(_root, "origem", "take.mp4"), RandomBytes(200_000));
        var originalBytes = await File.ReadAllBytesAsync(source);
        var asset = Asset(source, 200_000);
        var target = Path.Combine(_root, "projeto", "pacote.med");
        await _store.SaveAsync(new SaveRequest(
            ProjectWith(asset) with { StorageMode = StorageMode.Packaged },
            target,
            IncludeMedia: true,
            new Dictionary<Guid, string> { [asset.Id] = source }));

        File.Delete(source); // so a extracao do .med pode resolver a mídia
        var loaded = await _store.LoadAsync(target);

        var resolution = await _locator.ResolveAsync(loaded.Project, target);

        var location = resolution.Locations[asset.Id];
        Assert.Equal(MediaStatus.Available, location.Status);
        var resolved = location.ResolvedPath;
        Assert.NotNull(resolved);
        Assert.StartsWith(_locator.ExtractionDirectory(loaded.Project.Id), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(resolved));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(resolved));
    }

    [Theory]
    [InlineData(@"C:\Videos\Viagem\clip.mp4")]
    [InlineData("C:/Videos/Viagem/clip.mp4")]
    [InlineData(@"\\servidor\videos\clip.mp4")]
    public async Task FindCandidatesAsync_encontra_midia_de_projeto_feito_no_Windows(string original)
    {
        var bytes = RandomBytes(4096);
        var busca = Path.Combine(_root, "busca-windows");
        var copia = WriteFile(Path.Combine(busca, "clip.mp4"), bytes);

        var candidato = Assert.Single(await _locator.FindCandidatesAsync([Asset(original, bytes.Length)], busca));

        Assert.Equal(copia, candidato.Path);
    }

    [Fact]
    public async Task Caminho_relativo_gravado_no_Windows_localiza_a_midia_ao_lado_do_projeto()
    {
        var bytes = RandomBytes(4096);
        var copia = WriteFile(Path.Combine(_root, "projeto", "videos", "clip.mp4"), bytes);
        var asset = Asset(@"C:\Inexistente\videos\clip.mp4", bytes.Length, @"videos\clip.mp4");
        var projeto = Project.CreateNew("Windows", new ProjectSettings(), StorageMode.Linked, Now) with { Media = [asset] };

        var resolvido = await _locator.ResolveAsync(projeto, Path.Combine(_root, "projeto", "obra.med"));

        Assert.Equal(copia, resolvido.AvailablePaths[asset.Id]);
    }

    [Fact]
    public async Task Salvar_fora_do_Windows_mantem_o_relativo_de_um_projeto_do_Windows()
    {
        var asset = Asset(@"C:\Inexistente\videos\clip.mp4", 10, @"videos\clip.mp4");
        var projeto = Project.CreateNew("Windows", new ProjectSettings(), StorageMode.Linked, Now) with { Media = [asset] };

        var salvo = await _store.SaveAsync(new SaveRequest(projeto, Path.Combine(_root, "obra.med"), IncludeMedia: false, new Dictionary<Guid, string>()));

        // No Windows o caminho é nativo e o relativo é recalculado para a pasta nova.
        if (!OperatingSystem.IsWindows()) Assert.Equal(asset.RelativePath, salvo.Media[0].RelativePath);
        else Assert.NotEqual(asset.RelativePath, salvo.Media[0].RelativePath);
    }

    [Fact]
    public async Task FindCandidatesAsync_encontra_copia_em_pasta_renomeada_com_mesmo_nome_e_tamanho()
    {
        var bytes = RandomBytes(2_500_000);
        var original = WriteFile(Path.Combine(_root, "Pasta Original", "clip.mp4"), bytes);
        var asset = Asset(original, bytes.Length) with
        {
            Fingerprint = await _locator.ComputeFingerprintAsync(original),
        };
        File.Delete(original);

        var searchFolder = Path.Combine(_root, "busca");
        var copy = WriteFile(Path.Combine(searchFolder, "Pasta Renomeada", "clip.mp4"), bytes);

        var candidates = await _locator.FindCandidatesAsync([asset], searchFolder);

        var candidate = Assert.Single(candidates);
        Assert.Equal(asset.Id, candidate.AssetId);
        Assert.Equal(copy, candidate.Path);
        Assert.True(candidate.FingerprintMatches);
    }

    [Fact]
    public async Task FindCandidatesAsync_prioriza_candidato_com_impressao_digital_confirmada()
    {
        var bytes = RandomBytes(2_500_000);
        var original = WriteFile(Path.Combine(_root, "Pasta Original", "clip.mp4"), bytes);
        var asset = Asset(original, bytes.Length) with
        {
            Fingerprint = await _locator.ComputeFingerprintAsync(original),
        };
        File.Delete(original);

        var searchFolder = Path.Combine(_root, "busca");
        var copy = WriteFile(Path.Combine(searchFolder, "a", "clip.mp4"), bytes);
        // mesmo nome e tamanho, conteudo diferente: nao deve confirmar a impressao digital
        WriteFile(Path.Combine(searchFolder, "b", "clip.mp4"), RandomBytes(bytes.Length));

        var candidates = await _locator.FindCandidatesAsync([asset], searchFolder);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(copy, candidates[0].Path);
        Assert.True(candidates[0].FingerprintMatches);
        Assert.False(candidates[1].FingerprintMatches);
    }

    [Fact]
    public void Projeto_baixado_da_internet_nao_consulta_servidores_de_rede()
    {
        // Consultar \\servidor\... envia as credenciais do Windows (NTLM) ao servidor: num .med com a marca da Web
        // (zona Internet), só caminhos locais ou do mesmo servidor do projeto são procurados.
        Directory.CreateDirectory(_root);
        var local = Path.Combine(_root, "local.med");
        var baixado = Path.Combine(_root, "baixado.med");
        File.WriteAllText(local, "x");
        File.WriteAllText(baixado, "x");
        File.WriteAllText(baixado + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        const string rede = @"\\servidor\videos\clipe.mp4";

        bool? z1 = null, z2 = null, z3 = null, z4 = null;
        Assert.True(MediaLocator.IsFromInternet(baixado));
        Assert.False(MediaLocator.IsFromInternet(local));
        Assert.True(MediaLocator.IsAllowedPath(rede, local, ref z1));
        Assert.False(MediaLocator.IsAllowedPath(rede, baixado, ref z2));
        Assert.True(MediaLocator.IsAllowedPath(rede, @"\\SERVIDOR\projetos\p.med", ref z3));
        Assert.True(MediaLocator.IsAllowedPath(Path.Combine(_root, "clipe.mp4"), baixado, ref z4));
        Assert.False(MediaLocator.IsAllowedPath(@"\\.\PhysicalDrive0", null, ref z4));
        Assert.False(MediaLocator.IsAllowedPath(@"\\?\UNC\servidor\videos\clipe.mp4", null, ref z4));
        Assert.False(MediaLocator.IsAllowedPath("http://exemplo.com/clipe.mp4", null, ref z4));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
