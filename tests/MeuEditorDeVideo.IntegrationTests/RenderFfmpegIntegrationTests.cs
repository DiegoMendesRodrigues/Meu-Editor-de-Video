using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>§14.2 (5): Motor de Renderização ↔ FFmpeg. Exporta pelo serviço do DI e confere o arquivo gerado com o probe.</summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class RenderFfmpegIntegrationTests : IDisposable
{
    private readonly IntegrationMedia _media;
    private readonly AppContainer _app;

    public RenderFfmpegIntegrationTests(IntegrationMedia media)
    {
        _media = media;
        _app = new AppContainer(TempFiles.NewRoot("render-ffmpeg"));
    }

    private IProjectService Projects => _app.Get<IProjectService>();

    /// <summary>Projeto de 320x180 com o vídeo de exemplo (3 s, com áudio) colocado na timeline.</summary>
    private async Task<(Project Projeto, IReadOnlyDictionary<Guid, string> Caminhos)> ProjetoAsync()
    {
        Directory.CreateDirectory(_app.ProjectsDir);
        await Projects.CreateAsync(new NewProjectRequest("Exportacao", _app.ProjectsDir,
            new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }, StorageMode.Linked));
        var asset = Assert.Single((await _app.Get<IMediaImportService>().ImportAsync([_media.Video])).Imported);

        var seq = Projects.Current!.Project.MainSequence;
        Guid Trilha(TrackKind k) => seq.Tracks.First(t => t.Kind == k).Id;
        Projects.Execute(new AddMediaToTimelineCommand(seq.Id, asset.Id, Trilha(TrackKind.Video), Trilha(TrackKind.Audio), MediaTime.Zero, MediaTime.Zero));
        return (Projects.Current.Project, Projects.Current.Media.AvailablePaths);
    }

    private RenderRequest Pedido(Project projeto, IReadOnlyDictionary<Guid, string> caminhos, string nome, MediaTime? inicio = null, MediaTime? fim = null) => new()
    {
        Project = projeto,
        SequenceId = projeto.MainSequence.Id,
        MediaPaths = caminhos,
        Settings = new ExportSettings
        {
            Container = ExportContainer.Mp4,
            VideoCodec = VideoCodec.H264,
            AudioCodec = AudioCodec.Aac,
            Encoder = EncoderPreference.Software,
            Width = 320,
            Height = 180,
            VideoBitrateKbps = 1000,
            AudioBitrateKbps = 128,
        },
        OutputPath = Path.Combine(_app.Root, "saida", nome + ".mp4"),
        WorkDirectory = Path.Combine(_app.Root, "trabalho", nome),
        HardwareDecoding = false,
        RangeStart = inicio ?? MediaTime.Zero,
        RangeEnd = fim,
    };

    [Fact]
    public async Task Exporta_projeto_para_mp4_e_o_arquivo_tem_duracao_resolucao_e_audio()
    {
        var (projeto, caminhos) = await ProjetoAsync();
        var render = _app.Get<IRenderEngine>();
        var probe = _app.Get<IMediaProbe>();

        var result = await render.RenderAsync(Pedido(projeto, caminhos, "completo"));

        var file = Assert.Single(result.Files);
        Assert.True(File.Exists(file));
        var r = await probe.ProbeAsync(file);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal(320, r.Width);
        Assert.Equal(180, r.Height);
        Assert.InRange(r.Duration.TotalSeconds, 2.9, 3.1);
        Assert.NotNull(r.Info.AudioCodec);
        Assert.Equal("aac", r.Info.AudioCodec);
    }

    // Regressão (F12): RenderEngine.SplitParts somava long.MaxValue ao quadro inicial (sem MaxSecondsPerFile e com início > 0)
    // e estourava o inteiro, sem gerar nenhum trecho ("Nenhum trecho para multiplexar").
    [Fact]
    public async Task Exportar_um_intervalo_gera_um_arquivo_com_a_duracao_do_trecho()
    {
        var (projeto, caminhos) = await ProjetoAsync();

        var result = await _app.Get<IRenderEngine>().RenderAsync(
            Pedido(projeto, caminhos, "trecho", MediaTime.FromSecondsApprox(0.5), MediaTime.FromSecondsApprox(2.5)));

        var r = await _app.Get<IMediaProbe>().ProbeAsync(Assert.Single(result.Files));
        Assert.True(r.IsOk, r.Error);
        Assert.InRange(r.Duration.TotalSeconds, 1.9, 2.1);
    }

    public void Dispose() => _app.Dispose();
}
