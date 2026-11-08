using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public sealed class TimelineCommandsTests
{
    // Índices das trilhas criadas por Project.CreateNew: Vídeo 1, Vídeo 2, Áudio 1, Texto 1.
    private const int Video1 = 0;
    private const int Video2 = 1;
    private const int Audio1 = 2;

    private static readonly FrameRate Fr = FrameRate.Fps30;

    private static MediaTime S(long seconds) => MediaTime.FromSeconds(seconds);

    private static MediaAsset Asset(string name, MediaKind kind, long seconds, bool withAudio = false) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Name = name,
        OriginalPath = @"C:\m\" + name,
        Duration = S(seconds),
        Info = withAudio ? new MediaTechnicalInfo { AudioCodec = "aac" } : null,
    };

    private static Project NewProject(params MediaAsset[] media) =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [.. media] };

    private static Clip NewClip(MediaAsset? asset, MediaTime start, MediaTime duration, MediaTime sourceIn = default,
        Guid? link = null, Guid? group = null) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = asset?.Id,
        Start = start,
        Duration = duration,
        SourceIn = sourceIn,
        LinkId = link,
        GroupId = group,
    };

    private static Project WithTrack(Project project, int index, Func<Track, Track> change)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(index, change(seq.Tracks[index])) });
    }

    private static Project PlaceClips(Project project, int index, params Clip[] clips) =>
        WithTrack(project, index, t => t with { Clips = [.. clips] });

    private static Track TrackOf(Project project, int index) => project.MainSequence.Tracks[index];

    private static Clip ClipOf(Project project, int index, Guid clipId) =>
        TrackOf(project, index).Clips.Single(c => c.Id == clipId);

    private static void AssertNoOverlap(Track track)
    {
        var ordered = track.Clips.OrderBy(c => c.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i].Start >= ordered[i - 1].End, $"Clipes sobrepostos na trilha \"{track.Name}\".");
    }

    // ---------------------------------------------------------------- AddMediaToTimelineCommand

    [Fact]
    public void AddMedia_video_with_audio_creates_linked_clips_on_both_tracks()
    {
        var video = Asset("v.mp4", MediaKind.Video, 10, withAudio: true);
        var p = NewProject(video);
        var seq = p.MainSequence;
        var cmd = new AddMediaToTimelineCommand(seq.Id, video.Id, seq.Tracks[Video1].Id, seq.Tracks[Audio1].Id,
            MediaTime.Zero, S(5));

        var result = cmd.Apply(p);

        var v = Assert.Single(TrackOf(result, Video1).Clips);
        var a = Assert.Single(TrackOf(result, Audio1).Clips);
        Assert.Equal(cmd.VideoClipId, v.Id);
        Assert.Equal(cmd.AudioClipId, a.Id);
        Assert.NotNull(v.LinkId);
        Assert.Equal(v.LinkId, a.LinkId);
        Assert.Equal(S(10), v.Duration);
        Assert.Equal(S(10), a.Duration);
        Assert.Equal(video.Id, v.MediaAssetId);
        Assert.Equal(video.Id, a.MediaAssetId);
    }

    [Fact]
    public void AddMedia_image_uses_image_duration_snapped_down_to_frames()
    {
        var image = Asset("foto.png", MediaKind.Image, 0);
        var p = NewProject(image);
        var seq = p.MainSequence;
        // 75 quadros + meio quadro: deve arredondar para 75 quadros (2,5 s).
        var imageDuration = new MediaTime(Fr.FrameToTime(75).Ticks + Fr.FrameDuration.Ticks / 2);
        var cmd = new AddMediaToTimelineCommand(seq.Id, image.Id, seq.Tracks[Video1].Id, null, MediaTime.Zero, imageDuration);

        var clip = Assert.Single(TrackOf(cmd.Apply(p), Video1).Clips);

        Assert.Equal(Fr.FrameToTime(75), clip.Duration);
        Assert.Null(clip.LinkId);
    }

    [Fact]
    public void AddMedia_image_with_zero_image_duration_gets_one_frame()
    {
        var image = Asset("foto.png", MediaKind.Image, 0);
        var p = NewProject(image);
        var seq = p.MainSequence;
        var cmd = new AddMediaToTimelineCommand(seq.Id, image.Id, seq.Tracks[Video1].Id, null, MediaTime.Zero, MediaTime.Zero);

        var clip = Assert.Single(TrackOf(cmd.Apply(p), Video1).Clips);

        Assert.Equal(Fr.FrameDuration, clip.Duration);
    }

    [Fact]
    public void AddMedia_audio_only_asset_goes_to_audio_track_without_link()
    {
        var music = Asset("musica.wav", MediaKind.Audio, 3);
        var p = NewProject(music);
        var seq = p.MainSequence;
        var cmd = new AddMediaToTimelineCommand(seq.Id, music.Id, null, seq.Tracks[Audio1].Id, MediaTime.Zero, MediaTime.Zero);

        var result = cmd.Apply(p);

        var clip = Assert.Single(TrackOf(result, Audio1).Clips);
        Assert.Equal(S(3), clip.Duration);
        Assert.Null(clip.LinkId);
        Assert.Empty(TrackOf(result, Video1).Clips);
    }

    [Fact]
    public void AddMedia_audio_only_asset_with_only_video_track_throws()
    {
        var music = Asset("musica.wav", MediaKind.Audio, 3);
        var p = NewProject(music);
        var seq = p.MainSequence;
        var cmd = new AddMediaToTimelineCommand(seq.Id, music.Id, seq.Tracks[Video1].Id, null, MediaTime.Zero, MediaTime.Zero);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void AddMedia_without_compatible_track_throws()
    {
        var video = Asset("v.mp4", MediaKind.Video, 10, withAudio: true);
        var p = NewProject(video);
        var cmd = new AddMediaToTimelineCommand(p.MainSequence.Id, video.Id, null, null, MediaTime.Zero, MediaTime.Zero);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void AddMedia_to_locked_track_throws()
    {
        var video = Asset("v.mp4", MediaKind.Video, 10, withAudio: true);
        var p = WithTrack(NewProject(video), Video1, t => t with { IsLocked = true });
        var seq = p.MainSequence;
        var cmd = new AddMediaToTimelineCommand(seq.Id, video.Id, seq.Tracks[Video1].Id, seq.Tracks[Audio1].Id,
            MediaTime.Zero, MediaTime.Zero);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void AddMedia_over_existing_clip_overwrites_it_without_overlap()
    {
        var existingAsset = Asset("a.mp4", MediaKind.Video, 20);
        var video = Asset("b.mp4", MediaKind.Video, 10);
        var existing = NewClip(existingAsset, S(0), S(20));
        var p = PlaceClips(NewProject(existingAsset, video), Video1, existing);
        var seq = p.MainSequence;
        var cmd = new AddMediaToTimelineCommand(seq.Id, video.Id, seq.Tracks[Video1].Id, null, S(5), MediaTime.Zero);

        var track = TrackOf(cmd.Apply(p), Video1);

        AssertNoOverlap(track);
        Assert.Equal(3, track.Clips.Count);
        // Esquerda: o clipe original encurtado, mantendo o Id.
        var left = track.Clips.Single(c => c.Id == existing.Id);
        Assert.Equal(S(0), left.Start);
        Assert.Equal(S(5), left.Duration);
        // Direita: o restante do clipe original, com origem avançada.
        var right = track.Clips.Single(c => c.Start == S(15));
        Assert.Equal(S(5), right.Duration);
        Assert.Equal(S(15), right.SourceIn);
        // O clipe novo ocupa [5 s, 15 s).
        var added = track.Clips.Single(c => c.Id == cmd.VideoClipId);
        Assert.Equal(S(5), added.Start);
        Assert.Equal(S(10), added.Duration);
    }

    // ---------------------------------------------------------------- MoveClipsCommand

    [Fact]
    public void MoveClip_snaps_start_to_nearest_frame()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(5));
        var p = PlaceClips(NewProject(v), Video1, clip);
        var seq = p.MainSequence;
        // 2 s + 0,7 quadro: mais próximo do quadro 61.
        var target = new MediaTime(Fr.FrameToTime(60).Ticks + Fr.FrameDuration.Ticks * 7 / 10);

        var result = new MoveClipsCommand(seq.Id, [new ClipMove(clip.Id, seq.Tracks[Video1].Id, target)]).Apply(p);

        Assert.Equal(Fr.FrameToTime(61), ClipOf(result, Video1, clip.Id).Start);
    }

    [Fact]
    public void MoveClip_to_another_compatible_video_track()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(5));
        var p = PlaceClips(NewProject(v), Video1, clip);
        var seq = p.MainSequence;

        var result = new MoveClipsCommand(seq.Id, [new ClipMove(clip.Id, seq.Tracks[Video2].Id, S(1))]).Apply(p);

        Assert.Empty(TrackOf(result, Video1).Clips);
        var moved = Assert.Single(TrackOf(result, Video2).Clips);
        Assert.Equal(clip.Id, moved.Id);
        Assert.Equal(S(1), moved.Start);
    }

    [Fact]
    public void MoveVideoClip_to_audio_track_throws()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10, withAudio: true);
        var clip = NewClip(v, S(0), S(5));
        var p = PlaceClips(NewProject(v), Video1, clip);
        var seq = p.MainSequence;

        var cmd = new MoveClipsCommand(seq.Id, [new ClipMove(clip.Id, seq.Tracks[Audio1].Id, S(1))]);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void MoveClip_to_negative_start_throws()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(5));
        var p = PlaceClips(NewProject(v), Video1, clip);
        var seq = p.MainSequence;

        var cmd = new MoveClipsCommand(seq.Id, [new ClipMove(clip.Id, seq.Tracks[Video1].Id, -S(1))]);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void MoveClips_swap_positions_of_two_adjacent_clips()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var a = NewClip(v, S(0), S(5));
        var b = NewClip(v, S(5), S(5));
        var p = PlaceClips(NewProject(v), Video1, a, b);
        var seq = p.MainSequence;
        var trackId = seq.Tracks[Video1].Id;

        var result = new MoveClipsCommand(seq.Id, [new ClipMove(a.Id, trackId, S(5)), new ClipMove(b.Id, trackId, S(0))])
            .Apply(p);

        var track = TrackOf(result, Video1);
        AssertNoOverlap(track);
        Assert.Equal(S(5), track.Clips.Single(c => c.Id == a.Id).Start);
        Assert.Equal(S(0), track.Clips.Single(c => c.Id == b.Id).Start);
    }

    [Fact]
    public void MoveClip_to_same_position_returns_same_project()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(5));
        var p = PlaceClips(NewProject(v), Video1, clip);
        var seq = p.MainSequence;

        var result = new MoveClipsCommand(seq.Id, [new ClipMove(clip.Id, seq.Tracks[Video1].Id, S(0))]).Apply(p);

        Assert.Same(p, result);
    }

    [Fact]
    public void MoveClip_to_locked_track_throws()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(5));
        var p = WithTrack(PlaceClips(NewProject(v), Video1, clip), Video2, t => t with { IsLocked = true });
        var seq = p.MainSequence;

        var cmd = new MoveClipsCommand(seq.Id, [new ClipMove(clip.Id, seq.Tracks[Video2].Id, S(1))]);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    // ---------------------------------------------------------------- TrimClipCommand

    [Fact]
    public void TrimEnd_shortens_clip()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(10));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var result = new TrimClipCommand(p.MainSequence.Id, clip.Id, ClipEdge.End, S(6)).Apply(p);

        var trimmed = ClipOf(result, Video1, clip.Id);
        Assert.Equal(S(0), trimmed.Start);
        Assert.Equal(S(6), trimmed.Duration);
    }

    [Fact]
    public void TrimEnd_cannot_extend_beyond_source_duration()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(4));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var result = new TrimClipCommand(p.MainSequence.Id, clip.Id, ClipEdge.End, S(15)).Apply(p);

        Assert.Equal(S(10), ClipOf(result, Video1, clip.Id).Duration);
    }

    [Fact]
    public void TrimEnd_cannot_shrink_below_one_frame()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(10));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var result = new TrimClipCommand(p.MainSequence.Id, clip.Id, ClipEdge.End, S(0)).Apply(p);

        Assert.Equal(Fr.FrameDuration, ClipOf(result, Video1, clip.Id).Duration);
    }

    [Fact]
    public void TrimEnd_without_ripple_is_clamped_at_next_clip_start()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var a = NewClip(v, S(0), S(4));
        var b = NewClip(v, S(6), S(4));
        var p = PlaceClips(NewProject(v), Video1, a, b);

        var result = new TrimClipCommand(p.MainSequence.Id, a.Id, ClipEdge.End, S(9)).Apply(p);

        Assert.Equal(S(6), ClipOf(result, Video1, a.Id).End);
        Assert.Equal(S(6), ClipOf(result, Video1, b.Id).Start);
        Assert.Equal(S(4), ClipOf(result, Video1, b.Id).Duration);
    }

    [Fact]
    public void TrimEnd_with_ripple_shifts_following_clips()
    {
        var v = Asset("v.mp4", MediaKind.Video, 20);
        var a = NewClip(v, S(0), S(4));
        var b = NewClip(v, S(6), S(4));
        var p = PlaceClips(NewProject(v), Video1, a, b);

        var result = new TrimClipCommand(p.MainSequence.Id, a.Id, ClipEdge.End, S(9), Ripple: true).Apply(p);

        Assert.Equal(S(9), ClipOf(result, Video1, a.Id).End);
        Assert.Equal(S(11), ClipOf(result, Video1, b.Id).Start);
    }

    [Fact]
    public void TrimStart_forward_advances_source_in_and_keeps_end()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(10));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var result = new TrimClipCommand(p.MainSequence.Id, clip.Id, ClipEdge.Start, S(2)).Apply(p);

        var trimmed = ClipOf(result, Video1, clip.Id);
        Assert.Equal(S(2), trimmed.Start);
        Assert.Equal(S(10), trimmed.End);
        Assert.Equal(S(8), trimmed.Duration);
        Assert.Equal(S(2), trimmed.SourceIn);
    }

    [Fact]
    public void TrimStart_cannot_go_before_source_in_zero()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        // Começa em 5 s, com origem em 2 s: no máximo 2 s de conteúdo anterior disponível.
        var clip = NewClip(v, S(5), S(5), sourceIn: S(2));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var result = new TrimClipCommand(p.MainSequence.Id, clip.Id, ClipEdge.Start, S(0)).Apply(p);

        var trimmed = ClipOf(result, Video1, clip.Id);
        Assert.Equal(S(3), trimmed.Start);
        Assert.Equal(S(0), trimmed.SourceIn);
        Assert.Equal(S(7), trimmed.Duration);
    }

    [Fact]
    public void Trim_affects_linked_partner_with_same_edge()
    {
        var link = Guid.NewGuid();
        var av = Asset("av.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(av, S(0), S(10), link: link);
        var audio = NewClip(av, S(0), S(10), link: link);
        var p = PlaceClips(PlaceClips(NewProject(av), Video1, video), Audio1, audio);

        var result = new TrimClipCommand(p.MainSequence.Id, video.Id, ClipEdge.End, S(6)).Apply(p);

        Assert.Equal(S(6), ClipOf(result, Video1, video.Id).Duration);
        Assert.Equal(S(6), ClipOf(result, Audio1, audio.Id).Duration);
    }

    // ---------------------------------------------------------------- SplitClipsCommand

    [Fact]
    public void Split_divides_clip_at_playhead_with_correct_source_in()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(10));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var result = new SplitClipsCommand(p.MainSequence.Id, S(4)).Apply(p);

        var clips = TrackOf(result, Video1).Clips;
        Assert.Equal(2, clips.Count);
        var left = clips[0];
        var right = clips[1];
        Assert.Equal(clip.Id, left.Id);
        Assert.Equal(S(0), left.Start);
        Assert.Equal(S(4), left.End);
        Assert.Equal(S(4), right.Start);
        Assert.Equal(S(10), right.End);
        Assert.Equal(left.SourceIn + S(4), right.SourceIn);
        Assert.NotEqual(clip.Id, right.Id);
    }

    [Fact]
    public void Split_linked_clips_gives_right_pieces_a_new_shared_link()
    {
        var link = Guid.NewGuid();
        var av = Asset("av.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(av, S(0), S(10), link: link);
        var audio = NewClip(av, S(0), S(10), link: link);
        var p = PlaceClips(PlaceClips(NewProject(av), Video1, video), Audio1, audio);

        var result = new SplitClipsCommand(p.MainSequence.Id, S(4)).Apply(p);

        var videoClips = TrackOf(result, Video1).Clips;
        var audioClips = TrackOf(result, Audio1).Clips;
        Assert.Equal<Guid?>(link, videoClips[0].LinkId);
        Assert.Equal<Guid?>(link, audioClips[0].LinkId);
        Assert.NotNull(videoClips[1].LinkId);
        Assert.NotEqual<Guid?>(link, videoClips[1].LinkId);
        Assert.Equal(videoClips[1].LinkId, audioClips[1].LinkId);
    }

    [Fact]
    public void Split_with_nothing_under_playhead_throws()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var p = PlaceClips(NewProject(v), Video1, NewClip(v, S(0), S(10)));

        var cmd = new SplitClipsCommand(p.MainSequence.Id, S(15));

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    // ---------------------------------------------------------------- RemoveClipsCommand

    [Fact]
    public void Remove_also_removes_linked_partner()
    {
        var link = Guid.NewGuid();
        var av = Asset("av.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(av, S(0), S(10), link: link);
        var audio = NewClip(av, S(0), S(10), link: link);
        var p = PlaceClips(PlaceClips(NewProject(av), Video1, video), Audio1, audio);

        var result = new RemoveClipsCommand(p.MainSequence.Id, [video.Id]).Apply(p);

        Assert.Empty(TrackOf(result, Video1).Clips);
        Assert.Empty(TrackOf(result, Audio1).Clips);
    }

    [Fact]
    public void Remove_with_ripple_shifts_later_clips_left()
    {
        var v = Asset("v.mp4", MediaKind.Video, 20);
        var a = NewClip(v, S(0), S(4));
        var b = NewClip(v, S(4), S(4));
        var c = NewClip(v, S(8), S(4));
        var p = PlaceClips(NewProject(v), Video1, a, b, c);

        var result = new RemoveClipsCommand(p.MainSequence.Id, [b.Id], Ripple: true).Apply(p);

        Assert.Equal(2, TrackOf(result, Video1).Clips.Count);
        Assert.Equal(S(0), ClipOf(result, Video1, a.Id).Start);
        Assert.Equal(S(4), ClipOf(result, Video1, c.Id).Start);
    }

    // ---------------------------------------------------------------- PasteClipsCommand

    [Fact]
    public void Paste_creates_new_ids_and_keeps_relative_offsets_at_target()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var a = NewClip(v, S(2), S(2));
        var b = NewClip(v, S(5), S(1));
        var p = PlaceClips(NewProject(v), Video1, a, b);
        var trackId = TrackOf(p, Video1).Id;
        var items = new[] { new ClipboardItem(trackId, a), new ClipboardItem(trackId, b) };

        var cmd = PasteClipsCommand.Create(p.MainSequence.Id, items, S(10));
        var result = cmd.Apply(p);

        Assert.DoesNotContain(a.Id, cmd.NewClipIds);
        Assert.DoesNotContain(b.Id, cmd.NewClipIds);
        var pasted = TrackOf(result, Video1).Clips.Where(c => cmd.NewClipIds.Contains(c.Id)).OrderBy(c => c.Start).ToList();
        Assert.Equal(2, pasted.Count);
        Assert.Equal(S(10), pasted[0].Start);
        Assert.Equal(S(2), pasted[0].Duration);
        Assert.Equal(S(13), pasted[1].Start);
        Assert.Equal(S(1), pasted[1].Duration);
    }

    [Fact]
    public void Duplicate_places_copy_right_after_selection_end()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var a = NewClip(v, S(2), S(2));
        var b = NewClip(v, S(5), S(1));
        var p = PlaceClips(NewProject(v), Video1, a, b);

        var cmd = PasteClipsCommand.Duplicate(p.MainSequence, [a.Id, b.Id]);
        var result = cmd.Apply(p);

        // A seleção termina em 6 s; a cópia começa ali mantendo a distância entre os clipes.
        var pasted = TrackOf(result, Video1).Clips.Where(c => cmd.NewClipIds.Contains(c.Id)).OrderBy(c => c.Start).ToList();
        Assert.Equal(S(6), pasted[0].Start);
        Assert.Equal(S(9), pasted[1].Start);
    }

    [Fact]
    public void Paste_gives_new_links_and_groups_shared_between_copies_only()
    {
        var link = Guid.NewGuid();
        var group = Guid.NewGuid();
        var av = Asset("av.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(av, S(0), S(2), link: link, group: group);
        var audio = NewClip(av, S(0), S(2), link: link, group: group);
        var p = PlaceClips(PlaceClips(NewProject(av), Video1, video), Audio1, audio);
        var items = new[]
        {
            new ClipboardItem(TrackOf(p, Video1).Id, video),
            new ClipboardItem(TrackOf(p, Audio1).Id, audio),
        };

        var result = PasteClipsCommand.Create(p.MainSequence.Id, items, S(10)).Apply(p);

        var newVideo = TrackOf(result, Video1).Clips.Single(c => c.Start == S(10));
        var newAudio = TrackOf(result, Audio1).Clips.Single(c => c.Start == S(10));
        Assert.NotNull(newVideo.LinkId);
        Assert.Equal(newVideo.LinkId, newAudio.LinkId);
        Assert.NotEqual<Guid?>(link, newVideo.LinkId);
        Assert.NotNull(newVideo.GroupId);
        Assert.Equal(newVideo.GroupId, newAudio.GroupId);
        Assert.NotEqual<Guid?>(group, newVideo.GroupId);
    }

    // ---------------------------------------------------------------- SetClipSpeedCommand

    [Fact]
    public void SetSpeed_double_halves_duration_and_applies_to_linked_partner()
    {
        var link = Guid.NewGuid();
        var av = Asset("av.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(av, S(0), S(10), link: link);
        var audio = NewClip(av, S(0), S(10), link: link);
        var p = PlaceClips(PlaceClips(NewProject(av), Video1, video), Audio1, audio);

        var result = new SetClipSpeedCommand(p.MainSequence.Id, video.Id, new Rational(2, 1)).Apply(p);

        Assert.Equal(S(5), ClipOf(result, Video1, video.Id).Duration);
        Assert.Equal(S(5), ClipOf(result, Audio1, audio.Id).Duration);
        Assert.Equal(new Rational(2, 1), ClipOf(result, Video1, video.Id).Speed);
    }

    [Fact]
    public void SetSpeed_half_doubles_duration_and_shifts_following_clip()
    {
        var v = Asset("v.mp4", MediaKind.Video, 20);
        var first = NewClip(v, S(0), S(10));
        var next = NewClip(v, S(10), S(2));
        var p = PlaceClips(NewProject(v), Video1, first, next);

        var result = new SetClipSpeedCommand(p.MainSequence.Id, first.Id, new Rational(1, 2)).Apply(p);

        Assert.Equal(S(20), ClipOf(result, Video1, first.Id).Duration);
        Assert.Equal(S(20), ClipOf(result, Video1, next.Id).Start);
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(1, 20)]
    public void SetSpeed_outside_allowed_range_throws(long numerator, long denominator)
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var clip = NewClip(v, S(0), S(10));
        var p = PlaceClips(NewProject(v), Video1, clip);

        var cmd = new SetClipSpeedCommand(p.MainSequence.Id, clip.Id, new Rational(numerator, denominator));

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    // ---------------------------------------------------------------- GroupClipsCommand / LinkClipsCommand

    [Fact]
    public void Group_and_ungroup_set_and_clear_group_id()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var a = NewClip(v, S(0), S(2));
        var b = NewClip(v, S(5), S(2));
        var c = NewClip(v, S(0), S(2));
        var p = PlaceClips(PlaceClips(NewProject(v), Video1, a, b), Audio1, c);

        var grouped = new GroupClipsCommand(p.MainSequence.Id, [a.Id, b.Id], Group: true).Apply(p);

        var groupA = ClipOf(grouped, Video1, a.Id).GroupId;
        Assert.NotNull(groupA);
        Assert.Equal(groupA, ClipOf(grouped, Video1, b.Id).GroupId);
        Assert.Null(ClipOf(grouped, Audio1, c.Id).GroupId);

        var ungrouped = new GroupClipsCommand(p.MainSequence.Id, [a.Id], Group: false).Apply(grouped);

        Assert.Null(ClipOf(ungrouped, Video1, a.Id).GroupId);
        Assert.Null(ClipOf(ungrouped, Video1, b.Id).GroupId);
    }

    [Fact]
    public void Group_with_fewer_than_two_clips_throws()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10);
        var a = NewClip(v, S(0), S(2));
        var p = PlaceClips(NewProject(v), Video1, a);

        var cmd = new GroupClipsCommand(p.MainSequence.Id, [a.Id], Group: true);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void Link_gives_same_link_id_to_selected_clips()
    {
        var v = Asset("v.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(v, S(0), S(2));
        var audio = NewClip(v, S(0), S(2));
        var p = PlaceClips(PlaceClips(NewProject(v), Video1, video), Audio1, audio);

        var result = new LinkClipsCommand(p.MainSequence.Id, [video.Id, audio.Id], Link: true).Apply(p);

        var linkVideo = ClipOf(result, Video1, video.Id).LinkId;
        Assert.NotNull(linkVideo);
        Assert.Equal(linkVideo, ClipOf(result, Audio1, audio.Id).LinkId);
    }

    [Fact]
    public void Unlink_clears_link_on_both_partners()
    {
        var link = Guid.NewGuid();
        var av = Asset("av.mp4", MediaKind.Video, 10, withAudio: true);
        var video = NewClip(av, S(0), S(10), link: link);
        var audio = NewClip(av, S(0), S(10), link: link);
        var p = PlaceClips(PlaceClips(NewProject(av), Video1, video), Audio1, audio);

        var result = new LinkClipsCommand(p.MainSequence.Id, [video.Id], Link: false).Apply(p);

        Assert.Null(ClipOf(result, Video1, video.Id).LinkId);
        Assert.Null(ClipOf(result, Audio1, audio.Id).LinkId);
    }

    // ---------------------------------------------------------------- UpdateMarkerCommand

    [Fact]
    public void UpdateMarker_moves_snapped_and_keeps_markers_sorted()
    {
        var m1 = new Marker { Id = Guid.NewGuid(), Time = S(2), Name = "A" };
        var m2 = new Marker { Id = Guid.NewGuid(), Time = S(5), Name = "B" };
        var p = NewProject();
        p = p.WithSequence(p.MainSequence with { Markers = [m1, m2] });
        // 6 s + 0,7 quadro: deve ir para o quadro 181, depois de B (5 s).
        var target = new MediaTime(Fr.FrameToTime(180).Ticks + Fr.FrameDuration.Ticks * 7 / 10);

        var result = new UpdateMarkerCommand(p.MainSequence.Id, m1.Id, Time: target).Apply(p);

        var markers = result.MainSequence.Markers;
        Assert.Equal(m2.Id, markers[0].Id);
        Assert.Equal(m1.Id, markers[1].Id);
        Assert.Equal(Fr.FrameToTime(181), markers[1].Time);
    }

    [Fact]
    public void UpdateMarker_rename_trims_name_and_keeps_time()
    {
        var m1 = new Marker { Id = Guid.NewGuid(), Time = S(2), Name = "A" };
        var p = NewProject();
        p = p.WithSequence(p.MainSequence with { Markers = [m1] });

        var result = new UpdateMarkerCommand(p.MainSequence.Id, m1.Id, Name: "  Abertura  ").Apply(p);

        var marker = Assert.Single(result.MainSequence.Markers);
        Assert.Equal("Abertura", marker.Name);
        Assert.Equal(S(2), marker.Time);
    }
}
