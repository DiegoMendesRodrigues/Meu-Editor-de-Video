using System.Diagnostics;
using System.IO;
using System.Linq;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>
/// Matriz audiovisual (spec §14.3): resoluções e orientações, codecs, áudio mono/estéreo, múltiplas trilhas,
/// transparência e taxas de quadros diferentes. Cada mídia é gerada pelo FFmpeg empacotado em uma subpasta "matriz".
/// Codificadores ausentes no FFmpeg empacotado são ignorados (retorno antecipado) em vez de falhar.
/// </summary>
[Collection(SampleMediaCollection.Name)]
public sealed class AudiovisualMatrixTests(SampleMedia media)
{
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);
    private readonly string _dir = CriarPasta(media.Root);

    private static readonly Lazy<HashSet<string>> CodificadoresDisponiveis = new(LerCodificadores);

    private static string CriarPasta(string raiz)
    {
        var pasta = Path.Combine(raiz, "matriz");
        Directory.CreateDirectory(pasta);
        return pasta;
    }

    /// <summary>Lista os codificadores de "ffmpeg -encoders" (uma leitura por execução de testes).</summary>
    private static HashSet<string> LerCodificadores()
    {
        var psi = new ProcessStartInfo(SampleMedia.FFmpegExe, "-hide_banner -encoders")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var saida = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        var nomes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var linha in saida.Split('\n'))
        {
            // Cada linha de codificador começa com flags de 6 caracteres ("V....D", "A....D") seguidas do nome.
            var campos = linha.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (campos.Length >= 2 && campos[0].Length == 6) nomes.Add(campos[1]);
        }
        return nomes;
    }

    private static bool Disponivel(string codificador) => CodificadoresDisponiveis.Value.Contains(codificador);

    private async Task<MediaAsset> AssetAsync(string path, string nome)
    {
        var r = await _probe.ProbeAsync(path);
        Assert.True(r.IsOk, $"Falha ao analisar '{Path.GetFileName(path)}': {r.Error}");
        return new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = nome, OriginalPath = path,
            FileSize = new FileInfo(path).Length, LastWriteUtc = File.GetLastWriteTimeUtc(path),
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info,
        };
    }

    private static Project NovoProjeto(int largura, int altura, FrameRate fr) =>
        Project.CreateNew("Matriz", new ProjectSettings { Width = largura, Height = altura, FrameRate = fr }, StorageMode.Linked, DateTimeOffset.UtcNow);

    /// <summary>Projeto com uma única mídia no início da linha do tempo (vídeo, imagem ou áudio).</summary>
    private static Project ProjetoComMidia(MediaAsset asset, int largura, int altura, FrameRate fr)
    {
        var p = new AddMediaAssetsCommand([asset]).Apply(NovoProjeto(largura, altura, fr));
        var seq = p.MainSequence;
        var video = seq.Tracks.First(t => t.Kind == TrackKind.Video).Id;
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        return new AddMediaToTimelineCommand(seq.Id, asset.Id, asset.Kind == MediaKind.Audio ? null : video,
            asset.Info?.AudioStreams > 0 ? audio : null, MediaTime.Zero, MediaTime.FromSeconds(1)).Apply(p);
    }

    private static Dictionary<Guid, string> Caminhos(params MediaAsset[] assets) =>
        assets.ToDictionary(a => a.Id, a => a.OriginalPath);

    private static RenderEngine NovoMotor(MediaProbe probe) => new(
        new VideoFrameSourceFactory(NullLoggerFactory.Instance),
        new AudioSampleSourceFactory(NullLoggerFactory.Instance),
        new CpuCompositor(), probe, NullLogger<RenderEngine>.Instance);

    /// <summary>Renderiza o quadro no instante informado, no tamanho pedido.</summary>
    private static BgraImage Quadro(Project p, IReadOnlyDictionary<Guid, string> caminhos, double segundos, int largura, int altura)
    {
        using var video = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);
        return new FrameRenderer(new CpuCompositor())
            .Render(video, p, p.MainSequence, caminhos, MediaTime.FromSecondsApprox(segundos), largura, altura);
    }

    private static (int B, int G, int R) Pixel(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);
    }

    private static double LumaMaxima(BgraImage img)
    {
        double max = 0;
        for (var i = 0; i + 3 < img.Pixels.Length; i += 4)
        {
            var luma = 0.0722 * img.Pixels[i] + 0.7152 * img.Pixels[i + 1] + 0.2126 * img.Pixels[i + 2];
            if (luma > max) max = luma;
        }
        return max;
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    [InlineData(1080, 1920)]
    [InlineData(720, 1280)]
    [InlineData(1080, 1080)]
    public async Task Resolucoes_e_orientacoes_chegam_ao_quadro_no_tamanho_do_projeto(int largura, int altura)
    {
        var arquivo = Path.Combine(_dir, $"resolucao_{largura}x{altura}.mp4");
        SampleMedia.Ffmpeg($"-f lavfi -i testsrc2=size={largura}x{altura}:rate=25 -t 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{arquivo}\"");
        var asset = await AssetAsync(arquivo, "resolução");
        Assert.Equal(largura, asset.Width);
        Assert.Equal(altura, asset.Height);

        var p = ProjetoComMidia(asset, largura, altura, FrameRate.Fps25);
        var img = Quadro(p, Caminhos(asset), 0.5, largura, altura);

        Assert.Equal(largura, img.Width);
        Assert.Equal(altura, img.Height);
        Assert.True(LumaMaxima(img) > 30, "o quadro renderizado saiu preto");
    }

    public static TheoryData<string, string, string, string, bool> Codecs => new()
    {
        { "h264", "libx264", "mp4", "-t 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p", false },
        { "hevc", "libx265", "mp4", "-t 1 -c:v libx265 -preset ultrafast -tag:v hvc1 -pix_fmt yuv420p -x265-params log-level=error", false },
        { "vp9", "libvpx-vp9", "webm", "-t 1 -c:v libvpx-vp9 -b:v 0 -crf 45 -deadline realtime -cpu-used 8 -pix_fmt yuv420p", false },
        { "av1-svt", "libsvtav1", "mkv", "-t 1 -c:v libsvtav1 -preset 12 -pix_fmt yuv420p", false },
        { "av1-aom", "libaom-av1", "mkv", "-t 1 -c:v libaom-av1 -cpu-used 8 -row-mt 1 -b:v 0 -crf 50 -pix_fmt yuv420p", false },
        { "mpeg4", "mpeg4", "avi", "-t 1 -c:v mpeg4 -q:v 5 -pix_fmt yuv420p", false },
        { "mjpeg", "mjpeg", "avi", "-t 1 -c:v mjpeg -q:v 5 -pix_fmt yuvj420p", false },
        { "prores", "prores_ks", "mov", "-t 1 -c:v prores_ks -profile:v 0 -pix_fmt yuv422p10le", false },
        { "png", "png", "png", "-frames:v 1 -c:v png", true },
        // Regressão (F12): JPEG era classificado como "Corrupted" e depois saía preto — a busca até o quadro 0 descartava
        // o único pacote já lido por avformat_find_stream_info. Corrigido em MediaProbe.VerifyDecodable e VideoFrameReader.Position.
        { "jpg", "mjpeg", "jpg", "-frames:v 1 -q:v 3", true },
    };

    [Theory]
    [MemberData(nameof(Codecs))]
    public async Task Codecs_sao_reconhecidos_e_decodificam_um_quadro_nao_preto(string nome, string codificador, string extensao, string argumentos, bool imagem)
    {
        // Codificador ausente no FFmpeg empacotado: o caso é ignorado (xUnit 2 não tem skip dinâmico).
        if (!Disponivel(codificador)) return;

        var arquivo = Path.Combine(_dir, $"codec_{nome}.{extensao}");
        SampleMedia.Ffmpeg($"-f lavfi -i testsrc2=size=320x180:rate=25 {argumentos} \"{arquivo}\"");

        var r = await _probe.ProbeAsync(arquivo);
        Assert.True(r.IsOk, $"{nome} ({codificador}): {r.Error}");
        Assert.Equal(imagem ? MediaKind.Image : MediaKind.Video, r.Kind);

        var asset = await AssetAsync(arquivo, nome);
        var p = ProjetoComMidia(asset, 320, 180, FrameRate.Fps25);
        var img = Quadro(p, Caminhos(asset), 0.2, 320, 180);
        Assert.True(LumaMaxima(img) > 30, $"{nome}: o quadro decodificado saiu preto");
    }

    public static TheoryData<string, string, string, int> Audios => new()
    {
        { "wav", "pcm_s16le", "", 1 },
        { "wav", "pcm_s16le", "", 2 },
        { "aac", "aac", "-b:a 128k", 1 },
        { "aac", "aac", "-b:a 128k", 2 },
        { "mp3", "libmp3lame", "-b:a 128k", 1 },
        { "mp3", "libmp3lame", "-b:a 128k", 2 },
        { "flac", "flac", "", 1 },
        { "flac", "flac", "", 2 },
        { "opus", "libopus", "-b:a 96k", 1 },
        { "opus", "libopus", "-b:a 96k", 2 },
    };

    [Theory]
    [MemberData(nameof(Audios))]
    public async Task Audio_mono_e_estereo_sao_reconhecidos_e_exportados_com_som(string extensao, string codec, string extras, int canais)
    {
        if (!Disponivel(codec)) return;

        var arquivo = Path.Combine(_dir, $"som_{extensao}_{canais}ch.{extensao}");
        SampleMedia.Ffmpeg($"-f lavfi -i sine=frequency=440:sample_rate=48000 -t 2 -ac {canais} -c:a {codec} {extras} \"{arquivo}\"");

        var asset = await AssetAsync(arquivo, $"som {extensao}");
        Assert.Equal(MediaKind.Audio, asset.Kind);
        Assert.Equal(canais, asset.Info?.Channels);
        Assert.InRange(asset.Duration.TotalSeconds, 1.9, 2.1);

        var p = ProjetoComMidia(asset, 320, 180, FrameRate.Fps25);
        var caminhos = Caminhos(asset);
        var result = await NovoMotor(_probe).RenderAsync(new RenderRequest
        {
            Project = p, SequenceId = p.MainSequence.Id, MediaPaths = caminhos,
            Settings = new ExportSettings
            {
                Container = ExportContainer.Mp4, VideoCodec = VideoCodec.H264, AudioCodec = AudioCodec.Aac,
                Encoder = EncoderPreference.Software, VideoBitrateKbps = 500,
            },
            OutputPath = Path.Combine(_dir, $"export_som_{extensao}_{canais}ch.mp4"),
            WorkDirectory = Path.Combine(_dir, $"trabalho_som_{extensao}_{canais}ch"), HardwareDecoding = false,
        });

        var saida = await _probe.ProbeAsync(Assert.Single(result.Files));
        Assert.True(saida.IsOk, saida.Error);
        Assert.True(saida.Info.AudioStreams >= 1, "a exportação não contém faixa de áudio");
        Assert.InRange(saida.Duration.TotalSeconds, 1.9, 2.1);
    }

    [Fact]
    public async Task Multiplas_trilhas_de_video_e_audio_exportam_com_resolucao_e_duracao_da_timeline()
    {
        var camada = Path.Combine(_dir, "camada_multipla.mp4");
        SampleMedia.Ffmpeg($"-f lavfi -i testsrc2=size=640x360:rate=25 -t 2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{camada}\"");
        var som440 = Path.Combine(_dir, "tom_440.wav");
        SampleMedia.Ffmpeg($"-f lavfi -i sine=frequency=440:sample_rate=48000 -t 2 -ac 2 \"{som440}\"");
        var som660 = Path.Combine(_dir, "tom_660.wav");
        SampleMedia.Ffmpeg($"-f lavfi -i sine=frequency=660:sample_rate=48000 -t 2 -ac 2 \"{som660}\"");

        var video = await AssetAsync(camada, "camada");
        var tom1 = await AssetAsync(som440, "tom 440");
        var tom2 = await AssetAsync(som660, "tom 660");
        var p = new AddMediaAssetsCommand([video, tom1, tom2]).Apply(NovoProjeto(640, 360, FrameRate.Fps25));
        var seqId = p.MainSequence.Id;
        p = new AddTrackCommand(seqId, TrackKind.Video, "Vídeo 2", 0).Apply(p);
        p = new AddTrackCommand(seqId, TrackKind.Audio, "Áudio 2").Apply(p);

        // O projeto novo já traz Vídeo 1 e 2; com a faixa extra são três.
        // Índice 0 = topo: PiP; 1 = meio: 50% de opacidade; 2 = fundo: tela cheia.
        var videos = p.MainSequence.Tracks.Where(t => t.Kind == TrackKind.Video).Select(t => t.Id).ToList();
        var audios = p.MainSequence.Tracks.Where(t => t.Kind == TrackKind.Audio).Select(t => t.Id).ToList();
        Assert.Equal(3, videos.Count);
        Assert.Equal(2, audios.Count);

        foreach (var faixa in videos)
            p = new AddMediaToTimelineCommand(seqId, video.Id, faixa, null, MediaTime.Zero, MediaTime.FromSeconds(2)).Apply(p);
        p = new AddMediaToTimelineCommand(seqId, tom1.Id, null, audios[0], MediaTime.Zero, MediaTime.FromSeconds(2)).Apply(p);
        p = new AddMediaToTimelineCommand(seqId, tom2.Id, null, audios[1], MediaTime.Zero, MediaTime.FromSeconds(2)).Apply(p);

        var topo = p.MainSequence.Tracks.First(t => t.Id == videos[0]).Clips[0].Id;
        var meio = p.MainSequence.Tracks.First(t => t.Id == videos[1]).Clips[0].Id;
        p = new UpdateClipCommand(seqId, topo, c => ClipAnimation.SetValue(c, AnimatedProperty.Scale, MediaTime.Zero, 0.4), "pip").Apply(p);
        p = new UpdateClipCommand(seqId, meio, c => ClipAnimation.SetValue(c, AnimatedProperty.Opacity, MediaTime.Zero, 0.5), "opacidade").Apply(p);

        var result = await NovoMotor(_probe).RenderAsync(new RenderRequest
        {
            Project = p, SequenceId = seqId, MediaPaths = Caminhos(video, tom1, tom2),
            Settings = new ExportSettings
            {
                Container = ExportContainer.Mp4, VideoCodec = VideoCodec.H264, AudioCodec = AudioCodec.Aac,
                Encoder = EncoderPreference.Software, VideoBitrateKbps = 500,
            },
            OutputPath = Path.Combine(_dir, "export_multiplas_trilhas.mp4"),
            WorkDirectory = Path.Combine(_dir, "trabalho_multiplas_trilhas"), HardwareDecoding = false,
        });

        var saida = await _probe.ProbeAsync(Assert.Single(result.Files));
        Assert.True(saida.IsOk, saida.Error);
        Assert.Equal(1, saida.Info.VideoStreams);
        Assert.True(saida.Info.AudioStreams >= 1, "a exportação não contém faixa de áudio");
        Assert.Equal(640, saida.Width);
        Assert.Equal(360, saida.Height);
        Assert.InRange(saida.Duration.TotalSeconds, 1.9, 2.1);
    }

    [Fact]
    public async Task Transparencia_mostra_a_trilha_de_baixo_fora_da_forma_e_a_forma_por_cima()
    {
        var verde = Path.Combine(_dir, "fundo_verde.mp4");
        SampleMedia.Ffmpeg($"-f lavfi -i color=c=0x00FF00:s=320x180:r=25 -t 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{verde}\"");
        var forma = Path.Combine(_dir, "forma_alfa.mov");
        // drawbox não grava alfa sobre um fundo transparente; overlay de um retângulo opaco preserva o alfa.
        SampleMedia.Ffmpeg($"-f lavfi -i \"color=c=black@0.0:s=320x180:r=25,format=rgba[bg];color=c=blue:s=100x80:r=25,format=rgba[fg];[bg][fg]overlay=x=110:y=50,format=rgba\" -t 1 -c:v qtrle \"{forma}\"");

        var fundo = await AssetAsync(verde, "verde");
        var alfa = await AssetAsync(forma, "forma alfa");
        Assert.True(alfa.Info?.HasAlpha == true, "o clipe .mov com canal alfa deveria ser reconhecido com transparência");

        var p = new AddMediaAssetsCommand([fundo, alfa]).Apply(NovoProjeto(320, 180, FrameRate.Fps25));
        p = new AddTrackCommand(p.MainSequence.Id, TrackKind.Video, "Vídeo 2", 0).Apply(p);
        var seq = p.MainSequence;
        var topo = seq.Tracks[0].Id;
        var baixo = seq.Tracks.First(t => t.Kind == TrackKind.Video && t.Id != topo).Id;
        p = new AddMediaToTimelineCommand(seq.Id, fundo.Id, baixo, null, MediaTime.Zero, MediaTime.FromSeconds(1)).Apply(p);
        p = new AddMediaToTimelineCommand(seq.Id, alfa.Id, topo, null, MediaTime.Zero, MediaTime.FromSeconds(1)).Apply(p);

        var img = Quadro(p, Caminhos(fundo, alfa), 0.5, 320, 180);

        // Fora da forma transparente: aparece o verde da trilha de baixo.
        var (b1, g1, r1) = Pixel(img, 20, 20);
        Assert.True(g1 > 150 && r1 < 90 && b1 < 90, $"fora da forma deveria aparecer o verde, veio R={r1} G={g1} B={b1}");

        // Dentro da forma opaca (centro do retângulo): azul.
        var (b2, g2, r2) = Pixel(img, 160, 90);
        Assert.True(b2 > 180 && g2 < 90 && r2 < 90, $"dentro da forma deveria aparecer o azul, veio R={r2} G={g2} B={b2}");
    }

    public static TheoryData<int, int> TaxasDeQuadros => new()
    {
        { 24000, 1001 },
        { 25, 1 },
        { 30000, 1001 },
        { 30, 1 },
        { 50, 1 },
        { 60000, 1001 },
        { 60, 1 },
    };

    [Theory]
    [MemberData(nameof(TaxasDeQuadros))]
    public async Task Taxas_de_quadros_diferentes_sao_detectadas_e_exportadas_com_a_duracao_da_timeline(int numerador, int denominador)
    {
        var arquivo = Path.Combine(_dir, $"taxa_{numerador}_{denominador}.mp4");
        SampleMedia.Ffmpeg($"-f lavfi -i testsrc2=size=320x180:rate={numerador}/{denominador} -t 1 -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{arquivo}\"");

        var asset = await AssetAsync(arquivo, "taxa");
        var esperado = (double)numerador / denominador;
        Assert.True(asset.FrameRate is not null, "a taxa de quadros não foi detectada");
        Assert.InRange(asset.FrameRate!.Value.Value, esperado - 0.01, esperado + 0.01);

        // Projeto a 25 FPS: a mídia de outra taxa é reamostrada na exportação.
        var p = ProjetoComMidia(asset, 320, 180, FrameRate.Fps25);
        var result = await NovoMotor(_probe).RenderAsync(new RenderRequest
        {
            Project = p, SequenceId = p.MainSequence.Id, MediaPaths = Caminhos(asset),
            Settings = new ExportSettings { Encoder = EncoderPreference.Software, VideoBitrateKbps = 500 },
            OutputPath = Path.Combine(_dir, $"export_taxa_{numerador}_{denominador}.mp4"),
            WorkDirectory = Path.Combine(_dir, $"trabalho_taxa_{numerador}_{denominador}"), HardwareDecoding = false,
        });

        var saida = await _probe.ProbeAsync(Assert.Single(result.Files));
        Assert.True(saida.IsOk, saida.Error);
        var duracaoTimeline = p.MainSequence.Duration.TotalSeconds;
        Assert.InRange(saida.Duration.TotalSeconds, duracaoTimeline - 0.06, duracaoTimeline + 0.06);
    }
}
