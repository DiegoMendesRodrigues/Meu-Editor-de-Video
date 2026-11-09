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

    /// <summary>
    /// Lê "hh:mm:ss:ff", "hh:mm:ss", "mm:ss" ou segundos ("12,5"/"12.5"). Quadros usam a taxa informada.
    /// </summary>
    public static bool TryParse(string? text, FrameRate rate, out MediaTime time)
    {
        time = MediaTime.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length == 1)
        {
            var normalized = parts[0].Replace(',', '.');
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) || secs < 0 || double.IsInfinity(secs)) return false;
            time = MediaTime.FromSecondsApprox(secs);
            return true;
        }
        if (parts.Length > 4) return false;
        var numbers = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;

        long h = 0, m, s, ff = 0;
        switch (numbers.Length)
        {
            case 2: (m, s) = (numbers[0], numbers[1]); break;
            case 3: (h, m, s) = (numbers[0], numbers[1], numbers[2]); break;
            default: (h, m, s, ff) = (numbers[0], numbers[1], numbers[2], numbers[3]); break;
        }
        var fps = (long)Math.Round(rate.Value);
        if (m >= 60 || s >= 60 || ff >= fps) return false;
        time = rate.FrameToTime(((h * 60 + m) * 60 + s) * fps + ff);
        return true;
    }
}
