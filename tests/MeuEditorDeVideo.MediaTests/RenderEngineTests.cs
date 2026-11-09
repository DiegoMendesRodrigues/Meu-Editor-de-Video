using System.IO;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.MediaEngine.Playback;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>Exportação de ponta a ponta: timeline → arquivo validado pelo probe.</summary>
[Collection(SampleMediaCollection.Name)]
public sealed class RenderEngineTests(SampleMedia media) : IDisposable
{
    private readonly MediaProbe _probe = new(NullLogger<MediaProbe>.Instance);
    private readonly string _out = Path.Combine(media.Root, "exportações " + Guid.NewGuid().ToString("N")[..6]);

    private RenderEngine NewEngine() => new(
        new VideoFrameSourceFactory(NullLoggerFactory.Instance),
        new AudioSampleSourceFactory(NullLoggerFactory.Instance),
        new CpuCompositor(), _probe, NullLogger<RenderEngine>.Instance);

    private async Task<(Project Project, Dictionary<Guid, string> Paths)> ProjectAsync(int width = 320, int height = 180)
    {
        var r = await _probe.ProbeAsync(media.Video);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = r.Kind, Name = "vídeo", OriginalPath = media.Video,
            FileSize = new FileInfo(media.Video).Length, LastWriteUtc = File.GetLastWriteTimeUtc(media.Video),
            Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info,
        };
        var project = Project.CreateNew("Teste", new ProjectSettings { Width = width, Height = height, FrameRate = FrameRate.Fps25 },
            StorageMode.Linked, DateTimeOffset.UtcNow);
        project = new AddMediaAssetsCommand([asset]).Apply(project);
        var seq = project.MainSequence;
        var video = seq.Tracks.First(t => t.Kind == TrackKind.Video).Id;
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        project = new AddMediaToTimelineCommand(seq.Id, asset.Id, video, audio, MediaTime.Zero, MediaTime.FromSecondsApprox(5)).Apply(project);
        return (project, new Dictionary<Guid, string> { [asset.Id] = media.Video });
    }

    private RenderRequest Request(Project p, Dictionary<Guid, string> paths, ExportSettings s, string name) => new()
    {
        Project = p, SequenceId = p.MainSequence.Id, MediaPaths = paths, Settings = s,
        OutputPath = Path.Combine(_out, name + ExportFormats.Extension(s.Container)),
        WorkDirectory = Path.Combine(_out, "trabalho-" + name), HardwareDecoding = false,
    };

    public static TheoryData<ExportContainer, VideoCodec, AudioCodec, EncoderPreference> Formats => new()
    {
        { ExportContainer.Mp4, VideoCodec.H264, AudioCodec.Aac, EncoderPreference.Software },
        { ExportContainer.Mp4, VideoCodec.H264, AudioCodec.Aac, EncoderPreference.Auto },
        { ExportContainer.Mp4, VideoCodec.Hevc, AudioCodec.Aac, EncoderPreference.Software },
        { ExportContainer.Mp4, VideoCodec.Av1, AudioCodec.Aac, EncoderPreference.Software },
        { ExportContainer.Mov, VideoCodec.ProRes, AudioCodec.Pcm, EncoderPreference.Software },
        { ExportContainer.Mov, VideoCodec.QtAnimation, AudioCodec.Aac, EncoderPreference.Software },
        { ExportContainer.Mkv, VideoCodec.Ffv1, AudioCodec.Flac, EncoderPreference.Software },
        { ExportContainer.Mkv, VideoCodec.Vp9, AudioCodec.Opus, EncoderPreference.Software },
        { ExportContainer.WebM, VideoCodec.Vp9, AudioCodec.Vorbis, EncoderPreference.Software },
        { ExportContainer.Avi, VideoCodec.Mjpeg, AudioCodec.Mp3, EncoderPreference.Software },
        { ExportContainer.Avi, VideoCodec.H264, AudioCodec.Pcm, EncoderPreference.Software },
        { ExportContainer.Mpeg, VideoCodec.Mpeg2, AudioCodec.Mp2, EncoderPreference.Software },
        { ExportContainer.Gif, VideoCodec.Gif, AudioCodec.None, EncoderPreference.Software },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Exports_every_format_with_expected_duration(ExportContainer c, VideoCodec v, AudioCodec a, EncoderPreference e)
    {
        var (p, paths) = await ProjectAsync();
        var s = new ExportSettings { Container = c, VideoCodec = v, AudioCodec = a, Encoder = e, VideoBitrateKbps = 1000, AudioBitrateKbps = 128 };
        var req = Request(p, paths, s, $"{c}_{v}_{a}_{e}");

        var result = await NewEngine().RenderAsync(req);

        var file = Assert.Single(result.Files);
        var probe = await _probe.ProbeAsync(file);
        Assert.True(probe.IsOk, probe.Error);
        Assert.Equal(320, probe.Width);
        Assert.Equal(180, probe.Height);
        Assert.InRange(probe.Duration.TotalSeconds, 2.9, 3.1);
        Assert.False(Directory.Exists(req.WorkDirectory));
    }

    [Theory]
    [InlineData(ExportContainer.Mov, VideoCodec.ProRes)]
    [InlineData(ExportContainer.Mkv, VideoCodec.Ffv1)]
    [InlineData(ExportContainer.WebM, VideoCodec.Vp9)]
    [InlineData(ExportContainer.Gif, VideoCodec.Gif)]
    public async Task Exports_with_alpha(ExportContainer c, VideoCodec v)
    {
        var (p, paths) = await ProjectAsync();
        var s = ExportFormats.Normalize(new ExportSettings { Container = c, VideoCodec = v, Alpha = true, Encoder = EncoderPreference.Software });
        var result = await NewEngine().RenderAsync(Request(p, paths, s, $"alfa_{c}"));
        var probe = await _probe.ProbeAsync(Assert.Single(result.Files));
        Assert.True(probe.IsOk, probe.Error);
    }

    [Fact]
    public async Task Range_and_split_into_parts()
    {
        var (p, paths) = await ProjectAsync();
        var s = new ExportSettings { Encoder = EncoderPreference.Software, MaxSecondsPerFile = 1 };
        var req = Request(p, paths, s, "partes") with
        {
            RangeStart = MediaTime.FromSecondsApprox(0.5),
            RangeEnd = MediaTime.FromSecondsApprox(2.5),
        };
        var result = await NewEngine().RenderAsync(req);
        Assert.Equal(2, result.Files.Count);
        Assert.EndsWith(" (1).mp4", result.Files[0], StringComparison.Ordinal);
        foreach (var f in result.Files)
        {
            var probe = await _probe.ProbeAsync(f);
            Assert.InRange(probe.Duration.TotalSeconds, 0.95, 1.06);
        }
    }

    [Fact]
    public async Task Resolution_and_frame_rate_override()
    {
        var (p, paths) = await ProjectAsync();
        var s = new ExportSettings { Encoder = EncoderPreference.Software, Width = 180, Height = 320, FrameRate = FrameRate.Fps30 };
        var result = await NewEngine().RenderAsync(Request(p, paths, s, "vertical"));
        var probe = await _probe.ProbeAsync(Assert.Single(result.Files));
        Assert.Equal(180, probe.Width);
        Assert.Equal(320, probe.Height);
        Assert.Equal(30, probe.FrameRate!.Value.Value, 1);
    }

    [Fact]
    public async Task Cancellation_keeps_completed_segments_and_resume_finishes()
    {
        var (p, paths) = await ProjectAsync();
        var s = new ExportSettings { Encoder = EncoderPreference.Software };
        var req = Request(p, paths, s, "retomar");
        using var cts = new CancellationTokenSource();
        var progress = new Progress<RenderProgress>(pr => { if (pr.FramesDone > 10) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewEngine().RenderAsync(req, progress, cts.Token));
        Assert.False(File.Exists(req.OutputPath));

        var reports = new List<RenderProgress>();
        var result = await NewEngine().RenderAsync(req, new SyncProgress(reports.Add));
        Assert.True(File.Exists(Assert.Single(result.Files)));
        Assert.Contains(reports, r => r.Stage == RenderStage.Muxing);
        Assert.Equal(1, reports[^1].Fraction, 2);
    }

    [Fact]
    public async Task Missing_media_is_reported_in_portuguese()
    {
        var (p, _) = await ProjectAsync();
        var paths = new Dictionary<Guid, string> { [p.Media[0].Id] = Path.Combine(media.Root, "não existe.mp4") };
        var ex = await Assert.ThrowsAsync<MedException>(() => NewEngine().RenderAsync(Request(p, paths, new ExportSettings(), "faltando")));
        Assert.Equal("MED-3001", ex.Error.Code);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected()
    {
        var (p, paths) = await ProjectAsync();
        var s = new ExportSettings { Container = ExportContainer.WebM, VideoCodec = VideoCodec.H264 };
        var ex = await Assert.ThrowsAsync<MedException>(() => NewEngine().RenderAsync(Request(p, paths, s, "invalido")));
        Assert.Equal("MED-3008", ex.Error.Code);
    }

    private sealed class SyncProgress(Action<RenderProgress> action) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => action(value);
    }

    /// <summary>F8: legendas queimadas no vídeo ou só em arquivo .srt ao lado; títulos sempre desenhados.</summary>
    [Fact]
    public async Task Exporta_legendas_queimadas_ou_em_arquivo()
    {
        var (p, paths) = await ProjectAsync();
        var seq = p.MainSequence;
        p = new AddTextClipCommand(seq.Id, TextPresets.Create(TextKind.Title, p.Settings, MediaTime.Zero), TrackKind.Text).Apply(p);
        var subs = SubtitleOps.ToClips([new SubtitleEntry(MediaTime.Zero, MediaTime.FromSeconds(3), "LEGENDA DE TESTE")], p.Settings);
        p = new ImportSubtitlesCommand(seq.Id, subs).Apply(p);

        var burned = await NewEngine().RenderAsync(Request(p, paths, new ExportSettings { VideoBitrateKbps = 2000 }, "queimada"));
        var plain = await NewEngine().RenderAsync(Request(p, paths,
            new ExportSettings { VideoBitrateKbps = 2000, BurnSubtitles = false, SubtitleFile = SubtitleFileExport.Srt }, "arquivo"));

        var srt = Path.ChangeExtension(Assert.Single(plain.Files), ".srt");
        Assert.True(File.Exists(srt));
        var cue = Assert.Single(Core.Subtitles.SubtitleFile.Load(srt));
        Assert.Equal("LEGENDA DE TESTE", cue.Text);
        Assert.False(File.Exists(Path.ChangeExtension(Assert.Single(burned.Files), ".srt")));

        // Faixa de baixo (onde fica a legenda) difere entre as duas exportações; o título (centro) aparece nas duas.
        using var frames = new VideoFrameSourceFactory(NullLoggerFactory.Instance).Create(false);
        async Task<Core.Media.BgraImage> FrameAsync(string file)
        {
            var r = await _probe.ProbeAsync(file);
            var asset = new MediaAsset { Id = Guid.NewGuid(), Kind = r.Kind, Name = "e", OriginalPath = file, Duration = r.Duration, Width = r.Width, Height = r.Height, FrameRate = r.FrameRate, Info = r.Info };
            return frames.GetFrame(asset, file, MediaTime.FromSecondsApprox(1.5), 320, 180)!;
        }
        var a = await FrameAsync(burned.Files[0]);
        var b = await FrameAsync(plain.Files[0]);
        double Diff(int y0, int y1)
        {
            long sum = 0;
            for (var y = y0; y < y1; y++)
                for (var x = 0; x < 320; x++)
                    for (var c = 0; c < 3; c++) sum += Math.Abs(a.Pixels[(y * 320 + x) * 4 + c] - b.Pixels[(y * 320 + x) * 4 + c]);
            return sum / ((y1 - y0) * 320 * 3.0);
        }
        Assert.True(Diff(150, 175) > 8, $"faixa da legenda: {Diff(150, 175)}");
        Assert.True(Diff(60, 120) < 4, $"centro: {Diff(60, 120)}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_out, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
