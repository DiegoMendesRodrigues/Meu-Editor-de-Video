using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public sealed class DuckingTests
{
    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static Clip Music(double start = 10, double duration = 20) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = Guid.NewGuid(),
        Start = S(start),
        Duration = S(duration),
    };

    [Fact]
    public void Lowers_the_volume_during_speech_with_ramps()
    {
        var clip = Music();
        // Fala de 15 a 18 s na linha do tempo = 5 a 8 s dentro do clipe.
        var ducked = Ducking.Apply(clip, [new TimeRange(S(15), S(18))], -12, S(0.5));

        var low = Math.Pow(10, -12 / 20.0);
        Assert.Equal(1, ClipAnimation.GainAt(ducked, S(2)), 3);
        Assert.Equal(1, ClipAnimation.GainAt(ducked, S(4.5)), 3);
        Assert.Equal(low, ClipAnimation.GainAt(ducked, S(5)), 3);
        Assert.Equal(low, ClipAnimation.GainAt(ducked, S(7)), 3);
        Assert.Equal(1, ClipAnimation.GainAt(ducked, S(8.5)), 3);
        var mid = ClipAnimation.GainAt(ducked, S(4.75));
        Assert.True(mid < 1 && mid > low);
    }

    [Fact]
    public void Close_speech_ranges_are_merged_and_ranges_outside_the_clip_are_ignored()
    {
        var clip = Music();
        var ranges = new[] { new TimeRange(S(12), S(13)), new TimeRange(S(13.4), S(14)), new TimeRange(S(40), S(45)) };
        var ducked = Ducking.Apply(clip, ranges, -12, S(0.2));

        var low = Math.Pow(10, -12 / 20.0);
        Assert.Equal(low, ClipAnimation.GainAt(ducked, S(3.2)), 3);
        Assert.Equal(1, ClipAnimation.GainAt(ducked, S(15)), 3);
    }

    [Fact]
    public void Applying_again_replaces_the_previous_envelope_and_remove_restores_the_volume()
    {
        var clip = Music() with { Volume = 0.8 };
        var first = Ducking.Apply(clip, [new TimeRange(S(12), S(14))], -12, S(0.3));
        var second = Ducking.Apply(first, [new TimeRange(S(20), S(22))], -6, S(0.3));

        Assert.Equal(0.8, ClipAnimation.GainAt(second, S(3)), 3);
        Assert.Equal(0.8 * Math.Pow(10, -6 / 20.0), ClipAnimation.GainAt(second, S(11)), 3);

        var removed = Ducking.Remove(second);
        Assert.False(ClipAnimation.IsAnimated(removed, AnimatedProperty.Volume));
        Assert.Equal(0.8, removed.Volume, 3);
    }

    [Fact]
    public void No_speech_inside_the_clip_only_clears_the_volume_animation()
    {
        var clip = Ducking.Apply(Music(), [new TimeRange(S(12), S(14))], -12, S(0.3));
        var result = Ducking.Apply(clip, [new TimeRange(S(50), S(55))], -12, S(0.3));
        Assert.False(ClipAnimation.IsAnimated(result, AnimatedProperty.Volume));
    }

    [Fact]
    public void Complement_turns_silences_into_speech()
    {
        var speech = Ducking.Complement([new TimeRange(S(0), S(2)), new TimeRange(S(5), S(6))], S(0), S(10));
        Assert.Equal([new TimeRange(S(2), S(5)), new TimeRange(S(6), S(10))], speech);
    }
}
