using System.IO;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>F6 de ponta a ponta com mídia real: efeitos, cor, chroma key, keyframes, transição e congelamento.</summary>
[Collection(SampleMediaCollection.Name)]
public sealed class VisualRenderTests(SampleMedia media) : IDisposable
{
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);
    private readonly string _out = Path.Combine(media.Root, "visual " + Guid.NewGuid().ToString("N")[..6]);

    /// <summary>Quadros gravados para inspeção manual (pasta temporária, fora da pasta de mídias descartada).</summary>
    private static readonly string FramesDir = Path.Combine(Path.GetTempPath(), "med-f6-quadros");

    private async Task<MediaAsset> AssetAsync(string path, string name)
    {
        var r = await _probe.ProbeAsync(path);
        return new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = name, OriginalPath = path,
            FileSize = new FileInfo(path).Length, LastWriteUtc = File.GetLastWriteTimeUtc(path),
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info,
        };
    }

    private static Project NewProject() =>
        Project.CreateNew("Visual", new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }, StorageMode.Linked, DateTimeOffset.UtcNow);

    private static Clip FirstVideoClip(Project p, int index = 0) =>
        p.MainSequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips[index];

    /// <summary>Dois trechos encostados com dissolver, cor no segundo, keyframes no primeiro e um quadro congelado.</summary>
    private async Task<(Project Project, Dictionary<Guid, string> Paths)> EditedProjectAsync()
    {
        var asset = await AssetAsync(media.Video, "vídeo");
        var p = new AddMediaAssetsCommand([asset]).Apply(NewProject());
        var seq = p.MainSequence;
        var video = seq.Tracks.First(t => t.Kind == TrackKind.Video).Id;
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        p = new AddMediaToTimelineCommand(seq.Id, asset.Id, video, audio, MediaTime.Zero, MediaTime.FromSeconds(3)).Apply(p);
        p = new RepeatClipCommand(seq.Id, FirstVideoClip(p).Id, 1).Apply(p);

        var a = FirstVideoClip(p);
        var b = FirstVideoClip(p, 1);
        p = new SetTransitionCommand(seq.Id, a.Id, b.Id, TransitionKind.Dissolve, MediaTime.FromSeconds(1)).Apply(p);
        p = new UpdateClipCommand(seq.Id, b.Id, c => c with
        {
            Effects =
            [
                EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.ColorAdjust), "saturation", -100),
                EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Vignette), "amount", 80),
            ],
        }, "cor").Apply(p);
        p = new UpdateClipCommand(seq.Id, a.Id, c =>
        {
            c = ClipAnimation.ToggleKeyframe(c, AnimatedProperty.X, MediaTime.Zero);
            c = ClipAnimation.SetValue(c, AnimatedProperty.X, MediaTime.FromSeconds(1), 80);
            return ClipAnimation.SetValue(c, AnimatedProperty.Scale, MediaTime.Zero, 0.8);
        }, "animação").Apply(p);
        p = new FreezeFrameCommand(seq.Id, FirstVideoClip(p, 1).Id, MediaTime.FromSecondsApprox(4.5), MediaTime.FromSeconds(1)).Apply(p);
        return (p, new Dictionary<Guid, string> { [asset.Id] = media.Video });
    }

    [Fact]
    public async Task Exportacao_com_efeitos_transicao_keyframes_e_congelamento_tem_a_duracao_da_timeline()
    {
        var (p, paths) = await EditedProjectAsync();
        Assert.Equal(7.0, p.MainSequence.Duration.TotalSeconds, 3);

        var engine = new RenderEngine(new VideoFrameSourceFactory(NullLoggerFactory.Instance), new AudioSampleSourceFactory(NullLoggerFactory.Instance),
            new CpuCompositor(), _probe, NullLogger<RenderEngine>.Instance);
        var settings = new ExportSettings { Encoder = EncoderPreference.Software, VideoBitrateKbps = 800 };
        var result = await engine.RenderAsync(new RenderRequest
        {
            Project = p, SequenceId = p.MainSequence.Id, MediaPaths = paths, Settings = settings,
            OutputPath = Path.Combine(_out, "f6.mp4"), WorkDirectory = Path.Combine(_out, "trabalho"), HardwareDecoding = false,
        });

        var probe = await _probe.ProbeAsync(Assert.Single(result.Files));
        Assert.True(probe.IsOk, probe.Error);
        Assert.InRange(probe.Duration.TotalSeconds, 6.95, 7.06);
    }

    [Fact]
    public async Task Quadros_mostram_animacao_transicao_cor_e_congelamento()
    {
        var (p, paths) = await EditedProjectAsync();
        using var video = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);
        var renderer = new FrameRenderer(new CpuCompositor());
        BgraImage At(double s) => renderer.Render(video, p, p.MainSequence, paths, MediaTime.FromSecondsApprox(s), 320, 180);

        var start = At(0.2);
        var moved = At(1.0);
        var cut = At(3.0);
        var gray = At(4.0);
        var frozen1 = At(4.7);
        var frozen2 = At(5.3);
        var compare = renderer.RenderCompare(video, p, p.MainSequence, paths, MediaTime.FromSecondsApprox(4.0), 320, 180);
        Save(start, "1-inicio"); Save(moved, "2-animado"); Save(cut, "3-dissolver"); Save(gray, "4-sem-cor");
        Save(frozen1, "5-congelado"); Save(compare, "6-comparar");

        // Escala 80%: bordas pretas no topo; o keyframe de X desloca a imagem para a direita.
        Assert.True(Luma(start, 160, 3) < 10);
        Assert.True(Luma(moved, 15, 90) < 10, "o lado esquerdo deveria ficar vazio com X = 80 px");

        // Segundo trecho sem saturação: canais iguais no centro.
        var (b, g, r) = Pixel(gray, 160, 90);
        Assert.True(Math.Abs(r - g) <= 3 && Math.Abs(g - b) <= 3, $"esperava cinza, veio {r},{g},{b}");

        // No corte (dissolver em 50%) o centro não é cinza puro nem a imagem colorida original.
        Assert.NotEqual(Pixel(gray, 100, 90), Pixel(cut, 100, 90));

        // Quadro congelado: idêntico ao longo do clipe.
        Assert.Equal(frozen1.Pixels, frozen2.Pixels);

        // Comparação: metade esquerda colorida (antes), direita cinza (depois).
        var (lb, lg, lr) = Pixel(compare, 60, 60);
        var (rb, rg, rr) = Pixel(compare, 260, 60);
        Assert.True(Math.Abs(rr - rg) <= 3 && Math.Abs(rg - rb) <= 3);
        Assert.True(Math.Max(Math.Abs(lr - lg), Math.Abs(lg - lb)) > 3 || (lr, lg, lb) != (rr, rg, rb));
    }

    [Fact]
    public async Task Chroma_key_mostra_a_trilha_de_baixo_no_lugar_do_verde()
    {
        var below = await AssetAsync(media.Video, "fundo");
        var green = await AssetAsync(media.GreenScreen, "verde");
        var p = new AddMediaAssetsCommand([below, green]).Apply(NewProject());
        var seq = p.MainSequence;
        p = new AddTrackCommand(seq.Id, TrackKind.Video, "Vídeo 2", 0).Apply(p);
        seq = p.MainSequence;
        var top = seq.Tracks[0].Id;
        var bottom = seq.Tracks.First(t => t.Kind == TrackKind.Video && t.Id != top).Id;
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        p = new AddMediaToTimelineCommand(seq.Id, below.Id, bottom, audio, MediaTime.Zero, MediaTime.FromSeconds(3)).Apply(p);
        p = new AddMediaToTimelineCommand(seq.Id, green.Id, top, null, MediaTime.Zero, MediaTime.FromSeconds(3)).Apply(p);
        var keyed = p.MainSequence.Tracks[0].Clips[0];
        p = new UpdateClipCommand(seq.Id, keyed.Id, c => c with { Effects = [EffectCatalog.Create(EffectCatalog.ChromaKey)] }, "chroma").Apply(p);

        var paths = new Dictionary<Guid, string> { [below.Id] = media.Video, [green.Id] = media.GreenScreen };
        using var video = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);
        var renderer = new FrameRenderer(new CpuCompositor());
        var t = MediaTime.FromSecondsApprox(0.5);
        var withKey = renderer.Render(video, p, p.MainSequence, paths, t, 320, 180);
        var withoutKey = renderer.Render(video, p, p.MainSequence, paths, t, 320, 180, bypassEffects: true);
        var lower = video.GetFrame(below, media.Video, t, 320, 180)!;
        Save(withKey, "7-chroma");

        // Fora do retângulo: a imagem da trilha de baixo aparece (não verde).
        var (b, g, r) = Pixel(withKey, 30, 20);
        Assert.False(g > 120 && r < 60 && b < 100, $"o verde deveria ter sido removido, veio {r},{g},{b}");
        Assert.Equal(Pixel(lower, 30, 20), Pixel(withKey, 30, 20), new NearPixel(12));
        Assert.True(Pixel(withoutKey, 30, 20).G > 120);

        // Dentro do retângulo (pessoa): a cor original é mantida.
        var (pb, pg, pr) = Pixel(withKey, 160, 90);
        Assert.InRange(pr, 180, 255);
        Assert.InRange(pg, 140, 200);
        Assert.InRange(pb, 110, 170);
    }

    private sealed class NearPixel(int tolerance) : IEqualityComparer<(int B, int G, int R)>
    {
        public bool Equals((int B, int G, int R) x, (int B, int G, int R) y) =>
            Math.Abs(x.B - y.B) <= tolerance && Math.Abs(x.G - y.G) <= tolerance && Math.Abs(x.R - y.R) <= tolerance;

        public int GetHashCode((int B, int G, int R) obj) => 0;
    }

    private static (int B, int G, int R) Pixel(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);
    }

    private static double Luma(BgraImage img, int x, int y)
    {
        var (b, g, r) = Pixel(img, x, y);
        return 0.0722 * b + 0.7152 * g + 0.2126 * r;
    }

    private static void Save(BgraImage img, string name)
    {
        Directory.CreateDirectory(FramesDir);
        var raw = Path.Combine(FramesDir, name + ".raw");
        File.WriteAllBytes(raw, img.Pixels);
        SampleMedia.Ffmpeg($"-f rawvideo -pix_fmt bgra -s {img.Width}x{img.Height} -i \"{raw}\" -frames:v 1 \"{Path.Combine(FramesDir, name + ".png")}\"");
        File.Delete(raw);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true); } catch (IOException) { }
    }
}
