using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>
/// §14.2 (1): interface ↔ Motor de Timeline. Usa os mesmos serviços que as view models usam (<see cref="IProjectService"/>,
/// <see cref="IMediaImportService"/> e os comandos de edição), com o DI real, sem janela.
/// </summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class InterfaceTimelineIntegrationTests : IDisposable
{
    private readonly IntegrationMedia _media;
    private readonly AppContainer _app;

    public InterfaceTimelineIntegrationTests(IntegrationMedia media)
    {
        _media = media;
        _app = new AppContainer(TempFiles.NewRoot("interface-timeline"));
    }

    private IProjectService Projects => _app.Get<IProjectService>();

    private Project Atual => Projects.Current!.Project;

    private Sequence Sequencia => Atual.MainSequence;

    private Guid TrilhaDe(TrackKind kind) => Sequencia.Tracks.First(t => t.Kind == kind).Id;

    private async Task<Guid> ProjetoComVideoNaTimelineAsync()
    {
        Directory.CreateDirectory(_app.ProjectsDir);
        await Projects.CreateAsync(new NewProjectRequest("Interface", _app.ProjectsDir,
            new ProjectSettings { Width = 640, Height = 360, FrameRate = FrameRate.Fps25 }, StorageMode.Linked));

        var import = await _app.Get<IMediaImportService>().ImportAsync([_media.Video]);
        var asset = Assert.Single(import.Imported);

        Projects.Execute(new AddMediaToTimelineCommand(Sequencia.Id, asset.Id, TrilhaDe(TrackKind.Video), TrilhaDe(TrackKind.Audio), MediaTime.Zero, MediaTime.Zero));
        return asset.Id;
    }

    [Fact]
    public async Task Adicionar_midia_a_timeline_cria_clipes_de_video_e_audio_ligados()
    {
        await ProjetoComVideoNaTimelineAsync();

        var video = Sequencia.Tracks.First(t => t.Kind == TrackKind.Video).Clips;
        var audio = Sequencia.Tracks.First(t => t.Kind == TrackKind.Audio).Clips;
        Assert.Single(video);
        Assert.Single(audio);
        Assert.Equal(MediaTime.Zero, video[0].Start);
        Assert.Equal(MediaTime.FromSecondsApprox(3), video[0].Duration);
        Assert.Equal(MediaTime.FromSecondsApprox(3), Sequencia.Duration);
        Assert.True(Projects.Current!.IsDirty);
    }

    [Fact]
    public async Task Dividir_e_desfazer_e_refazer_refletem_o_estado_da_timeline()
    {
        await ProjetoComVideoNaTimelineAsync();
        var videoTrack = TrilhaDe(TrackKind.Video);

        Projects.Execute(new SplitClipsCommand(Sequencia.Id, MediaTime.FromSecondsApprox(1)));
        Assert.Equal(2, Sequencia.Tracks.First(t => t.Id == videoTrack).Clips.Count);

        Projects.Current!.History.Undo();
        Assert.Single(Sequencia.Tracks.First(t => t.Id == videoTrack).Clips);
        Assert.True(Projects.Current.History.CanRedo);

        Projects.Current.History.Redo();
        Assert.Equal(2, Sequencia.Tracks.First(t => t.Id == videoTrack).Clips.Count);
        Assert.False(Projects.Current.History.CanRedo);
    }

    [Fact]
    public async Task Mover_clipe_e_desfazer_restaura_a_posicao_original()
    {
        await ProjetoComVideoNaTimelineAsync();
        var videoTrack = TrilhaDe(TrackKind.Video);
        var clipId = Sequencia.Tracks.First(t => t.Id == videoTrack).Clips[0].Id;

        Projects.Execute(new MoveClipsCommand(Sequencia.Id, [new ClipMove(clipId, videoTrack, MediaTime.FromSecondsApprox(5))]));
        Assert.Equal(MediaTime.FromSecondsApprox(5), Sequencia.Tracks.First(t => t.Id == videoTrack).Clips[0].Start);

        Projects.Current!.History.Undo();
        Assert.Equal(MediaTime.Zero, Sequencia.Tracks.First(t => t.Id == videoTrack).Clips[0].Start);
    }

    [Fact]
    public async Task Salvar_limpa_o_estado_sujo_sem_apagar_o_historico_de_desfazer()
    {
        await ProjetoComVideoNaTimelineAsync();

        await Projects.SaveAsync();

        Assert.False(Projects.Current!.IsDirty);
        Assert.True(Projects.Current.History.CanUndo);
    }

    [Fact]
    public async Task Comando_recusado_pela_timeline_nao_altera_o_projeto()
    {
        await ProjetoComVideoNaTimelineAsync();
        var antes = Sequencia.Tracks.Sum(t => t.Clips.Count);

        var ex = Assert.Throws<EditRejectedException>(() =>
            Projects.Execute(new AddMediaToTimelineCommand(Sequencia.Id, Guid.NewGuid(), TrilhaDe(TrackKind.Video), null, MediaTime.Zero, MediaTime.Zero)));

        Assert.Equal("Mídia não encontrada.", ex.Message);
        Assert.Equal(antes, Sequencia.Tracks.Sum(t => t.Clips.Count));
    }

    public void Dispose() => _app.Dispose();
}
