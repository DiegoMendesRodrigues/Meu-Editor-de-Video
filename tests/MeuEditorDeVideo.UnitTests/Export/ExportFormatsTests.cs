using System.IO;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Export;

public sealed class ExportFormatsTests
{
    private static readonly ProjectSettings Project = new() { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps30 };

    private static List<string> Problems(ExportSettings settings, ProjectSettings? project = null) =>
        ExportFormats.Validate(settings, project ?? Project).ToList();

    [Fact]
    public void Every_container_has_video_codecs_audio_options_and_an_extension()
    {
        foreach (var container in ExportFormats.Containers)
        {
            Assert.NotEmpty(ExportFormats.VideoCodecsFor(container));
            Assert.NotEmpty(ExportFormats.AudioCodecsFor(container));
            Assert.StartsWith(".", ExportFormats.Extension(container));
        }
    }

    [Fact]
    public void Gif_only_allows_no_audio()
    {
        var audio = ExportFormats.AudioCodecsFor(ExportContainer.Gif);

        Assert.Equal(AudioCodec.None, Assert.Single(audio));
    }

    [Fact]
    public void Extensions_match_the_container_formats()
    {
        Assert.Equal(".mp4", ExportFormats.Extension(ExportContainer.Mp4));
        Assert.Equal(".mpg", ExportFormats.Extension(ExportContainer.Mpeg));
        Assert.Equal(".gif", ExportFormats.Extension(ExportContainer.Gif));
    }

    [Theory]
    [InlineData(ExportContainer.Mov, VideoCodec.ProRes)]
    [InlineData(ExportContainer.Mov, VideoCodec.QtAnimation)]
    [InlineData(ExportContainer.Mkv, VideoCodec.Ffv1)]
    [InlineData(ExportContainer.WebM, VideoCodec.Vp9)]
    [InlineData(ExportContainer.Gif, VideoCodec.Gif)]
    public void Alpha_is_supported_for_alpha_capable_combinations(ExportContainer container, VideoCodec codec) =>
        Assert.True(ExportFormats.SupportsAlpha(container, codec));

    [Theory]
    [InlineData(ExportContainer.Mp4, VideoCodec.H264)]
    [InlineData(ExportContainer.Mov, VideoCodec.H264)]
    [InlineData(ExportContainer.WebM, VideoCodec.Av1)]
    public void Alpha_is_not_supported_for_other_combinations(ExportContainer container, VideoCodec codec) =>
        Assert.False(ExportFormats.SupportsAlpha(container, codec));

    [Fact]
    public void Normalize_switching_to_webm_picks_vp9_and_opus()
    {
        var settings = new ExportSettings { Container = ExportContainer.WebM, VideoCodec = VideoCodec.H264, AudioCodec = AudioCodec.Aac };

        var normalized = ExportFormats.Normalize(settings);

        Assert.Equal(VideoCodec.Vp9, normalized.VideoCodec);
        Assert.Equal(AudioCodec.Opus, normalized.AudioCodec);
    }

    [Fact]
    public void Normalize_keeps_valid_codecs_untouched()
    {
        var settings = new ExportSettings { Container = ExportContainer.Mkv, VideoCodec = VideoCodec.Hevc, AudioCodec = AudioCodec.Flac };

        Assert.Equal(settings, ExportFormats.Normalize(settings));
    }

    [Fact]
    public void Normalize_drops_alpha_when_the_format_cannot_hold_it()
    {
        var settings = new ExportSettings { Container = ExportContainer.Mp4, VideoCodec = VideoCodec.H264, Alpha = true };

        Assert.False(ExportFormats.Normalize(settings).Alpha);
    }

    [Fact]
    public void Normalize_keeps_alpha_when_the_format_supports_it()
    {
        var settings = new ExportSettings { Container = ExportContainer.Mov, VideoCodec = VideoCodec.ProRes, AudioCodec = AudioCodec.Pcm, Alpha = true };

        Assert.True(ExportFormats.Normalize(settings).Alpha);
    }

    [Fact]
    public void Default_settings_are_valid_for_a_default_project()
    {
        Assert.Empty(Problems(new ExportSettings()));
    }

    [Fact]
    public void Codec_not_allowed_in_container_is_reported()
    {
        var problems = Problems(new ExportSettings { Container = ExportContainer.Mp4, VideoCodec = VideoCodec.ProRes });

        Assert.Contains(problems, p => p.Contains("ProRes") && p.Contains("MP4"));
    }

    [Fact]
    public void Alpha_on_unsupported_format_is_reported()
    {
        var problems = Problems(new ExportSettings { Alpha = true });

        Assert.Contains(problems, p => p.Contains("transparência"));
    }

    [Fact]
    public void Odd_width_is_reported()
    {
        var problems = Problems(new ExportSettings { Width = 1921 });

        Assert.Contains(problems, p => p.Contains("pares"));
    }

    [Fact]
    public void Mpeg2_at_12_fps_is_reported_and_at_25_fps_is_accepted()
    {
        var settings = new ExportSettings
        {
            Container = ExportContainer.Mpeg,
            VideoCodec = VideoCodec.Mpeg2,
            AudioCodec = AudioCodec.Mp2,
            FrameRate = new FrameRate(12, 1),
        };

        Assert.Contains(Problems(settings), p => p.Contains("MPEG-2"));
        Assert.Empty(Problems(settings with { FrameRate = FrameRate.Fps25 }));
    }

    [Fact]
    public void Video_bitrate_out_of_range_is_reported()
    {
        var problems = Problems(new ExportSettings { VideoBitrateKbps = 50 });

        Assert.Contains(problems, p => p.Contains("taxa de bits do vídeo"));
    }

    [Fact]
    public void Quality_out_of_range_with_constant_quality_is_reported()
    {
        var problems = Problems(new ExportSettings { RateControl = RateControl.ConstantQuality, Quality = 60 });

        Assert.Contains(problems, p => p.Contains("qualidade constante"));
    }

    [Fact]
    public void Unsupported_sample_rate_is_reported()
    {
        var problems = Problems(new ExportSettings { SampleRate = 22_050 });

        Assert.Contains(problems, p => p.Contains("amostragem"));
    }

    [Fact]
    public void Unsupported_channel_count_is_reported()
    {
        var problems = Problems(new ExportSettings { AudioChannels = 3 });

        Assert.Contains(problems, p => p.Contains("mono, estéreo ou 5.1"));
    }

    [Fact]
    public void Max_seconds_per_file_below_one_is_reported()
    {
        var problems = Problems(new ExportSettings { MaxSecondsPerFile = 0.5 });

        Assert.Contains(problems, p => p.Contains("duração máxima"));
    }

    [Fact]
    public void Audio_checks_are_skipped_when_there_is_no_audio()
    {
        var settings = new ExportSettings
        {
            Container = ExportContainer.Mp4,
            AudioCodec = AudioCodec.None,
            SampleRate = 22_050,
            AudioChannels = 3,
            AudioBitrateKbps = 10,
        };

        Assert.Empty(Problems(settings));
    }

    [Fact]
    public void Resolve_falls_back_to_project_values_when_null()
    {
        var project = new ProjectSettings { Width = 1280, Height = 720, FrameRate = FrameRate.Fps25 };
        var settings = new ExportSettings();

        Assert.Equal(1280, settings.ResolveWidth(project));
        Assert.Equal(720, settings.ResolveHeight(project));
        Assert.Equal(FrameRate.Fps25, settings.ResolveFrameRate(project));
    }

    [Fact]
    public void Resolve_prefers_explicit_values_over_project_values()
    {
        var project = new ProjectSettings { Width = 1280, Height = 720, FrameRate = FrameRate.Fps25 };
        var settings = new ExportSettings { Width = 640, Height = 360, FrameRate = FrameRate.Fps60 };

        Assert.Equal(640, settings.ResolveWidth(project));
        Assert.Equal(360, settings.ResolveHeight(project));
        Assert.Equal(FrameRate.Fps60, settings.ResolveFrameRate(project));
    }

    [Fact]
    public void Part_path_adds_the_part_number_before_the_extension()
    {
        var path = ExportFormats.PartPath(Path.Combine("C:\\x", "v.mp4"), 2, 3);

        Assert.Equal(Path.Combine("C:\\x", "v (2).mp4"), path);
    }

    [Fact]
    public void Part_path_returns_input_for_a_single_part()
    {
        var input = Path.Combine("C:\\x", "v.mp4");

        Assert.Equal(input, ExportFormats.PartPath(input, 1, 1));
    }

    [Fact]
    public void Bitrate_for_target_size_is_about_2000_kbps_for_16_mb_in_60_seconds()
    {
        var kbps = ExportFormats.BitrateForTargetSize(16, 60, 128);

        // 16 MB * 8 * 1024 * 1024 / 1000 / 60 s * 0.98 - 128 kbps de áudio ≈ 2064 kbps.
        Assert.InRange(kbps, 2000, 2100);
    }

    [Fact]
    public void Bitrate_for_target_size_is_clamped_to_at_least_100_kbps()
    {
        Assert.Equal(100, ExportFormats.BitrateForTargetSize(0.001, 60, 128));
    }

    [Fact]
    public void Bitrate_for_target_size_returns_default_for_non_positive_duration()
    {
        Assert.Equal(1000, ExportFormats.BitrateForTargetSize(16, 0, 128));
        Assert.Equal(1000, ExportFormats.BitrateForTargetSize(16, -5, 128));
    }
}
