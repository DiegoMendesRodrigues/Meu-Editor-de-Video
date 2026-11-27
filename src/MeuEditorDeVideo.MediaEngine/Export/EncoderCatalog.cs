// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Concurrent;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Export;

/// <summary>
/// Codificadores de vídeo por codec, sondados em tempo de execução (RF: NVENC/AMF/QSV/Media Foundation/software).
/// Um codificador de hardware só é oferecido se abrir de fato nesta máquina (GPU e driver presentes).
/// </summary>
public static unsafe class EncoderCatalog
{
    private static readonly ConcurrentDictionary<string, bool> Probed = new();

    /// <summary>
    /// Codificação VA-API (Linux) exige dispositivo + contexto de quadros de hardware (hwupload a partir de NV12), que o
    /// <c>VideoEncoder</c> ainda não monta (ele entrega quadros de software). Enquanto for falso, nenhum codificador *_vaapi
    /// é oferecido nem selecionado.
    /// TODO: criar AVHWDeviceContext/AVHWFramesContext (AV_HWDEVICE_TYPE_VAAPI), fazer av_hwframe_transfer_data do NV12 e ligar esta chave.
    /// </summary>
    public const bool VaapiEncodingSupported = false;

    /// <summary>Codificadores de software por codec (iguais em todos os sistemas).</summary>
    private static readonly Dictionary<VideoCodec, string[]> SoftwareNames = new()
    {
        [VideoCodec.H264] = ["libx264"],
        [VideoCodec.Hevc] = ["libx265"],
        [VideoCodec.Av1] = ["libsvtav1", "libaom-av1"],
        [VideoCodec.Vp9] = ["libvpx-vp9"],
        [VideoCodec.Mpeg2] = ["mpeg2video"],
        [VideoCodec.Mjpeg] = ["mjpeg"],
        [VideoCodec.ProRes] = ["prores_ks"],
        [VideoCodec.Ffv1] = ["ffv1"],
        [VideoCodec.QtAnimation] = ["qtrle"],
        [VideoCodec.Gif] = ["gif"],
    };

    /// <summary>Sufixos de hardware por codec, na ordem de tentativa, por sistema operacional.</summary>
    private static string[] HardwareSuffixes(VideoCodec codec, MediaOs os, bool includeVaapi) => (os, codec) switch
    {
        (MediaOs.Windows, VideoCodec.H264 or VideoCodec.Hevc or VideoCodec.Av1) => ["nvenc", "amf", "qsv", "mf"],
        (MediaOs.Windows, VideoCodec.Vp9 or VideoCodec.Mpeg2) => ["qsv"],
        (MediaOs.Linux, VideoCodec.H264 or VideoCodec.Hevc or VideoCodec.Av1) => includeVaapi ? ["nvenc", "vaapi", "qsv"] : ["nvenc", "qsv"],
        (MediaOs.Linux, VideoCodec.Vp9 or VideoCodec.Mpeg2) => includeVaapi ? ["vaapi", "qsv"] : ["qsv"],
        (MediaOs.MacOs, VideoCodec.H264 or VideoCodec.Hevc) => ["videotoolbox"],
        _ => [],
    };

    private static string Prefix(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.Hevc => "hevc",
        VideoCodec.Av1 => "av1",
        VideoCodec.Vp9 => "vp9",
        _ => "mpeg2",
    };

    /// <summary>Nomes dos codificadores de hardware candidatos do codec no sistema operacional, em ordem de tentativa.</summary>
    public static IReadOnlyList<string> HardwareNames(VideoCodec codec, MediaOs os, bool includeVaapi = VaapiEncodingSupported)
        => HardwareSuffixes(codec, os, includeVaapi).Select(s => $"{Prefix(codec)}_{s}").ToArray();

    private static string[] Hardware(VideoCodec codec) => [.. HardwareNames(codec, FFmpegPlatform.Current)];

    private static readonly HashSet<string> AllHardwareNames =
    [
        .. Enum.GetValues<MediaOs>().SelectMany(os => Enum.GetValues<VideoCodec>().SelectMany(c => HardwareNames(c, os, includeVaapi: true))),
    ];

    public static string DisplayName(string encoder) => encoder switch
    {
        var e when e.EndsWith("_nvenc", StringComparison.Ordinal) => "NVIDIA NVENC (GPU)",
        var e when e.EndsWith("_amf", StringComparison.Ordinal) => "AMD AMF (GPU)",
        var e when e.EndsWith("_qsv", StringComparison.Ordinal) => "Intel Quick Sync (GPU)",
        var e when e.EndsWith("_mf", StringComparison.Ordinal) => "Media Foundation (Windows)",
        var e when e.EndsWith("_vaapi", StringComparison.Ordinal) => "VA-API (GPU)",
        var e when e.EndsWith("_videotoolbox", StringComparison.Ordinal) => "Apple VideoToolbox (GPU)",
        "libx264" => "x264 (software)",
        "libx265" => "x265 (software)",
        "libsvtav1" => "SVT-AV1 (software)",
        "libaom-av1" => "libaom (software)",
        "libvpx-vp9" => "libvpx (software)",
        _ => $"{encoder} (software)",
    };

    public static bool IsHardware(string encoder) => AllHardwareNames.Contains(encoder);

    /// <summary>
    /// Codificadores por hardware que funcionam nesta máquina (§12.3), por fabricante e codec — ex.:
    /// "NVIDIA NVENC (GPU): H.264, HEVC, AV1". A primeira chamada testa cada codificador (pode levar alguns segundos).
    /// </summary>
    public static IReadOnlyList<string> HardwareSummary()
    {
        var codecs = new[] { (VideoCodec.H264, "H.264"), (VideoCodec.Hevc, "HEVC"), (VideoCodec.Av1, "AV1") };
        return codecs
            .SelectMany(c => Hardware(c.Item1).Where(Works).Select(n => (Family: DisplayName(n), Codec: c.Item2)))
            .GroupBy(x => x.Family)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Codec))}")
            .ToList();
    }

    /// <summary>Todos os codificadores que funcionam nesta máquina para o codec (hardware primeiro).</summary>
    public static IReadOnlyList<EncoderInfo> Available(VideoCodec codec)
    {
        var hw = Hardware(codec);
        return hw.Concat(SoftwareNames[codec]).Where(Works).Select(n => new EncoderInfo(n, DisplayName(n), hw.Contains(n))).ToList();
    }

    /// <summary>
    /// Ordem de tentativa, sem sondar (função pura, testável para qualquer SO): Automático = GPU dedicada
    /// (NVENC/AMF/QSV/VA-API/VideoToolbox) e depois software; Hardware = inclui Media Foundation antes do software;
    /// Software = só software. Alfa exige software.
    /// </summary>
    public static IReadOnlyList<string> CandidateOrder(VideoCodec codec, EncoderPreference preference, bool alpha, MediaOs os, bool includeVaapi = VaapiEncodingSupported)
    {
        var hw = HardwareNames(codec, os, includeVaapi);
        var sw = SoftwareNames[codec];
        IEnumerable<string> order = preference switch
        {
            EncoderPreference.Software => sw,
            EncoderPreference.Hardware => hw.Concat(sw),
            _ => hw.Where(n => !n.EndsWith("_mf", StringComparison.Ordinal)).Concat(sw),
        };
        if (alpha) order = order.Where(n => !IsHardware(n));
        return order.ToList();
    }

    /// <summary>Candidatos do codec neste SO que funcionam nesta máquina, na ordem de <see cref="CandidateOrder"/>.</summary>
    public static IReadOnlyList<string> Candidates(VideoCodec codec, EncoderPreference preference, bool alpha)
        => CandidateOrder(codec, preference, alpha, FFmpegPlatform.Current).Where(Works).ToList();

    /// <summary>O codificador existe nesta compilação do FFmpeg e abre com parâmetros mínimos.</summary>
    public static bool Works(string name) => Probed.GetOrAdd(name, Probe);

    private static bool Probe(string name)
    {
        FFmpegRuntime.EnsureInitialized();
        var codec = ffmpeg.avcodec_find_encoder_by_name(name);
        if (codec is null) return false;
        if (name == "gif") return true;

        var ctx = ffmpeg.avcodec_alloc_context3(codec);
        try
        {
            ctx->width = 640;
            ctx->height = 360;
            ctx->time_base = new AVRational { num = 1, den = 30 };
            ctx->framerate = new AVRational { num = 30, den = 1 };
            var formats = VideoEncoder.SupportedPixelFormats(ctx, codec);
            ctx->pix_fmt = formats.Contains(AVPixelFormat.AV_PIX_FMT_YUV420P) ? AVPixelFormat.AV_PIX_FMT_YUV420P
                : formats.Count > 0 ? formats[0] : AVPixelFormat.AV_PIX_FMT_YUV420P;
            ctx->bit_rate = 2_000_000;
            if (name == "mjpeg") ctx->color_range = AVColorRange.AVCOL_RANGE_JPEG; // MJPEG exige faixa completa
            return ffmpeg.avcodec_open2(ctx, codec, null) >= 0;
        }
        catch (System.Runtime.InteropServices.SEHException)
        {
            return false;
        }
        finally
        {
            ffmpeg.avcodec_free_context(&ctx);
        }
    }

}
