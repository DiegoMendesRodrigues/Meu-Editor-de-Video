using System.Globalization;

namespace MeuEditorDeVideo.Domain.Time;

/// <summary>Formatação de tempo para exibição (hh:mm:ss:ff), baseada em contagem real de quadros (sem drop-frame).</summary>
public static class Timecode
{
    public static string Format(MediaTime time, FrameRate rate)
    {
        var negative = time.Ticks < 0;
        var frames = Math.Abs(rate.TimeToFrame(time));
        var fpsRounded = (long)Math.Round(rate.Value);
        var ff = frames % fpsRounded;
        var totalSeconds = frames / fpsRounded;
        var s = totalSeconds % 60;
        var m = totalSeconds / 60 % 60;
        var h = totalSeconds / 3600;
        return string.Create(CultureInfo.InvariantCulture, $"{(negative ? "-" : "")}{h:00}:{m:00}:{s:00}:{ff:00}");
    }

    /// <summary>Formato curto hh:mm:ss usado no monitor e na régua.</summary>
    public static string FormatSeconds(MediaTime time)
    {
        var total = (long)Math.Floor(time.TotalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}");
    }
}
