using MeuEditorDeVideo.AudioEngine;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class PlayedPositionClockTests
{
    private const int Rate = 48000;

    [Fact]
    public void Seconds_is_submitted_minus_queued_minus_latency()
    {
        Assert.Equal(1.0, PlayedPositionClock.Seconds(96000, 48000, 0, Rate), 9);
        Assert.Equal(0.5, PlayedPositionClock.Seconds(96000, 48000, 24000, Rate), 9);
    }

    [Fact]
    public void Seconds_never_goes_negative_while_the_queue_is_still_filling()
    {
        Assert.Equal(0, PlayedPositionClock.Seconds(2880, 2880, 0, Rate));
        Assert.Equal(0, PlayedPositionClock.Seconds(2880, 0, 5000, Rate));
        Assert.Equal(0, PlayedPositionClock.Seconds(0, 0, 0, 0));
    }

    [Fact]
    public void Sample_is_monotonic_even_if_counters_regress()
    {
        var clock = new PlayedPositionClock(Rate);
        var a = clock.Sample(48000, 0, 0);
        var b = clock.Sample(48000, 24000, 0); // fila "cresceu" entre as leituras
        Assert.Equal(1.0, a, 9);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pause_freezes_the_position_and_resume_continues()
    {
        var clock = new PlayedPositionClock(Rate);
        Assert.Equal(0.5, clock.Sample(48000, 24000, 0), 9);
        clock.Pause();
        Assert.Equal(0.5, clock.Sample(96000, 0, 0), 9);
        clock.Resume();
        Assert.Equal(2.0, clock.Sample(96000, 0, 0), 9);
    }

    [Fact]
    public void Reset_returns_to_zero_and_runs_again()
    {
        var clock = new PlayedPositionClock(Rate);
        clock.Sample(96000, 0, 0);
        clock.Pause();
        clock.Reset();
        Assert.Equal(0, clock.Last);
        Assert.Equal(0.25, clock.Sample(12000, 0, 0), 9);
    }

    [Fact]
    public void Simulated_playback_advances_with_consumption_not_with_queueing()
    {
        // Envia 3 blocos de 0,1 s; o dispositivo consumiu apenas 0,1 s: o relógio marca 0,1 s, não 0,3 s.
        var clock = new PlayedPositionClock(Rate);
        long submitted = 3 * 4800;
        long queued = 2 * 4800;
        Assert.Equal(0.1, clock.Sample(submitted, queued, 0), 9);
        Assert.Equal(0.3, clock.Sample(submitted, 0, 0), 9);
    }
}
