using System.IO.Compression;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;
using MeuEditorDeVideo.ProjectEngine;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>
/// §14.2 (6): Motor de Projeto ↔ arquivos .med. Salvar, fechar, reabrir (vinculado e empacotado) e tratar arquivos
/// danificados, sempre pelos serviços reais do DI e sem tocar nos arquivos do usuário.
/// </summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class ProjectMedIntegrationTests : IDisposable
{
    private readonly IntegrationMedia _media;
    private readonly AppContainer _app;

    public ProjectMedIntegrationTests(IntegrationMedia media)
    {
        _media = media;
        _app = new AppContainer(TempFiles.NewRoot("projeto-med"));
    }

    private IProjectService Projects => _app.Get<IProjectService>();

    /// <summary>Cria um projeto com uma cópia do vídeo de exemplo (na pasta da raiz de teste) e um marcador.</summary>
    private async Task<string> ProjetoComMidiaAsync(StorageMode modo)
    {
        Directory.CreateDirectory(_app.ProjectsDir);
        await Projects.CreateAsync(new NewProjectRequest("Filme de teste", _app.ProjectsDir,
            new ProjectSettings { Width = 640, Height = 360, FrameRate = FrameRate.Fps25 }, modo));
        var copia = Path.Combine(_app.Root, "midias", "clipe.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
        File.Copy(_media.Video, copia);

        await _app.Get<IMediaImportService>().ImportAsync([copia]);
        var seq = Projects.Current!.Project.MainSequence;
        Projects.Execute(new AddMarkerCommand(seq.Id, MediaTime.FromSecondsApprox(1.5), "Corte"));
        return Projects.Current.FilePath!;
    }

    [Fact]
    public async Task Salvar_fechar_e_reabrir_projeto_vinculado_preserva_o_conteudo()
    {
        var path = await ProjetoComMidiaAsync(StorageMode.Linked);
        await Projects.SaveAsync();
        var antes = Projects.Current!.Project;

        await Projects.CloseAsync();
        Assert.Null(Projects.Current);
        await Projects.OpenAsync(path);

        var depois = Projects.Current!.Project;
        Assert.Equal(antes.Id, depois.Id);
        Assert.Equal("Filme de teste", depois.Name);
        Assert.Equal(antes.Settings, depois.Settings);
        Assert.Equal(StorageMode.Linked, depois.StorageMode);
        Assert.Equal(antes.Media.ToList(), depois.Media.ToList());
        Assert.Equal(antes.MainSequence.Markers.ToList(), depois.MainSequence.Markers.ToList());
        Assert.Equal(
            antes.MainSequence.Tracks.SelectMany(t => t.Clips).ToList(),
            depois.MainSequence.Tracks.SelectMany(t => t.Clips).ToList());
        Assert.False(Projects.Current.IsDirty);
        Assert.False(Projects.Current.HasMissingMedia);
    }

    [Fact]
    public async Task Projeto_empacotado_grava_a_midia_dentro_do_med_e_reabre_com_a_entrada()
    {
        var path = await ProjetoComMidiaAsync(StorageMode.Packaged);

        await Projects.PackageMediaAsync();
        await Projects.CloseAsync();

        using (var zip = ZipFile.OpenRead(path))
        {
            var midia = Assert.Single(zip.Entries, e => e.FullName.StartsWith("media/", StringComparison.Ordinal));
            Assert.Equal(new FileInfo(_media.Video).Length, midia.Length);
        }

        await Projects.OpenAsync(path);
        var asset = Assert.Single(Projects.Current!.Project.Media);
        Assert.StartsWith("media/", asset.PackagedEntry, StringComparison.Ordinal);
        Assert.Equal(StorageMode.Packaged, Projects.Current.Project.StorageMode);
    }

    [Fact]
    public async Task Med_corrompido_gera_erro_em_portugues_e_nao_derruba_o_servico()
    {
        var path = await ProjetoComMidiaAsync(StorageMode.Linked);
        await Projects.SaveAsync();
        await Projects.CloseAsync();

        var corrompido = Path.Combine(_app.ProjectsDir, "corrompido.med");
        File.WriteAllBytes(corrompido, File.ReadAllBytes(path)[..200]); // começo de um ZIP sem diretório central

        var ex = await Assert.ThrowsAsync<MedException>(() => Projects.OpenAsync(corrompido));

        Assert.Equal(MedErrors.NotAProject(corrompido).Code, ex.Error.Code);
        Assert.Contains("não é um projeto válido", ex.Error.Message);
        Assert.Null(Projects.Current);

        // O serviço continua utilizável: um projeto íntegro abre normalmente em seguida.
        await Projects.OpenAsync(path);
        Assert.NotNull(Projects.Current);
    }

    [Fact]
    public async Task Arquivo_com_texto_no_lugar_de_projeto_gera_erro_em_portugues()
    {
        var falso = Path.Combine(_app.ProjectsDir, "falso.med");
        Directory.CreateDirectory(_app.ProjectsDir);
        await File.WriteAllTextAsync(falso, "isto não é um projeto");

        var ex = await Assert.ThrowsAsync<MedException>(() => Projects.OpenAsync(falso));

        Assert.Equal("MED-1002", ex.Error.Code);
        Assert.Contains("não é um projeto válido", ex.Error.Message);
    }

    public void Dispose() => _app.Dispose();
}
