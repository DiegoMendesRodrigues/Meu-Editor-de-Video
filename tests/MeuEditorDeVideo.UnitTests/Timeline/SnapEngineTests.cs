using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Timeline;

public sealed class SnapEngineTests
{
    private static readonly FrameRate Fr = FrameRate.Fps30;

    private static MediaTime S(long seconds) => MediaTime.FromSeconds(seconds);

    // 1 ms = 705.600 flicks (exato).
    private static MediaTime Ms(long milliseconds) => new(milliseconds * 705_600);

    private static Clip ClipAt(MediaTime start, MediaTime duration) => new()
    {
        Id = Guid.NewGuid(),
        Start = start,
        Duration = duration,
    };

    private static Sequence SequenceWith(IEnumerable<Clip> clips, params Marker[] markers) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Sequência",
        Tracks = [new Track { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Vídeo 1", Clips = [.. clips] }],
        Markers = [.. markers],
    };

    // Clipe de 2 s a 4 s: bordas em 2 s e 4 s.
    private static Clip ClipTwoToFour() => ClipAt(S(2), S(2));

    [Fact]
    public void Snap_attaches_to_clip_edge_within_tolerance()
    {
        var clip = ClipTwoToFour();
        var engine = new SnapEngine(SequenceWith([clip]), Fr, playhead: S(10));

        var result = engine.Snap(Ms(4100), Ms(200));

        Assert.Equal(S(4), result.Time);
        Assert.Equal(SnapKind.ClipEdge, result.Kind);
        Assert.Equal(S(4), result.Target);
        Assert.True(result.Snapped);
    }

    [Fact]
    public void Snap_outside_tolerance_returns_frame_grid_time_without_target()
    {
        var engine = new SnapEngine(SequenceWith([ClipTwoToFour()]), Fr, playhead: S(10));
        // 3,5 s + 0,7 quadro: nenhuma borda a 0,1 s, então vai para o quadro 106.
        var time = new MediaTime(Fr.FrameToTime(105).Ticks + Fr.FrameDuration.Ticks * 7 / 10);

        var result = engine.Snap(time, Ms(100));

        Assert.Equal(SnapKind.None, result.Kind);
        Assert.Null(result.Target);
        Assert.False(result.Snapped);
        Assert.Equal(Fr.FrameToTime(106), result.Time);
    }

    [Fact]
    public void Snap_tie_between_playhead_and_clip_edge_prefers_playhead()
    {
        // Borda do clipe em 4 s e cursor em 6 s; o instante 5 s está a igual distância de ambos.
        var engine = new SnapEngine(SequenceWith([ClipTwoToFour()]), Fr, playhead: S(6));

        var result = engine.Snap(S(5), S(1));

        Assert.Equal(SnapKind.Playhead, result.Kind);
        Assert.Equal(S(6), result.Time);
    }

    [Fact]
    public void Snap_to_marker_within_tolerance()
    {
        var marker = new Marker { Id = Guid.NewGuid(), Time = S(7) };
        var engine = new SnapEngine(SequenceWith([], marker), Fr, playhead: S(20));

        var result = engine.Snap(Ms(6900), Ms(200));

        Assert.Equal(SnapKind.Marker, result.Kind);
        Assert.Equal(S(7), result.Time);
    }

    [Fact]
    public void Snap_to_timeline_start_within_tolerance()
    {
        var engine = new SnapEngine(SequenceWith([ClipTwoToFour()]), Fr, playhead: S(10));

        var result = engine.Snap(Ms(50), Ms(100));

        Assert.Equal(SnapKind.Zero, result.Kind);
        Assert.Equal(MediaTime.Zero, result.Time);
    }

    [Fact]
    public void SnapRange_snaps_end_of_range_to_edge_and_returns_adjusted_start()
    {
        // Início em 1,5 s (sem alvo perto), fim em 4,1 s: o fim gruda na borda de 4 s.
        var engine = new SnapEngine(SequenceWith([ClipTwoToFour()]), Fr, playhead: S(10));

        var result = engine.SnapRange(Ms(1500), Ms(2600), Ms(200));

        Assert.Equal(Ms(1400), result.Time);
        Assert.Equal(SnapKind.ClipEdge, result.Kind);
        Assert.Equal(S(4), result.Target);
    }

    [Fact]
    public void SnapRange_snaps_start_of_range_to_edge()
    {
        var engine = new SnapEngine(SequenceWith([ClipTwoToFour()]), Fr, playhead: S(10));

        var result = engine.SnapRange(Ms(1900), S(3), Ms(200));

        Assert.Equal(S(2), result.Time);
        Assert.Equal(SnapKind.ClipEdge, result.Kind);
    }

    [Fact]
    public void Excluded_clips_are_not_snap_targets()
    {
        var clip = ClipTwoToFour();
        var engine = new SnapEngine(SequenceWith([clip]), Fr, playhead: S(10), excludeClips: new HashSet<Guid> { clip.Id });

        var result = engine.Snap(Ms(4100), Ms(200));

        Assert.Equal(SnapKind.None, result.Kind);
        Assert.Equal(Fr.FrameToTime(123), result.Time); // 4,1 s já está no quadro 123.
    }

    [Fact]
    public void Disabled_snapping_uses_frame_grid_only()
    {
        var engine = new SnapEngine(SequenceWith([ClipTwoToFour()]), Fr, playhead: S(10));
        // 4 s + 0,7 quadro: com ímã ligado iria para a borda; desligado, vai para o quadro 121.
        var time = new MediaTime(Fr.FrameToTime(120).Ticks + Fr.FrameDuration.Ticks * 7 / 10);

        var snapped = engine.Snap(time, S(1));
        var disabled = engine.Snap(time, S(1), enabled: false);
        var rangeDisabled = engine.SnapRange(Ms(1900), S(2), S(1), enabled: false);

        Assert.Equal(SnapKind.ClipEdge, snapped.Kind);
        Assert.Equal(SnapKind.None, disabled.Kind);
        Assert.Equal(Fr.FrameToTime(121), disabled.Time);
        Assert.Equal(SnapKind.None, rangeDisabled.Kind);
        Assert.Equal(Fr.FrameToTime(57), rangeDisabled.Time); // 1,9 s = quadro 57.
    }
}
