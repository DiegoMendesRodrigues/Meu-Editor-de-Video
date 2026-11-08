using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Lê quadros de vídeo e os converte para BGRA (miniaturas, verificação de integridade).</summary>
internal sealed unsafe class VideoFrameReader : StreamDecoder
{
    private SwsContext* _sws;
    private (int W, int H, AVPixelFormat F, int DW, int DH) _swsKey;

    public VideoFrameReader(MediaFile file, int streamIndex) : base(file, streamIndex) { }

    /// <summary>Decodifica o primeiro quadro a partir de <paramref name="at"/> (ou o último disponível, se passar do fim).</summary>
    public bool DecodeAt(MediaTime at)
    {
        var tb = Stream->time_base;
        var start = Stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : Stream->start_time;
        var target = start + ffmpeg.av_rescale_q(Av.ToMicroseconds(at), Av.TimeBaseQ, tb);

        if (at > MediaTime.Zero) Seek(target);

        var got = false;
        while (ReadFrame())
        {
            got = true;
            var pts = Frame->best_effort_timestamp;
            if (pts == ffmpeg.AV_NOPTS_VALUE || pts >= target) return true;
            KeepCopy();
        }

        // Fim do arquivo antes do alvo: usa o último quadro decodificado.
        return got && RestoreCopy();
    }

    private AVFrame* _last;

    private void KeepCopy()
    {
        if (_last == null) _last = ffmpeg.av_frame_alloc();
        else ffmpeg.av_frame_unref(_last);
        ffmpeg.av_frame_ref(_last, Frame);
    }

    private bool RestoreCopy()
    {
        if (_last == null || _last->width == 0) return false;
        ffmpeg.av_frame_unref(Frame);
        ffmpeg.av_frame_ref(Frame, _last);
        return true;
    }

    /// <summary>Converte o quadro atual para BGRA com largura máxima, respeitando proporção de pixel e rotação.</summary>
    public BgraImage ToBgra(int maxWidth, int rotation)
    {
        var f = Frame;
        int w = f->width, h = f->height;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Quadro sem dimensões.");

        // Largura de exibição considerando pixels não quadrados.
        var sar = f->sample_aspect_ratio;
        var displayW = sar.num > 0 && sar.den > 0 ? w * (double)sar.num / sar.den : w;

        // Após girar 90/270 a largura final é a altura do quadro.
        var swapped = rotation is 90 or 270;
        var finalW = swapped ? h : displayW;
        var scale = Math.Min(1.0, maxWidth / finalW);
        var dw = Math.Max(2, (int)Math.Round(displayW * scale));
        var dh = Math.Max(2, (int)Math.Round(h * scale));

        var key = (w, h, (AVPixelFormat)f->format, dw, dh);
        if (_sws == null || key != _swsKey)
        {
            if (_sws != null) ffmpeg.sws_freeContext(_sws);
            _sws = ffmpeg.sws_getContext(w, h, (AVPixelFormat)f->format, dw, dh, AVPixelFormat.AV_PIX_FMT_BGRA,
                (int)(SwsFlags.SWS_AREA | SwsFlags.SWS_ACCURATE_RND), null, null, null);
            if (_sws == null) throw new InvalidOperationException("Conversão de cor não suportada para este vídeo.");
            _swsKey = key;
        }

        var pixels = new byte[dw * dh * 4];
        fixed (byte* dst = pixels)
        {
            var dstData = new byte*[] { dst, null, null, null };
            var dstStride = new[] { dw * 4, 0, 0, 0 };
            var srcData = new byte*[] { f->data[0], f->data[1], f->data[2], f->data[3] };
            var srcStride = new[] { f->linesize[0], f->linesize[1], f->linesize[2], f->linesize[3] };
            Av.Check(ffmpeg.sws_scale(_sws, srcData, srcStride, 0, h, dstData, dstStride), "converter quadro");
        }

        var image = new BgraImage(dw, dh, pixels);
        return rotation == 0 ? image : Rotate(image, rotation);
    }

    private static BgraImage Rotate(BgraImage src, int rotation)
    {
        int w = src.Width, h = src.Height;
        var (nw, nh) = rotation == 180 ? (w, h) : (h, w);
        var dst = new byte[src.Pixels.Length];
        var s = src.Pixels;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (nx, ny) = rotation switch
                {
                    90 => (h - 1 - y, x),
                    180 => (w - 1 - x, h - 1 - y),
                    _ => (y, w - 1 - x), // 270
                };
                Buffer.BlockCopy(s, (y * w + x) * 4, dst, (ny * nw + nx) * 4, 4);
            }
        }
        return new BgraImage(nw, nh, dst);
    }

    public override void Dispose()
    {
        if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
        if (_last != null) { var l = _last; ffmpeg.av_frame_free(&l); _last = null; }
        base.Dispose();
    }
}
