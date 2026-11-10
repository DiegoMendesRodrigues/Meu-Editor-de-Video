using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;
using MeuEditorDeVideo.RenderingEngine;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>
/// §14.2 (2): Motor de Timeline ↔ Motor de Mídia. Importa mídia gerada, coloca na timeline e pede quadros à
/// pré-visualização (<see cref="PreviewEngine"/>), que usa o decodificador FFmpeg real.
/// </summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class TimelineMediaIntegrationTests : IDisposable
{
    private readonly IntegrationMedia _media;
    private readonly AppContainer _app;

    public TimelineMediaIntegrationTests(IntegrationMedia media)
    {
        _media = media;
        _app = new AppContainer(TempFiles.NewRoot("timeline-midia"));
    }

    private IProjectService Projects => _app.Get<IProjectService>();

    private async Task<MediaAsset> ImportarEColocarNaTimelineAsync(string arquivo, MediaTime duracaoImagem)
    {
        Directory.CreateDirectory(_app.ProjectsDir);
        await Projects.CreateAsync(new NewProjectRequest("Preview", _app.ProjectsDir,
            new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }, StorageMode.Linked));
        var asset = Assert.Single((await _app.Get<IMediaImportService>().ImportAsync([arquivo])).Imported);

        var seq = Projects.Current!.Project.MainSequence;
        Guid Trilha(TrackKind k) => seq.Tracks.First(t => t.Kind == k).Id;
        Projects.Execute(new AddMediaToTimelineCommand(seq.Id, asset.Id, Trilha(TrackKind.Video), Trilha(TrackKind.Audio), MediaTime.Zero, duracaoImagem));
        return asset;
    }

    /// <summary>Entrega o estado atual ao motor de pré-visualização, como a tela faz ao mudar o projeto.</summary>
    private void Publicar(PreviewEngine preview)
    {
        var project = Projects.Current!.Project;
        preview.SetSnapshot(new PreviewSnapshot(project, project.MainSequence, Projects.Current.Media.AvailablePaths));
    }

    [Fact]
    public async Task Quadro_da_previa_do_clipe_colocado_na_timeline_tem_a_resolucao_pedida()
    {
        await ImportarEColocarNaTimelineAsync(_media.Video, MediaTime.Zero);
        using var preview = _app.Get<PreviewEngine>();
        Publicar(preview);

        var frame = await preview.CaptureAsync(MediaTime.FromSecondsApprox(1), 320, 180);

        Assert.NotNull(frame);
        Assert.Equal(320, frame.Width);
        Assert.Equal(180, frame.Height);
        Assert.Equal(320 * 180 * 4, frame.Pixels.Length);
        // testsrc2 é colorido: um quadro decodificado de verdade tem muitos tons diferentes, não um quadro vazio.
        Assert.True(frame.Pixels.Distinct().Count() > 16);
    }

    [Fact]
    public async Task Quadros_de_instantes_diferentes_do_mesmo_clipe_sao_diferentes()
    {
        await ImportarEColocarNaTimelineAsync(_media.Video, MediaTime.Zero);
        using var preview = _app.Get<PreviewEngine>();
        Publicar(preview);

        var inicio = await preview.CaptureAsync(MediaTime.FromSecondsApprox(0.2), 160, 90);
        var fim = await preview.CaptureAsync(MediaTime.FromSecondsApprox(2.5), 160, 90);

        Assert.NotNull(inicio);
        Assert.NotNull(fim);
        Assert.False(inicio.Pixels.AsSpan().SequenceEqual(fim.Pixels));
    }

    [Fact]
    public async Task Imagem_colocada_na_timeline_aparece_na_previa_com_a_resolucao_pedida()
    {
        await ImportarEColocarNaTimelineAsync(_media.Image, MediaTime.FromSecondsApprox(2));
        using var preview = _app.Get<PreviewEngine>();
        Publicar(preview);

        var frame = await preview.CaptureAsync(MediaTime.FromSecondsApprox(1), 240, 135);

        Assert.NotNull(frame);
        Assert.Equal(240, frame.Width);
        Assert.Equal(135, frame.Height);
        Assert.True(frame.Pixels.Distinct().Count() > 16);
    }

    public void Dispose() => _app.Dispose();
}
