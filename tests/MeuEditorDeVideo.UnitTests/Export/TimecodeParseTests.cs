using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Export;

public sealed class TimecodeParseTests
{
    [Fact]
    public void Frame_timecode_is_parsed_using_the_frame_rate()
    {
        Assert.True(Timecode.TryParse("00:00:01:15", FrameRate.Fps30, out var time));

        Assert.Equal(FrameRate.Fps30.FrameToTime(45), time);
    }

    [Fact]
    public void Minutes_and_seconds_are_parsed()
    {
        Assert.True(Timecode.TryParse("01:02", FrameRate.Fps30, out var time));

        Assert.Equal(MediaTime.FromSeconds(62), time);
    }

    [Fact]
    public void Hours_minutes_and_seconds_are_parsed()
    {
        Assert.True(Timecode.TryParse("00:01:00", FrameRate.Fps30, out var time));

        Assert.Equal(MediaTime.FromSeconds(60), time);
    }

    [Theory]
    [InlineData("12,5")]
    [InlineData("12.5")]
    public void Plain_seconds_accept_comma_or_dot(string text)
    {
        Assert.True(Timecode.TryParse(text, FrameRate.Fps30, out var time));

        Assert.Equal(MediaTime.FromSecondsApprox(12.5), time);
        Assert.InRange(time.TotalSeconds, 12.5 - 1e-6, 12.5 + 1e-6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("00:61:00")]
    [InlineData("00:00:60")]
    [InlineData("-1")]
    [InlineData("1:2:3:4:5")]
    public void Invalid_text_is_rejected(string text)
    {
        Assert.False(Timecode.TryParse(text, FrameRate.Fps30, out var time));
        Assert.Equal(MediaTime.Zero, time);
    }

    [Fact]
    public void Frame_number_equal_to_frame_rate_is_rejected()
    {
        Assert.False(Timecode.TryParse("00:00:00:30", FrameRate.Fps30, out _));
        Assert.True(Timecode.TryParse("00:00:00:29", FrameRate.Fps30, out _));
    }

    [Fact]
    public void Frame_number_is_checked_against_25_fps()
    {
        Assert.False(Timecode.TryParse("00:00:00:25", FrameRate.Fps25, out _));
        Assert.True(Timecode.TryParse("00:00:00:24", FrameRate.Fps25, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(1000)]
    [InlineData(90_007)]
    public void Format_then_parse_round_trips_at_25_and_30_fps(long frame)
    {
        foreach (var rate in new[] { FrameRate.Fps25, FrameRate.Fps30 })
        {
            var time = rate.FrameToTime(frame);
            var text = Timecode.Format(time, rate);

            Assert.True(Timecode.TryParse(text, rate, out var parsed), text);
            Assert.Equal(rate.TimeToFrame(time), rate.TimeToFrame(parsed));
            Assert.Equal(time, parsed);
        }
    }
}
