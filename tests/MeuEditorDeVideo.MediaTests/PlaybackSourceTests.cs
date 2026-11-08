using System.IO;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

[Collection(SampleMediaCollection.Name)]
public sealed class PlaybackSourceTests(SampleMedia media)
{
    private const int Ch = 2;
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);

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

    private static MediaTime At(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static VideoFrameSource NewVideo(bool hardware = false) =>
        new(hardware, NullLogger<VideoFrameSource>.Instance);

    private static AudioSampleSource NewAudio() => new(NullLogger<AudioSampleSource>.Instance);

    // ---------------------------------------------------------------- VideoFrameSource

    [Fact]
    public async Task Frame_fits_box_and_preserves_aspect_ratio()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Video);

        var img = source.GetFrame(asset, media.Video, MediaTime.Zero, 320, 180);

        Assert.NotNull(img);
        Assert.True(img.Width <= 320, $"Largura {img.Width} excede 320.");
        Assert.True(img.Height <= 180, $"Altura {img.Height} excede 180.");
        Assert.InRange((double)img.Width / img.Height, 640.0 / 360.0 - 0.02, 640.0 / 360.0 + 0.02);
        Assert.Equal(img.Width * img.Height * 4, img.Pixels.Length);
        Assert.Contains(img.Pixels, b => b != 0);
    }

    [Fact]
    public async Task Sequential_reads_at_frame_steps_return_frames()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Video);

        for (var i = 0; i < 10; i++)
        {
            var img = source.GetFrame(asset, media.Video, At(i / 30.0), 320, 180);
            Assert.NotNull(img);
        }
    }

    [Fact]
    public async Task Backward_seek_after_forward_reading_returns_frame()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Video);

        Assert.NotNull(source.GetFrame(asset, media.Video, At(2.0), 320, 180));
        Assert.NotNull(source.GetFrame(asset, media.Video, At(0.5), 320, 180));
        Assert.NotNull(source.GetFrame(asset, media.Video, MediaTime.Zero, 320, 180));
    }

    [Fact]
    public async Task Image_asset_returns_same_instance_for_same_size()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Image);
        Assert.Equal(MediaKind.Image, asset.Kind);

        var first = source.GetFrame(asset, media.Image, MediaTime.Zero, 160, 120);
        var second = source.GetFrame(asset, media.Image, MediaTime.Zero, 160, 120);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task Time_beyond_duration_returns_last_frame()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Video);
        Assert.NotNull(source.GetFrame(asset, media.Video, At(1.0), 320, 180));

        var beyond = source.GetFrame(asset, media.Video, MediaTime.FromSeconds(60), 320, 180);

        Assert.NotNull(beyond);
    }

    [Fact]
    public async Task Trim_with_empty_set_then_get_frame_still_works()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Video);
        Assert.NotNull(source.GetFrame(asset, media.Video, MediaTime.Zero, 320, 180));

        source.Trim(new HashSet<Guid>());

        Assert.NotNull(source.GetFrame(asset, media.Video, MediaTime.Zero, 320, 180));
    }

    [Fact]
    public async Task Hardware_decoding_request_still_returns_frames()
    {
        using var source = NewVideo(hardware: true);
        var asset = await AssetAsync(media.Video);

        var img = source.GetFrame(asset, media.Video, At(0.5), 320, 180);

        Assert.NotNull(img);
    }

    [Fact]
    public async Task Asset_without_video_returns_null()
    {
        using var source = NewVideo();
        var asset = await AssetAsync(media.Audio);

        Assert.Null(source.GetFrame(asset, media.Audio, MediaTime.Zero, 320, 180));
    }

    // ---------------------------------------------------------------- AudioSampleSource

    [Fact]
    public async Task Read_4800_frames_at_zero_returns_non_silent_samples()
    {
        using var source = NewAudio();
        var asset = await AssetAsync(media.Video);
        var buffer = new float[4800 * Ch];

        var frames = source.Read(asset, media.Video, MediaTime.Zero, buffer);

        Assert.Equal(4800, frames);
        Assert.Contains(buffer, v => v != 0f);
    }

    [Fact]
    public async Task Contiguous_reads_continue_the_same_stream()
    {
        var asset = await AssetAsync(media.Video);

        using var single = NewAudio();
        var whole = new float[9600 * Ch];
        Assert.Equal(9600, single.Read(asset, media.Video, MediaTime.Zero, whole));

        using var split = NewAudio();
        var first = new float[4800 * Ch];
        var second = new float[4800 * Ch];
        Assert.Equal(4800, split.Read(asset, media.Video, MediaTime.Zero, first));
        Assert.Equal(4800, split.Read(asset, media.Video, At(0.1), second));
        Assert.Contains(second, v => v != 0f);

        Assert.Equal(whole, first.Concat(second).ToArray());
    }

    [Fact]
    public async Task Seek_to_one_second_returns_samples()
    {
        using var source = NewAudio();
        var asset = await AssetAsync(media.Video);
        var buffer = new float[4800 * Ch];
        Assert.Equal(4800, source.Read(asset, media.Video, MediaTime.Zero, buffer));

        var jumped = new float[4800 * Ch];
        var frames = source.Read(asset, media.Video, At(1.0), jumped);

        Assert.Equal(4800, frames);
        Assert.Contains(jumped, v => v != 0f);
    }

    [Fact]
    public async Task Audio_only_file_returns_samples_resampled_to_48k()
    {
        using var source = NewAudio();
        var asset = await AssetAsync(media.Audio);
        var buffer = new float[4800 * Ch];

        var frames = source.Read(asset, media.Audio, MediaTime.Zero, buffer);

        Assert.Equal(4800, frames);
        Assert.Contains(buffer, v => v != 0f);
    }

    [Fact]
    public async Task Asset_without_audio_returns_zero()
    {
        using var source = NewAudio();
        var asset = await AssetAsync(media.VideoNoAudio);
        var buffer = new float[4800 * Ch];

        Assert.Equal(0, source.Read(asset, media.VideoNoAudio, MediaTime.Zero, buffer));
    }
}
