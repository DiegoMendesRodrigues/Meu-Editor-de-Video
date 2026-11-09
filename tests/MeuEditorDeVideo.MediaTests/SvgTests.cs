using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>Importação de SVG como imagem vetorial: sondagem, quadros, nitidez em escala e miniaturas.</summary>
[Collection(SampleMediaCollection.Name)]
public sealed class SvgTests : IDisposable
{
    private const string Svg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\" viewBox=\"0 0 200 100\">" +
        "<rect x=\"50\" y=\"25\" width=\"100\" height=\"50\" fill=\"#FF0000\"/></svg>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-svg-tests", Guid.NewGuid().ToString("N"));
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);
    private readonly MediaCache _cache;

    public SvgTests()
    {
        Directory.CreateDirectory(_root);
        _cache = new MediaCache(new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local")), NullLogger<MediaCache>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string WriteSvg(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
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

    private static (byte B, byte G, byte R, byte A) PixelAt(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    [Fact]
    public async Task Svg_is_probed_as_image_with_alpha()
    {
        var path = WriteSvg("gráfico vetorial.svg", Svg);

        var r = await _probe.ProbeAsync(path);

        Assert.True(r.IsOk, r.Error);
        Assert.Equal(MediaKind.Image, r.Kind);
        Assert.Equal(200, r.Width);
        Assert.Equal(100, r.Height);
        Assert.Equal(MediaTime.Zero, r.Duration);
        Assert.True(r.Info.HasAlpha);
        Assert.Equal("svg", r.Info.VideoCodec);
    }

    [Fact]
    public async Task Frame_source_fits_svg_preserving_aspect_with_transparent_corners()
    {
        var path = WriteSvg("quadro.svg", Svg);
        var asset = await AssetAsync(path);
        using var frames = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);

        var img = frames.GetFrame(asset, path, MediaTime.Zero, 640, 640);

        Assert.NotNull(img);
        Assert.Equal(640, img.Width);
        Assert.Equal(320, img.Height);
        Assert.Equal(0, PixelAt(img, 0, 0).A);
        var center = PixelAt(img, 320, 160);
        Assert.True(center.R > 250, $"R={center.R}");
        Assert.True(center.G < 5, $"G={center.G}");
        Assert.True(center.B < 5, $"B={center.B}");
        Assert.Equal(255, center.A);
    }

    [Fact]
    public void Render_at_large_size_is_sharp_vector()
    {
        var path = WriteSvg("grande.svg", Svg);

        var img = SvgRenderer.Render(path, 2000, 2000);

        Assert.NotNull(img);
        Assert.Equal(2000, img.Width);
        Assert.Equal(1000, img.Height);
        // Logo dentro da borda do retângulo (x 500..1500, y 250..750): totalmente opaco, sem borda difusa.
        Assert.Equal(255, PixelAt(img, 501, 500).A);
        Assert.Equal(0, PixelAt(img, 490, 500).A);
    }

    [Fact]
    public async Task Thumbnail_is_rendered_from_svg()
    {
        var path = WriteSvg("miniatura.svg", Svg);
        var asset = await AssetAsync(path);
        var provider = new ThumbnailProvider(_cache, NullLogger<ThumbnailProvider>.Instance);

        var img = await provider.GetThumbnailAsync(asset, path, MediaTime.Zero, 160);

        Assert.NotNull(img);
        Assert.Equal(160, img.Width);
        Assert.Equal(80, img.Height);
        Assert.Contains(img.Pixels, b => b != 0);
    }

    [Fact]
    public async Task Invalid_svg_fails_probe_with_message()
    {
        var path = WriteSvg("invalido.svg", "not svg");

        var r = await _probe.ProbeAsync(path);

        Assert.False(r.IsOk);
        Assert.Equal(MediaProbeStatus.Corrupted, r.Status);
        Assert.Equal("O arquivo SVG não pôde ser lido.", r.Error);
    }
}
