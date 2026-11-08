using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

[Collection(SampleMediaCollection.Name)]
public sealed class MediaProbeTests(SampleMedia media)
{
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);

    [Fact]
    public async Task Video_with_audio_reports_all_metadata()
    {
        var r = await _probe.ProbeAsync(media.Video);

        Assert.True(r.IsOk, r.Error);
        Assert.Equal(MediaKind.Video, r.Kind);
        Assert.Equal(640, r.Width);
        Assert.Equal(360, r.Height);
        Assert.Equal(FrameRate.Fps25, r.FrameRate);
        Assert.InRange(r.Duration.TotalSeconds, 2.9, 3.2);
        Assert.Equal("h264", r.Info.VideoCodec);
        Assert.Equal("aac", r.Info.AudioCodec);
        Assert.Equal(48000, r.Info.SampleRate);
        Assert.Equal(2, r.Info.Channels);
        Assert.Contains("mp4", r.Info.Container);
        Assert.Equal(1, r.Info.VideoStreams);
        Assert.Equal(1, r.Info.AudioStreams);
        Assert.False(r.Info.HasAlpha);
    }

    [Fact]
    public async Task Rotated_video_swaps_dimensions()
    {
        var r = await _probe.ProbeAsync(media.Rotated);
        Assert.True(r.IsOk, r.Error);
        Assert.Contains(r.Info.Rotation, new[] { 90, 270 });
        Assert.Equal(360, r.Width);
        Assert.Equal(640, r.Height);
    }

    [Fact]
    public async Task Audio_file_is_audio()
    {
        var r = await _probe.ProbeAsync(media.Audio);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal(MediaKind.Audio, r.Kind);
        Assert.Equal(44100, r.Info.SampleRate);
        Assert.InRange(r.Duration.TotalSeconds, 1.9, 2.1);
    }

    [Fact]
    public async Task Png_is_image_without_duration()
    {
        var r = await _probe.ProbeAsync(media.Image);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal(MediaKind.Image, r.Kind);
        Assert.Equal(320, r.Width);
        Assert.Equal(MediaTime.Zero, r.Duration);
    }

    [Fact]
    public async Task Gifs_are_classified_by_frame_count()
    {
        Assert.Equal(MediaKind.AnimatedGif, (await _probe.ProbeAsync(media.AnimatedGif)).Kind);
        Assert.Equal(MediaKind.Image, (await _probe.ProbeAsync(media.StillGif)).Kind);
    }

    [Theory]
    [InlineData(nameof(SampleMedia.Truncated))]
    [InlineData(nameof(SampleMedia.Garbage))]
    public async Task Damaged_files_are_reported_as_corrupted(string which)
    {
        var path = (string)typeof(SampleMedia).GetProperty(which)!.GetValue(media)!;
        var r = await _probe.ProbeAsync(path);
        Assert.Equal(MediaProbeStatus.Corrupted, r.Status);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public async Task Unknown_format_is_unsupported()
    {
        var r = await _probe.ProbeAsync(media.TextFile);
        Assert.Equal(MediaProbeStatus.Unsupported, r.Status);
    }

    [Fact]
    public async Task Image_sequence_is_detected_and_probed()
    {
        var seq = ImageSequenceDetector.Detect(media.SequenceFirstFrame);
        Assert.NotNull(seq);
        Assert.Equal(1, seq.FirstNumber);
        Assert.Equal(12, seq.FrameCount);
        Assert.Equal(4, seq.Digits);
        Assert.Equal("quadro_", seq.Prefix);

        var r = await _probe.ProbeSequenceAsync(seq, FrameRate.Fps24);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal(MediaKind.ImageSequence, r.Kind);
        Assert.Equal(320, r.Width);
        Assert.Equal(FrameRate.Fps24.FrameToTime(12), r.Duration);
    }

    [Fact]
    public void Single_numbered_image_is_not_a_sequence() =>
        Assert.Null(ImageSequenceDetector.Detect(media.Image));
}
