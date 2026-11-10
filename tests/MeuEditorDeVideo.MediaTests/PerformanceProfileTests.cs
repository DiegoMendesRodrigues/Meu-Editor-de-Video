using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>
/// Perfil de desempenho (F11, RNF008/RNF009): projeto 4K com três trilhas de vídeo sobrepostas (picture-in-picture,
/// opacidade e correção de cor) renderizado em resolução completa, 1/2 e 1/4. O relatório fica em
/// %TEMP%\med-desempenho\perfil-4k.txt e os números entram em documentation/DESEMPENHO.md.
/// </summary>
[Collection(SampleMediaCollection.Name)]
public sealed class PerformanceProfileTests(SampleMedia media, ITestOutputHelper output)
{
    private const int Frames = 12;
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);

    [Fact]
    [Trait("Categoria", "Desempenho")]
    public async Task Projeto_4k_multitrilha_fica_mais_rapido_em_resolucao_reduzida()
    {
        var uhd = Path.Combine(media.Root, "uhd.mp4");
        if (!File.Exists(uhd))
            SampleMedia.Ffmpeg($"-f lavfi -i testsrc2=size=3840x2160:rate=30 -t 2 -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{uhd}\"");
        var r = await _probe.ProbeAsync(uhd);
        Assert.True(r.IsOk, r.Error);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = "4K", OriginalPath = uhd, FileSize = new FileInfo(uhd).Length,
            LastWriteUtc = File.GetLastWriteTimeUtc(uhd), Duration = r.Duration, Width = r.Width, Height = r.Height,
            FrameRate = r.FrameRate, Info = r.Info,
        };

        var p = Project.CreateNew("4K", new ProjectSettings { Width = 3840, Height = 2160, FrameRate = FrameRate.Fps30 }, StorageMode.Linked, DateTimeOffset.UtcNow);
        p = new AddMediaAssetsCommand([asset]).Apply(p);
        var seq = p.MainSequence;
        p = new AddTrackCommand(seq.Id, TrackKind.Video, "Vídeo 2", 0).Apply(p);
        p = new AddTrackCommand(p.MainSequence.Id, TrackKind.Video, "Vídeo 3", 0).Apply(p);
        var videoTracks = p.MainSequence.Tracks.Where(t => t.Kind == TrackKind.Video).Select(t => t.Id).ToList();
        foreach (var track in videoTracks)
            p = new AddMediaToTimelineCommand(seq.Id, asset.Id, track, null, MediaTime.Zero, MediaTime.FromSeconds(2)).Apply(p);

        // Trilha de cima: PiP a 40%; do meio: 50% de opacidade e correção de cor; de baixo: tela cheia.
        var top = p.MainSequence.Tracks.First(t => t.Id == videoTracks[0]).Clips[0];
        var middle = p.MainSequence.Tracks.First(t => t.Id == videoTracks[1]).Clips[0];
        p = new UpdateClipCommand(seq.Id, top.Id, c => ClipAnimation.SetValue(c, AnimatedProperty.Scale, MediaTime.Zero, 0.4), "pip").Apply(p);
        p = new UpdateClipCommand(seq.Id, middle.Id, c =>
        {
            c = ClipAnimation.SetValue(c, AnimatedProperty.Opacity, MediaTime.Zero, 0.5);
            return c with { Effects = [EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.ColorAdjust), "saturation", 30)] };
        }, "cor").Apply(p);

        var paths = new Dictionary<Guid, string> { [asset.Id] = uhd };
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Perfil 4K multitrilha ({Environment.ProcessorCount} núcleos lógicos, {Frames} quadros por qualidade, 3 trilhas)");
        var results = new Dictionary<int, double>();
        foreach (var divisor in new[] { 1, 2, 4 })
        {
            using var video = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);
            var renderer = new FrameRenderer(new CpuCompositor());
            int w = 3840 / divisor, h = 2160 / divisor;
            renderer.Render(video, p, p.MainSequence, paths, MediaTime.Zero, w, h); // aquecimento (abre decodificadores)
            GC.Collect();
            var before = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            for (var i = 1; i <= Frames; i++)
            {
                var img = renderer.Render(video, p, p.MainSequence, paths, MediaTime.FromSecondsApprox(i / 30.0), w, h);
                Assert.Equal(w, img.Width);
            }
            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds / Frames;
            results[divisor] = ms;
            var frameMb = w * h * 4 / (1024.0 * 1024);
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{(divisor == 1 ? "Completa" : "1/" + divisor),-9} {w}x{h}: {ms,7:0.0} ms/quadro ({1000 / ms,5:0.0} q/s), quadro pronto {frameMb:0.0} MB, Δheap {(GC.GetTotalMemory(false) - before) / (1024.0 * 1024):0} MB");
            report.AppendLine(line);
            output.WriteLine(line);
        }

        var dir = Path.Combine(Path.GetTempPath(), "med-desempenho");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "perfil-4k.txt"), report.ToString());

        // A redução de resolução precisa compensar: 1/4 deve ser claramente mais rápido que a completa.
        Assert.True(results[4] < results[1] * 0.8, $"1/4 ({results[4]:0} ms) deveria ser mais rápido que a completa ({results[1]:0} ms)");
    }
}
