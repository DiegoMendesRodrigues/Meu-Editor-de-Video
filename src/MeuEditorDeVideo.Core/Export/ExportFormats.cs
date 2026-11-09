using System.Globalization;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Core.Export;

/// <summary>Combinações válidas de contêiner, codecs e alfa (plano §4.4) e nomes exibidos na interface.</summary>
public static class ExportFormats
{
    private static readonly Dictionary<ExportContainer, (string Ext, VideoCodec[] Video, AudioCodec[] Audio)> Table = new()
    {
        [ExportContainer.Mp4] = (".mp4", [VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1], [AudioCodec.Aac, AudioCodec.None]),
        [ExportContainer.Mov] = (".mov", [VideoCodec.H264, VideoCodec.Hevc, VideoCodec.ProRes, VideoCodec.QtAnimation], [AudioCodec.Aac, AudioCodec.Pcm, AudioCodec.None]),
        [ExportContainer.Mkv] = (".mkv", [VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1, VideoCodec.Vp9, VideoCodec.Ffv1], [AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Flac, AudioCodec.None]),
        [ExportContainer.WebM] = (".webm", [VideoCodec.Vp9, VideoCodec.Av1], [AudioCodec.Opus, AudioCodec.Vorbis, AudioCodec.None]),
        [ExportContainer.Avi] = (".avi", [VideoCodec.Mjpeg, VideoCodec.H264], [AudioCodec.Pcm, AudioCodec.Mp3, AudioCodec.None]),
        [ExportContainer.Mpeg] = (".mpg", [VideoCodec.Mpeg2], [AudioCodec.Mp2, AudioCodec.None]),
        [ExportContainer.Gif] = (".gif", [VideoCodec.Gif], [AudioCodec.None]),
    };

    public static IReadOnlyList<ExportContainer> Containers { get; } = Enum.GetValues<ExportContainer>();

    public static string Extension(ExportContainer c) => Table[c].Ext;

    public static IReadOnlyList<VideoCodec> VideoCodecsFor(ExportContainer c) => Table[c].Video;

    public static IReadOnlyList<AudioCodec> AudioCodecsFor(ExportContainer c) => Table[c].Audio;

    /// <summary>Formatos com canal alfa: ProRes 4444, QuickTime Animation, FFV1, VP9 e GIF (transparência binária).</summary>
    public static bool SupportsAlpha(ExportContainer c, VideoCodec v) => (c, v) switch
    {
        (ExportContainer.Mov, VideoCodec.ProRes or VideoCodec.QtAnimation) => true,
        (ExportContainer.Mkv, VideoCodec.Ffv1) => true,
        (ExportContainer.WebM, VideoCodec.Vp9) => true,
        (ExportContainer.Gif, VideoCodec.Gif) => true,
        _ => false,
    };

    /// <summary>Codecs com controle de taxa de bits (os demais são intra/sem perdas ou por qualidade).</summary>
    public static bool HasBitrate(VideoCodec v) => v is VideoCodec.H264 or VideoCodec.Hevc or VideoCodec.Av1 or VideoCodec.Vp9 or VideoCodec.Mpeg2 or VideoCodec.Mjpeg;

    public static bool HasAudioBitrate(AudioCodec a) => a is AudioCodec.Aac or AudioCodec.Opus or AudioCodec.Vorbis or AudioCodec.Mp3 or AudioCodec.Mp2;

    public static string ContainerName(ExportContainer c) => c switch
    {
        ExportContainer.Mp4 => "MP4",
        ExportContainer.Mov => "MOV (QuickTime)",
        ExportContainer.Mkv => "MKV (Matroska)",
        ExportContainer.WebM => "WebM",
        ExportContainer.Avi => "AVI",
        ExportContainer.Mpeg => "MPEG-2 (.mpg)",
        ExportContainer.Gif => "GIF animado",
        _ => c.ToString(),
    };

    public static string VideoCodecName(VideoCodec v) => v switch
    {
        VideoCodec.H264 => "H.264 (AVC)",
        VideoCodec.Hevc => "H.265 (HEVC)",
        VideoCodec.Av1 => "AV1",
        VideoCodec.Vp9 => "VP9",
        VideoCodec.ProRes => "Apple ProRes",
        VideoCodec.Ffv1 => "FFV1 (sem perdas)",
        VideoCodec.QtAnimation => "QuickTime Animation",
        VideoCodec.Mjpeg => "Motion JPEG",
        VideoCodec.Mpeg2 => "MPEG-2",
        VideoCodec.Gif => "GIF",
        _ => v.ToString(),
    };

    public static string AudioCodecName(AudioCodec a) => a switch
    {
        AudioCodec.None => "Sem áudio",
        AudioCodec.Aac => "AAC",
        AudioCodec.Pcm => "PCM (sem compressão)",
        AudioCodec.Opus => "Opus",
        AudioCodec.Flac => "FLAC (sem perdas)",
        AudioCodec.Vorbis => "Vorbis",
        AudioCodec.Mp3 => "MP3",
        AudioCodec.Mp2 => "MP2",
        _ => a.ToString(),
    };

    /// <summary>Ajusta uma configuração para a combinação válida mais próxima (troca de contêiner na interface).</summary>
    public static ExportSettings Normalize(ExportSettings s)
    {
        var video = VideoCodecsFor(s.Container);
        var audio = AudioCodecsFor(s.Container);
        var v = video.Contains(s.VideoCodec) ? s.VideoCodec : video[0];
        var a = audio.Contains(s.AudioCodec) ? s.AudioCodec : audio[0];
        return s with
        {
            VideoCodec = v,
            AudioCodec = a,
            Alpha = s.Alpha && SupportsAlpha(s.Container, v),
        };
    }

    /// <summary>Problemas que impedem a exportação, em português.</summary>
    public static IEnumerable<string> Validate(ExportSettings s, ProjectSettings project)
    {
        if (!VideoCodecsFor(s.Container).Contains(s.VideoCodec))
            yield return $"O codec {VideoCodecName(s.VideoCodec)} não pode ser usado no formato {ContainerName(s.Container)}.";
        if (!AudioCodecsFor(s.Container).Contains(s.AudioCodec))
            yield return $"O áudio {AudioCodecName(s.AudioCodec)} não pode ser usado no formato {ContainerName(s.Container)}.";
        if (s.Alpha && !SupportsAlpha(s.Container, s.VideoCodec))
            yield return "O formato escolhido não suporta transparência (canal alfa).";

        var w = s.ResolveWidth(project);
        var h = s.ResolveHeight(project);
        if (w is < ProjectSettings.MinDimension or > ProjectSettings.MaxDimension || h is < ProjectSettings.MinDimension or > ProjectSettings.MaxDimension)
            yield return $"A resolução deve estar entre {ProjectSettings.MinDimension} e {ProjectSettings.MaxDimension} pixels.";
        else if (w % 2 != 0 || h % 2 != 0)
            yield return "Largura e altura devem ser números pares.";

        var fps = s.ResolveFrameRate(project).Value;
        if (fps is < 1 or > 240) yield return "A taxa de quadros deve estar entre 1 e 240 FPS.";
        if (s.VideoCodec == VideoCodec.Mpeg2 && !IsMpeg2FrameRate(fps))
            yield return "MPEG-2 aceita somente 23,976, 24, 25, 29,97, 30, 50, 59,94 ou 60 FPS.";

        if (HasBitrate(s.VideoCodec) && s.RateControl != RateControl.ConstantQuality && s.TargetSizeMegabytes is null &&
            s.VideoBitrateKbps is < 100 or > 500_000)
            yield return "A taxa de bits do vídeo deve estar entre 100 kbps e 500 Mbps.";
        if (s.RateControl == RateControl.ConstantQuality && s.Quality is < 0 or > 51)
            yield return "A qualidade constante deve estar entre 0 e 51.";
        if (s.TargetSizeMegabytes is { } mb && mb <= 0)
            yield return "O tamanho alvo deve ser maior que zero.";
        if (s.MaxSecondsPerFile is { } max && max < 1)
            yield return "A duração máxima por arquivo deve ser de pelo menos 1 segundo.";

        if (s.AudioCodec != AudioCodec.None)
        {
            if (s.SampleRate is not (44_100 or 48_000 or 96_000))
                yield return "A taxa de amostragem deve ser 44,1 kHz, 48 kHz ou 96 kHz.";
            if (s.AudioChannels is not (1 or 2 or 6))
                yield return "O áudio deve ser mono, estéreo ou 5.1.";
            if (HasAudioBitrate(s.AudioCodec) && s.AudioBitrateKbps is < 32 or > 640)
                yield return "A taxa de bits do áudio deve estar entre 32 e 640 kbps.";
        }
    }

    private static bool IsMpeg2FrameRate(double fps) =>
        new[] { 24000 / 1001.0, 24, 25, 30000 / 1001.0, 30, 50, 60000 / 1001.0, 60 }.Any(v => Math.Abs(v - fps) < 0.01);

    /// <summary>Nome do n-ésimo arquivo quando a saída é dividida por duração: "nome (1).mp4".</summary>
    public static string PartPath(string outputPath, int part, int parts)
    {
        if (parts <= 1) return outputPath;
        var dir = Path.GetDirectoryName(outputPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{name} ({part}){Path.GetExtension(outputPath)}"));
    }

    /// <summary>Taxa de bits do vídeo para atingir um tamanho alvo (desconta áudio e ~2 % de multiplexação).</summary>
    public static int BitrateForTargetSize(double megabytes, double seconds, int audioKbps)
    {
        if (seconds <= 0) return 1000;
        var totalKbps = megabytes * 8 * 1024 * 1024 / 1000 / seconds * 0.98;
        return (int)Math.Clamp(totalKbps - audioKbps, 100, 500_000);
    }
}
