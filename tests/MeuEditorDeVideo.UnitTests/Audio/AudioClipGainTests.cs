using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class AudioClipGainTests
{
    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static Clip MakeClip(double duration = 4) => new()
    {
        Id = Guid.NewGuid(),
        Start = MediaTime.Zero,
        Duration = S(duration),
    };

    private static ImmutableDictionary<string, ImmutableList<Keyframe>> VolumeKeys(params Keyframe[] keys) =>
        ImmutableDictionary<string, ImmutableList<Keyframe>>.Empty.Add(AnimatedProperty.Volume, [.. keys]);

    [Fact]
    public void Fade_in_gain_follows_a_quadratic_curve()
    {
        var clip = MakeClip() with { FadeIn = S(2) };

        Assert.InRange(ClipAnimation.GainAt(clip, S(0)), -1e-9, 1e-9);
        Assert.InRange(ClipAnimation.GainAt(clip, S(1)), 0.25 - 1e-9, 0.25 + 1e-9); // (1/2)²
        Assert.InRange(ClipAnimation.GainAt(clip, S(2)), 1 - 1e-9, 1 + 1e-9);
    }

    [Fact]
    public void Fade_out_gain_follows_the_remaining_time()
    {
        var clip = MakeClip() with { FadeOut = S(1) };

        Assert.InRange(ClipAnimation.GainAt(clip, S(3)), 1 - 1e-9, 1 + 1e-9);      // restam 1 s
        Assert.InRange(ClipAnimation.GainAt(clip, S(3.5)), 0.25 - 1e-9, 0.25 + 1e-9); // restam 0,5 s
        Assert.InRange(ClipAnimation.GainAt(clip, S(4)), -1e-9, 1e-9);
    }

    [Fact]
    public void Volume_keyframes_interpolate_linearly_between_their_values()
    {
        var clip = MakeClip() with
        {
            Keyframes = VolumeKeys(new Keyframe(S(0), 0.5, Easing.Linear), new Keyframe(S(4), 1.5, Easing.Linear)),
        };

        Assert.InRange(ClipAnimation.GainAt(clip, S(0)), 0.5 - 1e-9, 0.5 + 1e-9);
        Assert.InRange(ClipAnimation.GainAt(clip, S(2)), 1.0 - 1e-9, 1.0 + 1e-9);
    }

    [Fact]
    public void Scale_volume_multiplies_the_base_value_and_every_keyframe()
    {
        var clip = MakeClip() with
        {
            Volume = 1,
            Keyframes = VolumeKeys(new Keyframe(S(0), 0.5, Easing.Linear), new Keyframe(S(4), 1, Easing.Linear)),
        };

        var scaled = ClipAnimation.ScaleVolume(clip, 2);

        Assert.Equal(2.0, scaled.Volume, 9);
        Assert.Equal(new[] { 1.0, 2.0 }, ClipAnimation.KeysOf(scaled, AnimatedProperty.Volume)!.Select(k => k.Value));
    }

    [Fact]
    public void Scale_volume_clamps_to_the_maximum_volume()
    {
        var clip = MakeClip() with { Volume = 2 };

        var scaled = ClipAnimation.ScaleVolume(clip, 3);

        Assert.Equal(ClipAnimation.MaxVolume, scaled.Volume, 9);
    }

    [Fact]
    public void Scale_volume_rejects_negative_nan_and_infinite_factors()
    {
        var clip = MakeClip();

        Assert.Throws<ArgumentOutOfRangeException>(() => ClipAnimation.ScaleVolume(clip, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipAnimation.ScaleVolume(clip, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipAnimation.ScaleVolume(clip, double.PositiveInfinity));
    }
}
