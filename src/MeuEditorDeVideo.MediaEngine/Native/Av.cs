using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Erro retornado por uma função do FFmpeg.</summary>
public sealed class FFmpegException(int code, string operation)
    : Exception($"{operation}: {Av.ErrorText(code)} ({code})")
{
    public int Code { get; } = code;
}

internal static unsafe class Av
{
    public static readonly int EAgain = ffmpeg.AVERROR(ffmpeg.EAGAIN);

    public static readonly AVRational TimeBaseQ = new() { num = 1, den = ffmpeg.AV_TIME_BASE };

    public static string ErrorText(int code)
    {
        const int size = 1024;
        var buffer = stackalloc byte[size];
        return ffmpeg.av_strerror(code, buffer, size) == 0
            ? Marshal.PtrToStringUTF8((IntPtr)buffer) ?? "erro desconhecido"
            : "erro desconhecido";
    }

    public static int Check(int code, string operation) =>
        code < 0 ? throw new FFmpegException(code, operation) : code;

    public static string? Str(byte* p) => p is null ? null : Marshal.PtrToStringUTF8((IntPtr)p);

    /// <summary>Converte tempo do projeto (flicks) para microssegundos (AV_TIME_BASE).</summary>
    public static long ToMicroseconds(MediaTime t) => (long)((Int128)t.Ticks * ffmpeg.AV_TIME_BASE / MediaTime.TicksPerSecond);

    public static MediaTime FromMicroseconds(long us) =>
        new((long)((Int128)us * MediaTime.TicksPerSecond / ffmpeg.AV_TIME_BASE));

    /// <summary>Converte um carimbo de tempo da faixa para tempo do projeto (relativo ao início).</summary>
    public static MediaTime StreamToTime(long pts, AVRational timeBase, long startTime)
    {
        if (pts == ffmpeg.AV_NOPTS_VALUE) return MediaTime.Zero;
        var rel = startTime == ffmpeg.AV_NOPTS_VALUE ? pts : pts - startTime;
        return FromMicroseconds(ffmpeg.av_rescale_q(rel, timeBase, TimeBaseQ));
    }

    public static string? CodecName(AVCodecID id) => id == AVCodecID.AV_CODEC_ID_NONE ? null : ffmpeg.avcodec_get_name(id);

    public static string? CodecLongName(AVCodecID id)
    {
        var d = ffmpeg.avcodec_descriptor_get(id);
        return d is null ? null : Str(d->long_name);
    }

    /// <summary>Taxa de quadros plausível de uma faixa de vídeo, ou null.</summary>
    public static FrameRate? FrameRateOf(AVStream* stream)
    {
        foreach (var r in new[] { stream->avg_frame_rate, stream->r_frame_rate })
        {
            if (r.num > 0 && r.den > 0)
            {
                var value = (double)r.num / r.den;
                if (value is >= 1 and <= 1000) return new FrameRate(r.num, r.den);
            }
        }
        return null;
    }

    /// <summary>Rotação de exibição (0/90/180/270, sentido horário) a partir da matriz de exibição.</summary>
    public static int RotationOf(AVStream* stream)
    {
        var par = stream->codecpar;
        var sd = ffmpeg.av_packet_side_data_get(par->coded_side_data, par->nb_coded_side_data, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX);
        if (sd is null || sd->data is null || sd->size < 36) return 0;
        var matrix = *(int_array9*)sd->data;
        var angle = ffmpeg.av_display_rotation_get(in matrix);
        if (double.IsNaN(angle)) return 0;
        var cw = (int)Math.Round(-angle / 90.0) * 90;
        return ((cw % 360) + 360) % 360;
    }
}
