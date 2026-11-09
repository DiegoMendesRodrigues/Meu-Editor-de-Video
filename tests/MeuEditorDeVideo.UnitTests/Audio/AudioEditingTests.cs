using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class AudioEditingTests
{
    private const int Video1 = 0;
    private const int Video2 = 1;
    private const int Audio1 = 2;

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset AssetWithAudio() => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = "v.mp4",
        OriginalPath = @"C:\m\v.mp4",
        Duration = S(60),
        Info = new MediaTechnicalInfo { AudioCodec = "aac" },
    };

    private static Project NewProject(MediaAsset media) =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [media] };

    private static Clip NewClip(MediaAsset media, double start, double duration, Guid? link = null) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = media.Id,
        Start = S(start),
        Duration = S(duration),
        LinkId = link,
    };

    private static Project WithClips(Project p, int trackIndex, params Clip[] clips)
    {
        var seq = p.MainSequence;
        return p.WithSequence(seq with { Tracks = seq.Tracks.SetItem(trackIndex, seq.Tracks[trackIndex] with { Clips = [.. clips] }) });
    }

    private static Project EditTrack(Project p, int trackIndex, Func<Track, Track> change)
    {
        var seq = p.MainSequence;
        return p.WithSequence(seq with { Tracks = seq.Tracks.SetItem(trackIndex, change(seq.Tracks[trackIndex])) });
    }

    /// <summary>Cenário: vídeo 1 com clipe vinculado ao áudio 1 (0–10 s) e um clipe depois (10–15 s); vídeo 2 com um clipe em 20 s.</summary>
    private sealed record Scene(Project Project, Clip Video, Clip Audio, Clip LaterVideo, Clip LaterAudio, Clip Other);

    private static Scene Build()
    {
        var media = AssetWithAudio();
        var link = Guid.NewGuid();
        var v = NewClip(media, 0, 10, link);
        var a = NewClip(media, 0, 10, link);
        var v2 = NewClip(media, 10, 5);
        var a2 = NewClip(media, 10, 5);
        var x = NewClip(media, 20, 2);
        var p = NewProject(media);
        p = WithClips(p, Video1, v, v2);
        p = WithClips(p, Audio1, a, a2);
        p = WithClips(p, Video2, x);
        return new Scene(p, v, a, v2, a2, x);
    }

    private static Guid SeqId(Project p) => p.MainSequence.Id;

    // ---------------------------------------------------------------- RemoveTimeRangesCommand

    [Fact]
    public void Removing_a_middle_range_splits_the_clip_and_its_link_and_ripples_the_track()
    {
        var s = Build();
        var cmd = new RemoveTimeRangesCommand(SeqId(s.Project), s.Video.Id, [new TimeRange(S(3), S(5))]);

        var result = cmd.Apply(s.Project);

        var video = result.MainSequence.Tracks[Video1].Clips;
        Assert.Equal(3, video.Count);
        Assert.Equal(s.Video.Id, video[0].Id); // o pedaço da esquerda mantém a identidade
        Assert.Equal(S(0), video[0].Start);
        Assert.Equal(S(3), video[0].Duration);
        Assert.NotEqual(s.Video.Id, video[1].Id);
        Assert.Equal(S(3), video[1].Start);
        Assert.Equal(S(5), video[1].Duration);
        Assert.Equal(S(5), video[1].SourceIn);
        Assert.Equal(MediaTime.Zero, video[1].FadeIn);
        Assert.Equal(s.LaterVideo.Id, video[2].Id);
        Assert.Equal(S(8), video[2].Start); // deslocado 2 s para trás (ripple)
        Assert.Equal(S(13), video[2].End);

        var audio = result.MainSequence.Tracks[Audio1].Clips;
        Assert.Equal(3, audio.Count);
        Assert.Equal(s.Audio.LinkId, audio[0].LinkId);
        Assert.NotNull(video[1].LinkId);
        Assert.Equal(video[1].LinkId, audio[1].LinkId); // pedaços da direita compartilham um novo vínculo
        Assert.NotEqual(s.Audio.LinkId, video[1].LinkId);
        Assert.Equal(S(8), audio[2].Start);

        // Trilha de vídeo 2 não está vinculada ao clipe: não sofre ripple.
        Assert.Equal(S(20), result.MainSequence.Tracks[Video2].Clips.Single().Start);
    }

    [Fact]
    public void Several_ranges_are_removed_in_one_pass()
    {
        var s = Build();
        var cmd = new RemoveTimeRangesCommand(SeqId(s.Project), s.Video.Id,
            [new TimeRange(S(4), S(5)), new TimeRange(S(1), S(2))]);

        var video = cmd.Apply(s.Project).MainSequence.Tracks[Video1].Clips;

        // [0,1) + [1,3) (era 2–4) + [3,8) (era 5–10) + clipe seguinte deslocado em 2 s
        Assert.Equal(4, video.Count);
        Assert.Equal(new[] { S(0), S(1), S(3), S(8) }, video.Select(c => c.Start));
        Assert.Equal(new[] { S(1), S(2), S(5), S(5) }, video.Select(c => c.Duration));
    }

    [Fact]
    public void Overlapping_ranges_are_merged_before_removal()
    {
        var s = Build();
        var cmd = new RemoveTimeRangesCommand(SeqId(s.Project), s.Video.Id,
            [new TimeRange(S(1), S(3)), new TimeRange(S(2), S(4))]);

        var video = cmd.Apply(s.Project).MainSequence.Tracks[Video1].Clips;

        // Mescladas: [1,4) removido → restam [0,1) e [1,7); o clipe seguinte começa em 7.
        Assert.Equal(3, video.Count);
        Assert.Equal(S(1), video[1].Start);
        Assert.Equal(S(6), video[1].Duration);
        Assert.Equal(S(7), video[2].Start);
    }

    [Fact]
    public void Ranges_outside_the_clip_are_clipped_or_discarded()
    {
        var s = Build();
        // [-2,1) vira [0,1); [12,14) está fora do clipe e é descartado.
        var cmd = new RemoveTimeRangesCommand(SeqId(s.Project), s.Video.Id,
            [new TimeRange(S(-2), S(1)), new TimeRange(S(12), S(14))]);

        var video = cmd.Apply(s.Project).MainSequence.Tracks[Video1].Clips;

        Assert.Equal(s.Video.Id, video[0].Id); // sem pedaço à esquerda, o clipe continua com o mesmo Id
        Assert.Equal(S(0), video[0].Start);
        Assert.Equal(S(9), video[0].Duration);
        Assert.Equal(S(9), video[1].Start);
    }

    [Fact]
    public void Empty_or_out_of_clip_ranges_are_rejected()
    {
        var s = Build();
        var id = s.Video.Id;

        Assert.Throws<EditRejectedException>(() => new RemoveTimeRangesCommand(SeqId(s.Project), id, [new TimeRange(S(3), S(3))]).Apply(s.Project));
        Assert.Throws<EditRejectedException>(() => new RemoveTimeRangesCommand(SeqId(s.Project), id, [new TimeRange(S(12), S(14))]).Apply(s.Project));
        Assert.Throws<EditRejectedException>(() => new RemoveTimeRangesCommand(SeqId(s.Project), id, []).Apply(s.Project));
    }

    [Fact]
    public void Ranges_covering_the_whole_clip_are_rejected()
    {
        var s = Build();
        var id = s.Video.Id;

        Assert.Throws<EditRejectedException>(() => new RemoveTimeRangesCommand(SeqId(s.Project), id, [new TimeRange(S(0), S(10))]).Apply(s.Project));
        // Dois trechos que juntos cobrem tudo (após a mescla, o mesmo caso).
        Assert.Throws<EditRejectedException>(() => new RemoveTimeRangesCommand(SeqId(s.Project), id,
            [new TimeRange(S(0), S(5)), new TimeRange(S(5), S(10))]).Apply(s.Project));
    }

    // ---------------------------------------------------------------- UpdateTrackCommand

    [Theory]
    [InlineData(5.0, 4.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.5, 0.5)]
    public void Set_volume_clamps_the_track_volume_between_zero_and_four(double value, double expected)
    {
        var s = Build();
        var track = s.Project.MainSequence.Tracks[Audio1].Id;

        var result = UpdateTrackCommand.SetVolume(SeqId(s.Project), track, value).Apply(s.Project);

        Assert.Equal(expected, result.MainSequence.Tracks[Audio1].Volume, 9);
    }

    [Fact]
    public void Set_solo_flags_the_track_and_only_continuous_adjustments_share_merge_keys()
    {
        var s = Build();
        var seq = SeqId(s.Project);
        var track = s.Project.MainSequence.Tracks[Audio1].Id;

        var solo = UpdateTrackCommand.SetSolo(seq, track, true).Apply(s.Project);

        Assert.True(solo.MainSequence.Tracks[Audio1].IsSolo);
        Assert.Equal($"track-volume:{track}", UpdateTrackCommand.SetVolume(seq, track, 1).MergeKey);
        Assert.Equal($"track-pan:{track}", UpdateTrackCommand.SetPan(seq, track, 0).MergeKey);
        Assert.Null(UpdateTrackCommand.SetSolo(seq, track, true).MergeKey);
    }

    // ---------------------------------------------------------------- EffectCatalog

    [Fact]
    public void Active_visual_and_audio_effect_lists_do_not_overlap()
    {
        var clip = new Clip
        {
            Id = Guid.NewGuid(),
            Start = MediaTime.Zero,
            Duration = S(4),
            Effects =
            [
                EffectCatalog.Create(EffectCatalog.Blur),       // visual, ativo
                EffectCatalog.Create(EffectCatalog.Echo),       // áudio, ativo
                EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Echo), "mix", 0), // áudio, neutro
                EffectCatalog.Create(EffectCatalog.Compressor), // áudio, ativo
                EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Blur), "radius", 0), // visual, neutro
                EffectCatalog.Create(EffectCatalog.Equalizer) with { Enabled = false }, // áudio, desligado
            ],
        };

        Assert.Equal(new[] { EffectCatalog.Blur }, EffectCatalog.ActiveEffects(clip).Select(e => e.Type));
        Assert.Equal(new[] { EffectCatalog.Echo, EffectCatalog.Compressor },
            EffectCatalog.ActiveAudioEffects(clip).Select(e => e.Type));
    }

    [Fact]
    public void Audio_effects_are_neutral_when_their_strength_is_zero()
    {
        Assert.True(EffectCatalog.IsNeutral(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Denoise), "amount", 0)));
        Assert.True(EffectCatalog.IsNeutral(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Telephone), "intensity", 0)));
        Assert.True(EffectCatalog.IsNeutral(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Distortion), "mix", 0)));
        Assert.False(EffectCatalog.IsNeutral(EffectCatalog.Create(EffectCatalog.Distortion)));
    }

    // ---------------------------------------------------------------- TimelineEvaluator.AudioIn

    private static Project TwoAudioTracks(MediaAsset media, Clip? secondClip = null)
    {
        var p = WithClips(NewProject(media), Audio1, NewClip(media, 0, 4));
        var seq = p.MainSequence;
        var second = new Track
        {
            Id = Guid.NewGuid(),
            Kind = TrackKind.Audio,
            Name = "Áudio 2",
            Clips = [secondClip ?? NewClip(media, 0, 4)],
        };
        return p.WithSequence(seq with { Tracks = seq.Tracks.Add(second) });
    }

    private static IReadOnlyList<AudioSegment> AudioIn(Project p) =>
        TimelineEvaluator.AudioIn(p, p.MainSequence, MediaTime.Zero, S(4));

    [Fact]
    public void Audio_in_returns_a_segment_for_every_audible_track()
    {
        var media = AssetWithAudio();
        var p = TwoAudioTracks(media);

        Assert.Equal(2, AudioIn(p).Count);
    }

    [Fact]
    public void Audio_in_honours_solo_tracks()
    {
        var media = AssetWithAudio();
        var p = EditTrack(TwoAudioTracks(media), 4, t => t with { IsSolo = true });

        var segment = Assert.Single(AudioIn(p));
        Assert.Equal(p.MainSequence.Tracks[4].Id, segment.Track.Id);
    }

    [Fact]
    public void Audio_in_skips_muted_tracks_and_tracks_with_zero_volume()
    {
        var media = AssetWithAudio();

        var muted = EditTrack(TwoAudioTracks(media), 4, t => t with { IsMuted = true });
        var audible = Assert.Single(AudioIn(muted));
        Assert.Equal(muted.MainSequence.Tracks[Audio1].Id, audible.Track.Id);

        var silentTrack = EditTrack(TwoAudioTracks(media), 4, t => t with { Volume = 0 });
        var audibleToo = Assert.Single(AudioIn(silentTrack));
        Assert.Equal(silentTrack.MainSequence.Tracks[Audio1].Id, audibleToo.Track.Id);
    }

    [Fact]
    public void Audio_in_skips_zero_volume_clips_unless_their_volume_is_animated()
    {
        var media = AssetWithAudio();
        var silentClip = NewClip(media, 0, 4) with { Volume = 0 };

        Assert.Single(AudioIn(TwoAudioTracks(media, silentClip)));

        var animated = silentClip with
        {
            Keyframes = ImmutableDictionary<string, ImmutableList<Keyframe>>.Empty.Add(AnimatedProperty.Volume,
                [new Keyframe(MediaTime.Zero, 0, Easing.Linear), new Keyframe(S(2), 1, Easing.Linear)]),
        };
        Assert.Equal(2, AudioIn(TwoAudioTracks(media, animated)).Count);
    }
}
