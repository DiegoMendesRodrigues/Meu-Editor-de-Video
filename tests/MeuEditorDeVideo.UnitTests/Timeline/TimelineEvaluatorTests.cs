using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Timeline;

public sealed class TimelineEvaluatorTests
{
    // Índices das trilhas criadas por Project.CreateNew: Vídeo 1, Vídeo 2, Áudio 1, Texto 1.
    private const int Video1 = 0;
    private const int Video2 = 1;
    private const int Audio1 = 2;

    private static MediaTime T(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset VideoAsset(string name = "v.mp4", long seconds = 10, bool withAudio = false) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = name,
        OriginalPath = @"C:\m\" + name,
        Duration = MediaTime.FromSeconds(seconds),
        Width = 640,
        Height = 360,
        FrameRate = FrameRate.Fps30,
        Info = withAudio ? new MediaTechnicalInfo { AudioCodec = "aac" } : null,
    };

    private static MediaAsset ImageAsset(string name = "imagem.png") => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Image,
        Name = name,
        OriginalPath = @"C:\m\" + name,
        Width = 320,
        Height = 240,
    };

    private static MediaAsset AudioAsset(string name = "som.wav", long seconds = 20) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Audio,
        Name = name,
        OriginalPath = @"C:\m\" + name,
        Duration = MediaTime.FromSeconds(seconds),
    };

    private static Clip MakeClip(MediaAsset? asset, double start, double duration, double sourceIn = 0,
        Rational? speed = null, double opacity = 1, double volume = 1) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = asset?.Id,
        Start = T(start),
        Duration = T(duration),
        SourceIn = T(sourceIn),
        Speed = speed ?? Rational.One,
        Opacity = opacity,
        Volume = volume,
    };

    private static Project NewProject(ProjectSettings? settings, params MediaAsset[] media) =>
        Project.CreateNew("P", settings ?? new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [.. media] };

    /// <summary>Substitui os clipes da trilha (os clipes devem vir ordenados e sem sobreposição).</summary>
    private static Project Place(Project project, int trackIndex, params Clip[] clips) =>
        UpdateTrack(project, trackIndex, t => t with { Clips = [.. clips] });

    private static Project UpdateTrack(Project project, int trackIndex, Func<Track, Track> change)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(trackIndex, change(seq.Tracks[trackIndex])) });
    }

    private static Project AppendTrack(Project project, Track track)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.Add(track) });
    }

    private static Track AudioTrack(string name) => new() { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = name };

    // ---------------------------------------------------------------- Evaluate

    [Fact]
    public void Evaluate_orders_layers_bottom_to_top()
    {
        var upper = VideoAsset("cima.mp4");
        var lower = VideoAsset("baixo.mp4");
        var upperClip = MakeClip(upper, 0, 5);
        var lowerClip = MakeClip(lower, 0, 5);
        var p = NewProject(null, upper, lower);
        p = Place(p, Video1, upperClip);
        p = Place(p, Video2, lowerClip);

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(1));

        Assert.Equal(2, graph.Layers.Count);
        // Trilha de índice 1 (Vídeo 2) é desenhada primeiro; a de índice 0 (Vídeo 1) fica por cima.
        Assert.Equal(lowerClip.Id, graph.Layers[0].ClipId);
        Assert.Equal(p.MainSequence.Tracks[Video2].Id, graph.Layers[0].TrackId);
        Assert.Equal(upperClip.Id, graph.Layers[1].ClipId);
        Assert.Equal(p.MainSequence.Tracks[Video1].Id, graph.Layers[1].TrackId);
    }

    [Fact]
    public void Evaluate_skips_hidden_tracks()
    {
        var a = VideoAsset("a.mp4");
        var b = VideoAsset("b.mp4");
        var visible = MakeClip(a, 0, 5);
        var hidden = MakeClip(b, 0, 5);
        var p = NewProject(null, a, b);
        p = Place(p, Video1, visible);
        p = Place(p, Video2, hidden);
        p = UpdateTrack(p, Video2, t => t with { IsHidden = true });

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(1));

        var layer = Assert.Single(graph.Layers);
        Assert.Equal(visible.Id, layer.ClipId);
    }

    [Fact]
    public void Evaluate_ignores_audio_tracks()
    {
        var media = VideoAsset("v.mp4", withAudio: true);
        var p = NewProject(null, media);
        p = Place(p, Audio1, MakeClip(media, 0, 5));

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(1));

        Assert.True(graph.IsEmpty);
    }

    [Fact]
    public void Evaluate_in_a_gap_returns_empty_graph_with_time_and_size()
    {
        var media = VideoAsset();
        var p = NewProject(null, media);
        p = Place(p, Video1, MakeClip(media, 1, 1));

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(3));

        Assert.True(graph.IsEmpty);
        Assert.Equal(T(3), graph.Time);
        Assert.Equal(1920, graph.Width);
        Assert.Equal(1080, graph.Height);
    }

    [Fact]
    public void Evaluate_uses_project_resolution()
    {
        var media = VideoAsset();
        var p = NewProject(new ProjectSettings { Width = 1280, Height = 720 }, media);
        p = Place(p, Video1, MakeClip(media, 0, 5));

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(1));

        Assert.Equal(1280, graph.Width);
        Assert.Equal(720, graph.Height);
    }

    [Fact]
    public void Evaluate_source_time_applies_source_in_and_speed()
    {
        // Clipe começa em 2 s, dura 4 s na timeline, entra na mídia em 1 s, velocidade 2x.
        // Em t = 3 s, já se passou 1 s da timeline = 2 s de mídia: origem = 1 + 2 = 3 s.
        var media = VideoAsset();
        var p = NewProject(null, media);
        p = Place(p, Video1, MakeClip(media, 2, 4, sourceIn: 1, speed: new Rational(2, 1)));

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(3));

        var layer = Assert.Single(graph.Layers);
        Assert.Equal(T(3), layer.SourceTime);
    }

    [Fact]
    public void Evaluate_start_is_inclusive_and_end_is_exclusive()
    {
        var media = VideoAsset();
        var clip = MakeClip(media, 1, 1);
        var p = NewProject(null, media);
        p = Place(p, Video1, clip);

        var atStart = TimelineEvaluator.Evaluate(p, p.MainSequence, T(1));
        var atEnd = TimelineEvaluator.Evaluate(p, p.MainSequence, T(2));

        Assert.Single(atStart.Layers);
        Assert.True(atEnd.IsEmpty);
    }

    [Fact]
    public void Evaluate_clamps_source_time_before_media_end()
    {
        // Mídia de 10 s; clipe entra em 9 s e ainda dura 5 s: em t = 2 s a origem seria 11 s.
        var media = VideoAsset(seconds: 10);
        var p = NewProject(null, media);
        p = Place(p, Video1, MakeClip(media, 0, 5, sourceIn: 9));

        var graph = TimelineEvaluator.Evaluate(p, p.MainSequence, T(2));

        var layer = Assert.Single(graph.Layers);
        Assert.Equal(media.Duration - new MediaTime(1), layer.SourceTime);
    }

    [Fact]
    public void Evaluate_skips_zero_opacity_and_keeps_partial_opacity()
    {
        var media = VideoAsset();
        var p = NewProject(null, media);
        p = Place(p, Video1, MakeClip(media, 0, 5, opacity: 0));
        Assert.True(TimelineEvaluator.Evaluate(p, p.MainSequence, T(1)).IsEmpty);

        p = Place(p, Video1, MakeClip(media, 0, 5, opacity: 0.4));
        var layer = Assert.Single(TimelineEvaluator.Evaluate(p, p.MainSequence, T(1)).Layers);
        Assert.Equal(0.4, layer.Opacity);
    }

    [Fact]
    public void Evaluate_skips_clips_without_media()
    {
        var p = NewProject(null);
        p = Place(p, Video1, MakeClip(null, 0, 5));

        Assert.True(TimelineEvaluator.Evaluate(p, p.MainSequence, T(1)).IsEmpty);
    }

    [Fact]
    public void Evaluate_includes_image_clips_without_clamping()
    {
        var image = ImageAsset();
        var p = NewProject(null, image);
        p = Place(p, Video1, MakeClip(image, 0, 5, sourceIn: 100));

        var layer = Assert.Single(TimelineEvaluator.Evaluate(p, p.MainSequence, T(1)).Layers);
        Assert.Equal(T(101), layer.SourceTime);
    }

    // ---------------------------------------------------------------- ClipAt

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 0)]
    [InlineData(1.0, -1)]
    [InlineData(1.5, -1)]
    [InlineData(2.0, 1)]
    [InlineData(2.99, 1)]
    [InlineData(3.0, -1)]
    [InlineData(5.0, 2)]
    [InlineData(5.99, 2)]
    [InlineData(7.0, -1)]
    [InlineData(8.5, 3)]
    [InlineData(9.0, -1)]
    [InlineData(-1.0, -1)]
    public void ClipAt_binary_search_finds_covering_clip(double seconds, int expectedIndex)
    {
        var media = VideoAsset();
        var clips = new[] { MakeClip(media, 0, 1), MakeClip(media, 2, 1), MakeClip(media, 5, 1), MakeClip(media, 8, 1) };
        var p = Place(NewProject(null, media), Video1, clips);
        var track = p.MainSequence.Tracks[Video1];

        var found = TimelineEvaluator.ClipAt(track, T(seconds));

        if (expectedIndex < 0) Assert.Null(found);
        else Assert.Equal(clips[expectedIndex].Id, found!.Id);
    }

    [Fact]
    public void ClipAt_on_empty_track_returns_null()
    {
        var p = NewProject(null);

        Assert.Null(TimelineEvaluator.ClipAt(p.MainSequence.Tracks[Video1], T(1)));
    }

    // ---------------------------------------------------------------- AudioIn

    [Fact]
    public void AudioIn_returns_overlapping_segments_clipped_to_window()
    {
        var music = AudioAsset("musica.wav");
        var clipA = MakeClip(music, 0, 4);   // Áudio 1: [0, 4)
        var clipB = MakeClip(music, 3, 3);   // Áudio 2: [3, 6)
        var p = NewProject(null, music);
        p = Place(p, Audio1, clipA);
        p = AppendTrack(p, AudioTrack("Áudio 2"));
        p = Place(p, p.MainSequence.Tracks.Count - 1, clipB);

        var segments = TimelineEvaluator.AudioIn(p, p.MainSequence, T(2), T(5));

        Assert.Equal(2, segments.Count);
        var a = segments.Single(s => s.ClipId == clipA.Id);
        Assert.Equal(T(2), a.TimelineStart);
        Assert.Equal(T(4), a.TimelineEnd);
        Assert.Equal(T(2), a.SourceStart);
        var b = segments.Single(s => s.ClipId == clipB.Id);
        Assert.Equal(T(3), b.TimelineStart);
        Assert.Equal(T(5), b.TimelineEnd);
        Assert.Equal(T(0), b.SourceStart);
    }

    [Fact]
    public void AudioIn_source_start_is_correct_when_window_starts_mid_clip()
    {
        // Clipe em [1, 6) com entrada na mídia em 2 s. Janela [3, 4): origem = 2 + (3 - 1) = 4 s.
        var music = AudioAsset();
        var clip = MakeClip(music, 1, 5, sourceIn: 2);
        var p = Place(NewProject(null, music), Audio1, clip);

        var segment = Assert.Single(TimelineEvaluator.AudioIn(p, p.MainSequence, T(3), T(4)));

        Assert.Equal(T(3), segment.TimelineStart);
        Assert.Equal(T(4), segment.TimelineEnd);
        Assert.Equal(T(4), segment.SourceStart);
    }

    [Fact]
    public void AudioIn_source_start_accounts_for_speed_when_window_starts_mid_clip()
    {
        // Clipe em [1, 5) com entrada em 2 s e velocidade 2x. Janela começa em 2 s: origem = 2 + 2 * (2 - 1) = 4 s.
        var music = AudioAsset();
        var clip = MakeClip(music, 1, 4, sourceIn: 2, speed: new Rational(2, 1));
        var p = Place(NewProject(null, music), Audio1, clip);

        var segment = Assert.Single(TimelineEvaluator.AudioIn(p, p.MainSequence, T(2), T(3)));

        Assert.Equal(T(4), segment.SourceStart);
        Assert.Equal(new Rational(2, 1), segment.Speed);
    }

    [Fact]
    public void AudioIn_skips_muted_tracks()
    {
        var music = AudioAsset();
        var p = Place(NewProject(null, music), Audio1, MakeClip(music, 0, 5));
        p = UpdateTrack(p, Audio1, t => t with { IsMuted = true });

        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, T(1), T(2)));
    }

    [Fact]
    public void AudioIn_skips_zero_volume_clips_and_reports_volume()
    {
        var music = AudioAsset();
        var p = Place(NewProject(null, music), Audio1, MakeClip(music, 0, 5, volume: 0));
        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, T(1), T(2)));

        p = Place(p, Audio1, MakeClip(music, 0, 5, volume: 0.5));
        var segment = Assert.Single(TimelineEvaluator.AudioIn(p, p.MainSequence, T(1), T(2)));
        Assert.Equal(0.5, segment.Volume);
    }

    [Fact]
    public void AudioIn_ignores_clips_outside_window_and_empty_windows()
    {
        var music = AudioAsset();
        var p = Place(NewProject(null, music), Audio1, MakeClip(music, 0, 1));

        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, T(2), T(3)));
        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, T(0.5), T(0.5)));
        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, T(0.5), T(0.25)));
    }

    [Fact]
    public void AudioIn_skips_video_without_audio_and_assets_missing_from_project()
    {
        var silentVideo = VideoAsset("sem_som.mp4", withAudio: false);
        var missing = AudioAsset("fora_do_projeto.wav");
        var p = NewProject(null, silentVideo);
        p = Place(p, Audio1, MakeClip(silentVideo, 0, 5), MakeClip(missing, 0, 5));

        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, T(1), T(2)));
    }

    // ---------------------------------------------------------------- AssetsInUse

    [Fact]
    public void AssetsInUse_lists_ids_referenced_by_clips_only()
    {
        var a = VideoAsset("a.mp4");
        var b = AudioAsset("b.wav");
        var p = NewProject(null, a, b);
        p = Place(p, Video1, MakeClip(a, 0, 1), MakeClip(null, 2, 1));
        p = Place(p, Audio1, MakeClip(b, 0, 1), MakeClip(a, 3, 1));

        var inUse = TimelineEvaluator.AssetsInUse(p.MainSequence);

        Assert.Equal(2, inUse.Count);
        Assert.Contains(a.Id, inUse);
        Assert.Contains(b.Id, inUse);
    }
}
