using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Export;

/// <summary>Fornece a mixagem final (estéreo float 48 kHz intercalado) a partir de um quadro de áudio; devolve quantos quadros preencheu.</summary>
public delegate int AudioFill(long fromFrame, Span<float> destination);

/// <summary>
/// Multiplexação final: junta os trechos de vídeo já codificados, codifica o áudio em paralelo (intercalado pelo
/// carimbo de tempo, para não acumular memória) e grava o contêiner. Em caso de falha o arquivo parcial é apagado.
/// </summary>
public static unsafe class ExportMuxer
{
    private const int AudioChunkFrames = 4096;

    public static string FormatName(ExportContainer c) => c switch
    {
        ExportContainer.Mp4 => "mp4",
        ExportContainer.Mov => "mov",
        ExportContainer.Mkv => "matroska",
        ExportContainer.WebM => "webm",
        ExportContainer.Avi => "avi",
        ExportContainer.Mpeg => "mpeg",
        ExportContainer.Gif => "gif",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>O contêiner guarda os parâmetros do codec no cabeçalho (os codificadores precisam saber antes de abrir).</summary>
    public static bool NeedsGlobalHeader(ExportContainer c)
    {
        FFmpegRuntime.EnsureInitialized();
        var fmt = ffmpeg.av_guess_format(FormatName(c), null, null);
        return fmt != null && (fmt->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0;
    }

    public static void Mux(string outputPath, ExportSettings settings, IReadOnlyList<string> segments, long audioFrames,
        AudioFill? audio, Action<double>? progress, CancellationToken ct)
    {
        if (segments.Count == 0) throw new ArgumentException("Nenhum trecho para multiplexar.", nameof(segments));
        FFmpegRuntime.EnsureInitialized();

        var headers = new List<(SegmentFile.Header Header, long Frames)>();
        foreach (var s in segments)
        {
            var h = SegmentFile.TryReadComplete(s, out var frames) ?? throw new InvalidDataException($"Trecho incompleto: {Path.GetFileName(s)}");
            headers.Add((h, frames));
        }
        var first = headers[0].Header;
        var totalFrames = Math.Max(1, headers.Sum(h => h.Frames));

        AVFormatContext* oc = null;
        AudioEncoder? audioEncoder = null;
        var opened = false;
        var success = false;
        try
        {
            Av.Check(ffmpeg.avformat_alloc_output_context2(&oc, null, FormatName(settings.Container), outputPath), "criar arquivo de saída");

            var vs = ffmpeg.avformat_new_stream(oc, null);
            first.ApplyTo(vs->codecpar);
            if (first.CodecId == AVCodecID.AV_CODEC_ID_HEVC && settings.Container is ExportContainer.Mp4 or ExportContainer.Mov)
                vs->codecpar->codec_tag = 'h' | ('v' << 8) | ('c' << 16) | ((uint)'1' << 24); // compatibilidade com Apple
            vs->time_base = first.TimeBase;
            vs->avg_frame_rate = first.FrameRate;
            if (settings.Alpha && first.CodecId == AVCodecID.AV_CODEC_ID_VP9)
                ffmpeg.av_dict_set(&vs->metadata, "alpha_mode", "1", 0);

            AVStream* aStream = null;
            if (audio is not null && settings.AudioCodec != AudioCodec.None && settings.Container != ExportContainer.Gif)
            {
                audioEncoder = new AudioEncoder(settings, (oc->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0);
                aStream = ffmpeg.avformat_new_stream(oc, null);
                Av.Check(ffmpeg.avcodec_parameters_from_context(aStream->codecpar, audioEncoder.Context), "configurar faixa de áudio");
                aStream->time_base = audioEncoder.TimeBase;
            }

            if ((oc->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                Av.Check(ffmpeg.avio_open(&oc->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE), "abrir arquivo de saída");
            opened = true;

            AVDictionary* opts = null;
            if (settings.Container is ExportContainer.Mp4 or ExportContainer.Mov) ffmpeg.av_dict_set(&opts, "movflags", "+faststart", 0);
            try
            {
                Av.Check(ffmpeg.avformat_write_header(oc, &opts), "gravar cabeçalho");
            }
            finally
            {
                ffmpeg.av_dict_free(&opts);
            }

            var ctx = oc;
            var videoTb = vs->time_base;
            var videoIndex = vs->index;
            var lastDts = ffmpeg.AV_NOPTS_VALUE;
            long audioDone = 0;
            var audioBuffer = new float[AudioChunkFrames * AudioFormat.Channels];
            var encoder = audioEncoder;
            var audioStream = aStream;

            void WriteAudioPacket(AVPacket* p)
            {
                p->stream_index = audioStream->index;
                ffmpeg.av_packet_rescale_ts(p, encoder!.TimeBase, audioStream->time_base);
                Av.Check(ffmpeg.av_interleaved_write_frame(ctx, p), "gravar áudio");
            }

            void PumpAudio(double untilSeconds)
            {
                if (encoder is null || audio is null) return;
                var until = Math.Min(audioFrames, (long)Math.Ceiling(untilSeconds * AudioFormat.SampleRate));
                while (audioDone < until)
                {
                    ct.ThrowIfCancellationRequested();
                    var n = (int)Math.Min(AudioChunkFrames, until - audioDone);
                    var span = audioBuffer.AsSpan(0, n * AudioFormat.Channels);
                    var filled = Math.Clamp(audio(audioDone, span), 0, n);
                    span[(filled * AudioFormat.Channels)..].Clear();
                    encoder.Write(span, WriteAudioPacket);
                    audioDone += n;
                }
            }

            long written = 0;
            foreach (var (path, _) in segments.Zip(headers))
            {
                SegmentFile.ReadPackets(path, ptr =>
                {
                    var p = (AVPacket*)ptr;
                    p->stream_index = videoIndex;
                    ffmpeg.av_packet_rescale_ts(p, first.TimeBase, videoTb);
                    // Emenda entre trechos: decodificação sempre crescente.
                    if (p->dts != ffmpeg.AV_NOPTS_VALUE && lastDts != ffmpeg.AV_NOPTS_VALUE && p->dts <= lastDts) p->dts = lastDts + 1;
                    if (p->pts != ffmpeg.AV_NOPTS_VALUE && p->dts != ffmpeg.AV_NOPTS_VALUE && p->pts < p->dts) p->pts = p->dts;
                    if (p->dts != ffmpeg.AV_NOPTS_VALUE) lastDts = p->dts;

                    var seconds = p->pts == ffmpeg.AV_NOPTS_VALUE ? 0 : p->pts * (double)videoTb.num / videoTb.den;
                    PumpAudio(seconds + 0.5);
                    Av.Check(ffmpeg.av_interleaved_write_frame(ctx, p), "gravar vídeo");
                    written++;
                    if ((written & 15) == 0) progress?.Invoke(Math.Min(1, (double)written / totalFrames));
                }, ct);
            }

            PumpAudio(double.MaxValue);
            encoder?.Flush(WriteAudioPacket);
            Av.Check(ffmpeg.av_write_trailer(oc), "finalizar arquivo");
            progress?.Invoke(1);
            success = true;
        }
        finally
        {
            audioEncoder?.Dispose();
            if (oc != null)
            {
                if (opened && (oc->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) ffmpeg.avio_closep(&oc->pb);
                ffmpeg.avformat_free_context(oc);
            }
            if (!success)
            {
                try { File.Delete(outputPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
