using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;
using MeuEditorDeVideo.MediaEngine;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>§14.2 (3): Motor de Mídia ↔ FFmpeg. Sonda, miniaturas e forma de onda de mídia gerada, pelo DI real.</summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class MediaFfmpegIntegrationTests : IDisposable
{
    private readonly IntegrationMedia _media;
    private readonly AppContainer _app;

    public MediaFfmpegIntegrationTests(IntegrationMedia media)
    {
        _media = media;
        _app = new AppContainer(TempFiles.NewRoot("midia-ffmpeg"));
    }

    private IMediaProbe Probe => _app.Get<IMediaProbe>();

    private async Task<MediaAsset> ComoAtivoAsync(string arquivo)
    {
        var r = await Probe.ProbeAsync(arquivo);
        Assert.True(r.IsOk, r.Error);
        return new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = Path.GetFileName(arquivo), OriginalPath = arquivo,
            FileSize = new FileInfo(arquivo).Length, LastWriteUtc = File.GetLastWriteTimeUtc(arquivo),
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info,
        };
    }

    [Fact]
    public async Task Sonda_de_video_informa_resolucao_duracao_codecs_e_audio()
    {
        var r = await Probe.ProbeAsync(_media.Video);

        Assert.True(r.IsOk, r.Error);
        Assert.Equal(MediaKind.Video, r.Kind);
        Assert.Equal(640, r.Width);
        Assert.Equal(360, r.Height);
        Assert.InRange(r.Duration.TotalSeconds, 2.9, 3.1);
        Assert.Equal("h264", r.Info.VideoCodec);
        Assert.Equal("aac", r.Info.AudioCodec);
        Assert.NotNull(r.FrameRate);
    }

    [Fact]
    public async Task Sonda_reconhece_audio_imagem_e_video_sem_audio()
    {
        var audio = await Probe.ProbeAsync(_media.Audio);
        var imagem = await Probe.ProbeAsync(_media.Image);
        var semAudio = await Probe.ProbeAsync(_media.VideoSemAudio);

        Assert.True(audio.IsOk, audio.Error);
        Assert.Equal(MediaKind.Audio, audio.Kind);
        Assert.True(imagem.IsOk, imagem.Error);
        Assert.Equal(MediaKind.Image, imagem.Kind);
        Assert.Equal(320, imagem.Width);
        Assert.True(semAudio.IsOk, semAudio.Error);
        Assert.Null(semAudio.Info.AudioCodec);
    }

    [Fact]
    public async Task Arquivo_danificado_e_reportado_pelo_probe_sem_excecao()
    {
        var r = await Probe.ProbeAsync(_media.Truncated);

        Assert.False(r.IsOk);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public async Task Miniatura_tem_a_largura_pedida_e_fica_no_cache_de_disco()
    {
        var asset = await ComoAtivoAsync(_media.Video);
        var provider = _app.Get<IThumbnailProvider>();
        var cache = _app.Get<MediaCache>();

        var img = await provider.GetThumbnailAsync(asset, _media.Video, MediaTime.FromSecondsApprox(1), 160);

        Assert.NotNull(img);
        Assert.Equal(160, img.Width);
        Assert.Equal(90, img.Height);
        Assert.Contains(img.Pixels, b => b != 0);
        Assert.True(File.Exists(cache.ThumbnailPath(asset, _media.Video, MediaTime.FromSecondsApprox(1), 160)));

        var again = await provider.GetThumbnailAsync(asset, _media.Video, MediaTime.FromSecondsApprox(1), 160);
        Assert.Equal(img.Pixels, again!.Pixels);
    }

    [Fact]
    public async Task Forma_de_onda_tem_cem_picos_por_segundo_da_faixa_de_audio()
    {
        var asset = await ComoAtivoAsync(_media.Video);

        var wave = await _app.Get<IWaveformProvider>().GetWaveformAsync(asset, _media.Video);

        Assert.NotNull(wave);
        Assert.Equal(WaveformProvider.PeaksPerSecond, wave.PeaksPerSecond);
        Assert.InRange(wave.Count, 280, 320); // 3 s × 100 picos
        Assert.Contains(wave.Max, m => m > 0.1f);
    }

    public void Dispose() => _app.Dispose();
}
