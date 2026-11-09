using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>Proxies de edição (pendência da F5): geração 540p, fidelidade de quadros, rotação e fila em segundo plano.</summary>
[Collection(SampleMediaCollection.Name)]
public sealed class ProxyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-proxy-tests", Guid.NewGuid().ToString("N"));
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);
    private readonly MediaCache _cache;

    public ProxyTests()
    {
        Directory.CreateDirectory(_root);
        _cache = new MediaCache(new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local")), NullLogger<MediaCache>.Instance);
    }

    private string Generate(string name, string args)
    {
        var path = Path.Combine(_root, name);
        SampleMedia.Ffmpeg($"{args} \"{path}\"");
        return path;
    }

    private async Task<MediaAsset> AssetAsync(string path)
    {
        var r = await _probe.ProbeAsync(path);
        Assert.True(r.IsOk, r.Error);
        return new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = Path.GetFileName(path), OriginalPath = path,
            FileSize = new FileInfo(path).Length, LastWriteUtc = File.GetLastWriteTimeUtc(path),
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info,
        };
    }

    private static double MeanDifference(BgraImage a, BgraImage b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        long sum = 0;
        for (var i = 0; i < a.Pixels.Length; i += 4)
            for (var c = 0; c < 3; c++) sum += Math.Abs(a.Pixels[i + c] - b.Pixels[i + c]);
        return sum / (a.Pixels.Length / 4 * 3.0);
    }

    [Fact]
    public async Task Proxy_1080p_vira_540p_so_video_com_a_mesma_duracao_e_quadros_equivalentes()
    {
        var source = Generate("1080p.mp4", "-f lavfi -i testsrc2=size=1920x1080:rate=25 -f lavfi -i sine=frequency=440 -t 2 -c:v libx264 -pix_fmt yuv420p -c:a aac");
        var asset = await AssetAsync(source);
        var output = Path.Combine(_root, "proxy.mp4");
        var reports = new List<double>();

        ProxyGenerator.Generate(asset, source, output, reports.Add, CancellationToken.None);

        var proxy = await _probe.ProbeAsync(output);
        Assert.True(proxy.IsOk, proxy.Error);
        Assert.Equal(960, proxy.Width);
        Assert.Equal(540, proxy.Height);
        Assert.Null(proxy.Info?.AudioCodec);
        Assert.Equal(25.0, proxy.FrameRate!.Value.Value, 3);
        Assert.Equal(2.0, proxy.Duration.TotalSeconds, 1);
        Assert.Equal(1.0, reports[^1]);

        // O quadro do proxy em t corresponde ao do original em t (testsrc2 muda a cada quadro).
        var proxyAsset = await AssetAsync(output);
        using var frames = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);
        foreach (var t in new[] { 0.0, 0.52, 1.0, 1.96 })
        {
            var at = MediaTime.FromSecondsApprox(t);
            var a = frames.GetFrame(asset, source, at, 320, 180)!;
            var b = frames.GetFrame(proxyAsset, output, at, 320, 180)!;
            Assert.True(MeanDifference(a, b) < 12, $"t={t}: {MeanDifference(a, b)}");
        }
        Assert.False(File.Exists(output + ".tmp.mp4"));
    }

    [Fact]
    public async Task Proxy_de_video_vertical_girado_fica_em_pe_sem_metadado_de_rotacao()
    {
        var raw = Generate("deitado.mp4", "-f lavfi -i testsrc2=size=1280x720:rate=30 -t 1 -c:v libx264 -pix_fmt yuv420p");
        var rotated = Path.Combine(_root, "celular.mp4");
        SampleMedia.Ffmpeg($"-display_rotation 90 -i \"{raw}\" -c copy \"{rotated}\"");
        var asset = await AssetAsync(rotated);
        var output = Path.Combine(_root, "proxy-celular.mp4");

        ProxyGenerator.Generate(asset, rotated, output, null, CancellationToken.None);

        var proxy = await _probe.ProbeAsync(output);
        Assert.Equal(540, proxy.Width);
        Assert.Equal(960, proxy.Height);
        Assert.Equal(0, proxy.Info?.Rotation ?? 0);
    }

    [Fact]
    public async Task Fila_gera_avisa_e_apaga_o_proxy()
    {
        var source = Generate("1440p.mp4", "-f lavfi -i testsrc2=size=2560x1440:rate=24 -t 1 -c:v libx264 -pix_fmt yuv420p");
        var asset = await AssetAsync(source);
        Assert.True(ProxyPolicy.IsRecommended(asset));

        using var service = new ProxyService(_cache, NullLogger<ProxyService>.Instance);
        var states = new List<ProxyState>();
        var ready = new TaskCompletionSource();
        service.StatusChanged += (_, s) =>
        {
            lock (states) states.Add(s.State);
            if (s.State == ProxyState.Ready) ready.TrySetResult();
            if (s.State == ProxyState.Failed) ready.TrySetException(new InvalidOperationException(s.Error));
        };

        Assert.Equal(ProxyState.None, service.StatusOf(asset, source).State);
        service.Request(asset, source);
        service.Request(asset, source); // repetido: ignorado
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(ProxyState.Ready, service.StatusOf(asset, source).State);
        var path = service.ReadyPath(asset, source);
        Assert.NotNull(path);
        Assert.Equal(540, (await _probe.ProbeAsync(path)).Height);
        lock (states) Assert.Equal(1, states.Count(s => s == ProxyState.Queued));

        // A pré-visualização troca só os caminhos de vídeo; áudio e capturas continuam no original.
        var snapshot = new PreviewSnapshot(Project.CreateNew("p", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.UtcNow),
            new Sequence { Id = Guid.NewGuid(), Name = "s" }, new Dictionary<Guid, string> { [asset.Id] = source })
        {
            ProxyPaths = new Dictionary<Guid, string> { [asset.Id] = path },
        };
        Assert.Equal(path, snapshot.VideoPaths[asset.Id]);
        Assert.Equal(source, snapshot.Paths[asset.Id]);

        service.Delete(asset, source);
        Assert.Null(service.ReadyPath(asset, source));
        Assert.Equal(ProxyState.None, service.StatusOf(asset, source).State);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
