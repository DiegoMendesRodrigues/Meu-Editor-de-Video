using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.MediaEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

[Collection(SampleMediaCollection.Name)]
public sealed class ThumbnailAndWaveformTests : IDisposable
{
    private readonly SampleMedia _media;
    private readonly string _root;
    private readonly MediaCache _cache;
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);

    public ThumbnailAndWaveformTests(SampleMedia media)
    {
        _media = media;
        _root = Path.Combine(Path.GetTempPath(), "med-cache-tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _cache = new MediaCache(paths, NullLogger<MediaCache>.Instance);
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

    [Fact]
    public async Task Video_thumbnail_has_requested_width_and_is_cached()
    {
        var provider = new ThumbnailProvider(_cache, NullLogger<ThumbnailProvider>.Instance);
        var asset = await AssetAsync(_media.Video);

        var img = await provider.GetThumbnailAsync(asset, _media.Video, MediaTime.FromSeconds(1), 160);

        Assert.NotNull(img);
        Assert.Equal(160, img.Width);
        Assert.Equal(90, img.Height);
        Assert.Equal(160 * 90 * 4, img.Pixels.Length);
        Assert.Contains(img.Pixels, b => b != 0);
        Assert.True(File.Exists(_cache.ThumbnailPath(asset, _media.Video, MediaTime.FromSeconds(1), 160)));

        var again = await provider.GetThumbnailAsync(asset, _media.Video, MediaTime.FromSeconds(1), 160);
        Assert.Equal(img.Pixels, again!.Pixels);
    }

    [Fact]
    public async Task Rotated_video_thumbnail_is_portrait()
    {
        var provider = new ThumbnailProvider(_cache, NullLogger<ThumbnailProvider>.Instance);
        var asset = await AssetAsync(_media.Rotated);
        var img = await provider.GetThumbnailAsync(asset, _media.Rotated, MediaTime.Zero, 640);
        Assert.NotNull(img);
        Assert.True(img.Height > img.Width);
    }

    [Fact]
    public async Task Thumbnail_past_end_uses_last_frame()
    {
        var provider = new ThumbnailProvider(_cache, NullLogger<ThumbnailProvider>.Instance);
        var asset = await AssetAsync(_media.Video);
        var img = await provider.GetThumbnailAsync(asset, _media.Video, MediaTime.FromSeconds(60), 64);
        Assert.NotNull(img);
    }

    [Fact]
    public async Task Audio_has_no_thumbnail_but_has_waveform()
    {
        var thumbs = new ThumbnailProvider(_cache, NullLogger<ThumbnailProvider>.Instance);
        var waves = new WaveformProvider(_cache, NullLogger<WaveformProvider>.Instance);
        var asset = await AssetAsync(_media.Audio);

        Assert.Null(await thumbs.GetThumbnailAsync(asset, _media.Audio, MediaTime.Zero, 64));

        var w = await waves.GetWaveformAsync(asset, _media.Audio);
        Assert.NotNull(w);
        Assert.Equal(WaveformProvider.PeaksPerSecond, w.PeaksPerSecond);
        Assert.InRange(w.Count, 195, 205);                  // 2 s × 100 picos
        Assert.InRange(w.Max.Max(), 0.05f, 1.0f);            // senoide audível
        Assert.InRange(w.Min.Min(), -1.0f, -0.05f);
        Assert.InRange(w.Duration.TotalSeconds, 1.95, 2.05);

        var cached = await waves.GetWaveformAsync(asset, _media.Audio);
        Assert.Equal(w.Max, cached!.Max);
    }

    [Fact]
    public async Task Video_without_audio_has_no_waveform()
    {
        var waves = new WaveformProvider(_cache, NullLogger<WaveformProvider>.Instance);
        var asset = await AssetAsync(_media.VideoNoAudio);
        Assert.Null(await waves.GetWaveformAsync(asset, _media.VideoNoAudio));
    }

    [Fact]
    public async Task Sequence_thumbnail_reads_the_matching_frame()
    {
        var provider = new ThumbnailProvider(_cache, NullLogger<ThumbnailProvider>.Instance);
        var seq = ImageSequenceDetector.Detect(_media.SequenceFirstFrame)!;
        var r = await _probe.ProbeSequenceAsync(seq, FrameRate.Fps24);
        var first = seq.PathOf(seq.FirstNumber);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = MediaKind.ImageSequence, Name = "seq", OriginalPath = first,
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Sequence = seq,
        };
        var img = await provider.GetThumbnailAsync(asset, first, FrameRate.Fps24.FrameToTime(6), 100);
        Assert.NotNull(img);
        Assert.Equal(100, img.Width);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
