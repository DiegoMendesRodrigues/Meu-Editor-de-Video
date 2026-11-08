using FFmpeg.AutoGen;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Arquivo aberto pelo FFmpeg (AVFormatContext). Não é thread-safe; use uma instância por tarefa.</summary>
internal sealed unsafe class MediaFile : IDisposable
{
    private AVFormatContext* _ctx;

    private MediaFile(AVFormatContext* ctx) => _ctx = ctx;

    public AVFormatContext* Context => _ctx != null ? _ctx : throw new ObjectDisposedException(nameof(MediaFile));

    public static MediaFile Open(string path, string? inputFormat = null, IReadOnlyDictionary<string, string>? options = null)
    {
        FFmpegRuntime.EnsureInitialized();

        AVInputFormat* fmt = null;
        if (inputFormat is not null)
        {
            fmt = ffmpeg.av_find_input_format(inputFormat);
            if (fmt is null) throw new FFmpegException(ffmpeg.AVERROR_DEMUXER_NOT_FOUND, $"formato {inputFormat}");
        }

        AVDictionary* dict = null;
        if (options is not null)
            foreach (var (k, v) in options) ffmpeg.av_dict_set(&dict, k, v, 0);

        AVFormatContext* ctx = null;
        try
        {
            Av.Check(ffmpeg.avformat_open_input(&ctx, path, fmt, &dict), "abrir arquivo");
        }
        finally
        {
            ffmpeg.av_dict_free(&dict);
        }

        var file = new MediaFile(ctx);
        try
        {
            Av.Check(ffmpeg.avformat_find_stream_info(ctx, null), "analisar faixas");
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public int StreamCount => (int)Context->nb_streams;

    public AVStream* Stream(int index) => Context->streams[index];

    /// <summary>Melhor faixa do tipo pedido; para vídeo, ignora capas de álbum (attached pictures).</summary>
    public int BestStream(AVMediaType type)
    {
        var index = ffmpeg.av_find_best_stream(Context, type, -1, -1, null, 0);
        if (index < 0) return -1;
        if (type == AVMediaType.AVMEDIA_TYPE_VIDEO && IsAttachedPicture(index))
        {
            for (var i = 0; i < StreamCount; i++)
                if (Stream(i)->codecpar->codec_type == type && !IsAttachedPicture(i)) return i;
            return -1;
        }
        return index;
    }

    public bool IsAttachedPicture(int index) => (Stream(index)->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0;

    public void Dispose()
    {
        if (_ctx == null) return;
        var ctx = _ctx;
        ffmpeg.avformat_close_input(&ctx);
        _ctx = null;
    }
}
