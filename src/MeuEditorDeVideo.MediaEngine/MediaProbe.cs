using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Native;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine;

/// <summary>Identifica tipo, duração, resolução, FPS, codecs e contêiner, e detecta arquivos danificados (RF029–RF032, RF036).</summary>
public sealed unsafe class MediaProbe(ILogger<MediaProbe> logger) : IMediaProbe
{
    /// <summary>Demuxers de imagem única do FFmpeg ("png_pipe", "jpeg_pipe", "image2"…).</summary>
    private static bool IsImageDemuxer(string name) =>
        name == "image2" || name.EndsWith("_pipe", StringComparison.Ordinal);

    public Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken = default) =>
        MediaWorkers.RunAsync(() => Probe(path), cancellationToken);

    public Task<MediaProbeResult> ProbeSequenceAsync(ImageSequenceInfo sequence, FrameRate frameRate, CancellationToken cancellationToken = default) =>
        MediaWorkers.RunAsync(() => ProbeSequence(sequence, frameRate), cancellationToken);

    private MediaProbeResult Probe(string path)
    {
        if (!File.Exists(path))
            return MediaProbeResult.Failed(path, MediaProbeStatus.Corrupted, "O arquivo não foi encontrado.");

        if (new FileInfo(path).Length == 0)
            return MediaProbeResult.Failed(path, MediaProbeStatus.Corrupted, "O arquivo está vazio.");

        MediaFile file;
        try
        {
            file = MediaFile.Open(path);
        }
        catch (FFmpegException ex)
        {
            logger.LogInformation("Arquivo não reconhecido ({Code}): {Ext}", ex.Code, Path.GetExtension(path));
            return MediaFormats.IsSupported(path)
                ? MediaProbeResult.Failed(path, MediaProbeStatus.Corrupted, "O arquivo está danificado ou incompleto e não pôde ser lido.")
                : MediaProbeResult.Failed(path, MediaProbeStatus.Unsupported, "Formato de arquivo não suportado.");
        }
        catch (MedException)
        {
            throw;
        }

        using (file)
        {
            return Analyze(path, file, sequence: null, sequenceRate: null);
        }
    }

    private MediaProbeResult ProbeSequence(ImageSequenceInfo sequence, FrameRate frameRate)
    {
        var first = sequence.PathOf(sequence.FirstNumber);
        try
        {
            using var file = MediaFile.Open(sequence.FfmpegPattern, "image2", new Dictionary<string, string>
            {
                ["start_number"] = sequence.FirstNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["framerate"] = $"{frameRate.Numerator}/{frameRate.Denominator}",
            });
            return Analyze(first, file, sequence, frameRate);
        }
        catch (FFmpegException ex)
        {
            logger.LogInformation("Sequência não reconhecida ({Code}).", ex.Code);
            return MediaProbeResult.Failed(first, MediaProbeStatus.Corrupted, "As imagens da sequência não puderam ser lidas.");
        }
    }

    private MediaProbeResult Analyze(string path, MediaFile file, ImageSequenceInfo? sequence, FrameRate? sequenceRate)
    {
        var ctx = file.Context;
        var demuxer = Av.Str(ctx->iformat->name) ?? "";

        int videoStreams = 0, audioStreams = 0, subtitleStreams = 0;
        for (var i = 0; i < file.StreamCount; i++)
        {
            switch (file.Stream(i)->codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_VIDEO when !file.IsAttachedPicture(i): videoStreams++; break;
                case AVMediaType.AVMEDIA_TYPE_AUDIO: audioStreams++; break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE: subtitleStreams++; break;
            }
        }

        var vIndex = file.BestStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
        var aIndex = file.BestStream(AVMediaType.AVMEDIA_TYPE_AUDIO);

        var info = new MediaTechnicalInfo
        {
            Container = demuxer,
            ContainerLongName = Av.Str(ctx->iformat->long_name),
            BitRate = Math.Max(0, ctx->bit_rate),
            VideoStreams = videoStreams,
            AudioStreams = audioStreams,
            SubtitleStreams = subtitleStreams,
        };

        int width = 0, height = 0;
        FrameRate? fps = null;
        if (vIndex >= 0)
        {
            var vs = file.Stream(vIndex);
            var par = vs->codecpar;
            var pixFmt = (AVPixelFormat)par->format;
            var desc = ffmpeg.av_pix_fmt_desc_get(pixFmt);
            var rotation = Av.RotationOf(vs);
            (width, height) = rotation is 90 or 270 ? (par->height, par->width) : (par->width, par->height);
            fps = Av.FrameRateOf(vs);
            info = info with
            {
                VideoCodec = Av.CodecName(par->codec_id),
                VideoCodecLongName = Av.CodecLongName(par->codec_id),
                PixelFormat = pixFmt == AVPixelFormat.AV_PIX_FMT_NONE ? null : ffmpeg.av_get_pix_fmt_name(pixFmt),
                HasAlpha = desc is not null && (desc->flags & ffmpeg.AV_PIX_FMT_FLAG_ALPHA) != 0,
                Rotation = rotation,
            };
        }

        if (aIndex >= 0)
        {
            var par = file.Stream(aIndex)->codecpar;
            info = info with
            {
                AudioCodec = Av.CodecName(par->codec_id),
                AudioCodecLongName = Av.CodecLongName(par->codec_id),
                SampleRate = par->sample_rate,
                Channels = par->ch_layout.nb_channels,
                ChannelLayout = DescribeLayout(&par->ch_layout),
            };
        }

        // Classificação do tipo de mídia.
        MediaKind kind;
        if (sequence is not null) kind = MediaKind.ImageSequence;
        else if (vIndex >= 0 && demuxer == "gif") kind = CountFrames(file, vIndex, 2) > 1 ? MediaKind.AnimatedGif : MediaKind.Image;
        else if (vIndex >= 0 && (IsImageDemuxer(demuxer) || (demuxer == "webp" && CountFrames(file, vIndex, 2) <= 1))) kind = MediaKind.Image;
        else if (vIndex >= 0) kind = MediaKind.Video;
        else if (aIndex >= 0) kind = MediaKind.Audio;
        else return MediaProbeResult.Failed(path, MediaProbeStatus.Unsupported, "O arquivo não contém vídeo, imagem nem áudio.");

        // Duração.
        var duration = MediaTime.Zero;
        if (kind == MediaKind.ImageSequence && sequence is not null && sequenceRate is { } rate)
        {
            duration = rate.FrameToTime(sequence.FrameCount);
            fps = rate;
        }
        else if (kind != MediaKind.Image)
        {
            if (ctx->duration != ffmpeg.AV_NOPTS_VALUE && ctx->duration > 0)
                duration = Av.FromMicroseconds(ctx->duration);
            else
            {
                var s = file.Stream(vIndex >= 0 ? vIndex : aIndex);
                if (s->duration != ffmpeg.AV_NOPTS_VALUE && s->duration > 0)
                    duration = Av.StreamToTime(s->duration, s->time_base, ffmpeg.AV_NOPTS_VALUE);
            }
        }
        else
        {
            fps = null;
        }

        // Verificação de integridade: decodificar o primeiro quadro/bloco de áudio (RF036).
        var verify = VerifyDecodable(file, kind == MediaKind.Audio ? -1 : vIndex, kind == MediaKind.Audio ? aIndex : -1);
        if (verify is not null)
            return MediaProbeResult.Failed(path, verify.Value.Status, verify.Value.Message) with { Kind = kind, Info = info };

        if (kind is MediaKind.Video or MediaKind.Audio && duration <= MediaTime.Zero)
            return MediaProbeResult.Failed(path, MediaProbeStatus.Corrupted, "Não foi possível identificar a duração do arquivo.") with { Kind = kind, Info = info };

        return new MediaProbeResult(path, MediaProbeStatus.Ok, kind, duration, width, height, fps, info);
    }

    private (MediaProbeStatus Status, string Message)? VerifyDecodable(MediaFile file, int videoIndex, int audioIndex)
    {
        var index = videoIndex >= 0 ? videoIndex : audioIndex;
        try
        {
            // Volta ao início (CountFrames pode ter avançado a leitura).
            ffmpeg.av_seek_frame(file.Context, -1, file.Context->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : file.Context->start_time, ffmpeg.AVSEEK_FLAG_BACKWARD);
            using var decoder = new StreamDecoder(file, index, threads: 1);
            return decoder.ReadFrame() ? null : (MediaProbeStatus.Corrupted, "Nenhum quadro pôde ser decodificado: o arquivo parece danificado.");
        }
        catch (DecoderNotFoundException ex)
        {
            return (MediaProbeStatus.Unsupported, $"O codec \"{ex.Codec}\" não é suportado.");
        }
        catch (FFmpegException ex)
        {
            logger.LogInformation("Falha ao decodificar ({Code}).", ex.Code);
            return (MediaProbeStatus.Corrupted, "O arquivo está danificado e não pôde ser decodificado.");
        }
    }

    /// <summary>Conta pacotes da faixa até <paramref name="limit"/> (para distinguir imagem estática de animação).</summary>
    private static int CountFrames(MediaFile file, int streamIndex, int limit)
    {
        var count = 0;
        var pkt = ffmpeg.av_packet_alloc();
        try
        {
            while (count < limit && ffmpeg.av_read_frame(file.Context, pkt) >= 0)
            {
                if (pkt->stream_index == streamIndex) count++;
                ffmpeg.av_packet_unref(pkt);
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&pkt);
        }
        return count;
    }

    private static string? DescribeLayout(AVChannelLayout* layout)
    {
        const int size = 128;
        var buffer = stackalloc byte[size];
        return ffmpeg.av_channel_layout_describe(layout, buffer, size) > 0 ? Marshal.PtrToStringUTF8((IntPtr)buffer) : null;
    }
}
