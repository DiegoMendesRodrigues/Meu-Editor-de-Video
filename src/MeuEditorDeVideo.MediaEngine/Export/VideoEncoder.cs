using System.Globalization;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Export;

/// <summary>Parâmetros já resolvidos do vídeo de saída (resolução, taxa de quadros e taxa de bits efetiva).</summary>
public sealed record VideoEncodeSpec(ExportSettings Settings, int Width, int Height, FrameRate FrameRate, int VideoBitrateKbps)
{
    /// <summary>Intervalo entre quadros-chave (nulo = ~2 s). Proxies usam GOP curto para busca rápida.</summary>
    public int? GopSize { get; init; }

    /// <summary>Predefinição rápida do codificador (proxies: velocidade importa mais que eficiência).</summary>
    public bool FastPreset { get; init; }

    public bool Alpha => Settings.Alpha && ExportFormats.SupportsAlpha(Settings.Container, Settings.VideoCodec);
}

/// <summary>Pacote codificado entregue pelo codificador (ponteiro válido apenas durante a chamada).</summary>
internal unsafe delegate void PacketSink(AVPacket* packet);

/// <summary>
/// Codificador de vídeo: converte quadros BGRA para o formato do codificador (sws, BT.709) — ou para paleta,
/// no GIF (palettegen/paletteuse por quadro) — e entrega os pacotes.
/// </summary>
internal sealed unsafe class VideoEncoder : IDisposable
{
    private AVCodecContext* _ctx;
    private AVFrame* _frame;
    private AVFrame* _bgra;
    private AVPacket* _packet;
    private SwsContext* _sws;
    private AVFilterGraph* _graph;
    private AVFilterContext* _src;
    private AVFilterContext* _sink;
    private AVFrame* _filtered;

    public VideoEncoder(VideoEncodeSpec spec, string encoderName, bool globalHeader)
    {
        FFmpegRuntime.EnsureInitialized();
        var codec = ffmpeg.avcodec_find_encoder_by_name(encoderName);
        if (codec is null) throw new EncoderSetupException($"Codificador \"{encoderName}\" ausente.");
        EncoderName = encoderName;
        IsGif = spec.Settings.VideoCodec == VideoCodec.Gif;

        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        try
        {
            Configure(_ctx, codec, spec, encoderName, globalHeader);
            AVDictionary* opts = null;
            ApplyOptions(_ctx, spec, encoderName, &opts);
            try
            {
                Av.Check(ffmpeg.avcodec_open2(_ctx, codec, &opts), $"abrir codificador {encoderName}");
            }
            finally
            {
                ffmpeg.av_dict_free(&opts);
            }

            _packet = ffmpeg.av_packet_alloc();
            _frame = ffmpeg.av_frame_alloc();
            if (IsGif)
            {
                SetupPaletteGraph(spec);
                _filtered = ffmpeg.av_frame_alloc();
                _bgra = ffmpeg.av_frame_alloc();
            }
            else
            {
                _frame->format = (int)_ctx->pix_fmt;
                _frame->width = _ctx->width;
                _frame->height = _ctx->height;
                Av.Check(ffmpeg.av_frame_get_buffer(_frame, 0), "alocar quadro");
                _sws = ffmpeg.sws_getContext(spec.Width, spec.Height, AVPixelFormat.AV_PIX_FMT_BGRA,
                    _ctx->width, _ctx->height, _ctx->pix_fmt, (int)SwsFlags.SWS_BICUBIC, null, null, null);
                if (_sws == null) throw new InvalidOperationException("Conversão de cor não suportada pelo codificador.");
                if (!IsRgb(_ctx->pix_fmt))
                {
                    var dstFull = _ctx->color_range == AVColorRange.AVCOL_RANGE_JPEG ? 1 : 0;
                    var coef = *(int_array4*)ffmpeg.sws_getCoefficients(ffmpeg.SWS_CS_ITU709);
                    ffmpeg.sws_setColorspaceDetails(_sws, coef, 1, coef, dstFull, 0, 1 << 16, 1 << 16);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string EncoderName { get; }

    public bool IsGif { get; }

    public AVCodecContext* Context => _ctx;

    public AVRational TimeBase => _ctx->time_base;

    // ------------------------------------------------------------------ configuração

    private static void Configure(AVCodecContext* c, AVCodec* codec, VideoEncodeSpec spec, string name, bool globalHeader)
    {
        var s = spec.Settings;
        c->width = spec.Width;
        c->height = spec.Height;
        c->time_base = new AVRational { num = spec.FrameRate.Denominator, den = spec.FrameRate.Numerator };
        c->framerate = new AVRational { num = spec.FrameRate.Numerator, den = spec.FrameRate.Denominator };
        c->sample_aspect_ratio = new AVRational { num = 1, den = 1 };
        c->pix_fmt = IsGifName(name) ? AVPixelFormat.AV_PIX_FMT_PAL8 : ChoosePixelFormat(c, codec, spec.Alpha, name);
        c->thread_count = 0; // automático

        if (!IsRgb(c->pix_fmt) && c->pix_fmt != AVPixelFormat.AV_PIX_FMT_PAL8)
        {
            c->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
            c->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
            c->colorspace = AVColorSpace.AVCOL_SPC_BT709;
            c->color_range = c->pix_fmt == AVPixelFormat.AV_PIX_FMT_YUVJ420P || s.VideoCodec == VideoCodec.Mjpeg
                ? AVColorRange.AVCOL_RANGE_JPEG
                : AVColorRange.AVCOL_RANGE_MPEG;
        }

        // GOP de ~2 s (busca rápida nos players); intra para ProRes/FFV1/MJPEG/QuickTime Animation.
        c->gop_size = s.VideoCodec is VideoCodec.ProRes or VideoCodec.Ffv1 or VideoCodec.Mjpeg ? 1 : Math.Max(1, (int)Math.Round(spec.FrameRate.Value * 2));
        if (s.VideoCodec == VideoCodec.Mpeg2) c->gop_size = Math.Min(c->gop_size, 15);
        if (s.Container == ExportContainer.Avi) c->max_b_frames = 0; // AVI não guarda reordenação de quadros
        if (spec.GopSize is { } gop) c->gop_size = Math.Max(1, gop);

        if (ExportFormats.HasBitrate(s.VideoCodec))
        {
            var kbps = (long)spec.VideoBitrateKbps * 1000;
            switch (s.RateControl)
            {
                case RateControl.Vbr when s.TargetSizeMegabytes is null:
                    c->bit_rate = kbps;
                    c->rc_max_rate = kbps * 3 / 2;
                    c->rc_buffer_size = (int)Math.Min(int.MaxValue, kbps * 2);
                    break;
                case RateControl.Cbr:
                case RateControl.Vbr: // tamanho alvo: taxa limitada para não estourar o arquivo
                    c->bit_rate = kbps;
                    c->rc_max_rate = kbps;
                    c->rc_min_rate = s.RateControl == RateControl.Cbr ? kbps : 0;
                    c->rc_buffer_size = (int)Math.Min(int.MaxValue, kbps);
                    break;
                case RateControl.ConstantQuality:
                    if (s.VideoCodec is VideoCodec.Mpeg2 or VideoCodec.Mjpeg)
                    {
                        c->flags |= ffmpeg.AV_CODEC_FLAG_QSCALE;
                        c->global_quality = ffmpeg.FF_QP2LAMBDA * (1 + s.Quality * 30 / 51);
                    }
                    break;
            }
        }

        if (globalHeader) c->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
    }

    /// <summary>Opções privadas de cada codificador (predefinição, controle de taxa, perfil).</summary>
    private static void ApplyOptions(AVCodecContext* c, VideoEncodeSpec spec, string name, AVDictionary** opts)
    {
        var s = spec.Settings;
        var cq = s.RateControl == RateControl.ConstantQuality;
        var q = s.Quality.ToString(CultureInfo.InvariantCulture);
        void Set(string key, string value) => ffmpeg.av_dict_set(opts, key, value, 0);

        if (s.Profile is { Length: > 0 } profile && name is "libx264" or "h264_nvenc" or "h264_amf" or "h264_qsv" or "hevc_nvenc" or "libx265")
            Set("profile", profile);

        switch (name)
        {
            case "libx264":
                Set("preset", spec.FastPreset ? "veryfast" : "medium");
                if (cq) Set("crf", q);
                if (s.RateControl == RateControl.Cbr) Set("nal-hrd", "cbr");
                if (s.Container == ExportContainer.Avi) Set("bf", "0");
                break;
            case "libx265":
                Set("preset", "medium");
                if (cq) Set("crf", q);
                Set("x265-params", "log-level=error");
                break;
            case "h264_nvenc" or "hevc_nvenc" or "av1_nvenc":
                Set("preset", "p5");
                Set("tune", "hq");
                Set("rc", s.RateControl == RateControl.Cbr ? "cbr" : "vbr");
                if (cq) { Set("cq", q); c->bit_rate = 0; }
                if (s.Container == ExportContainer.Avi) Set("bf", "0");
                break;
            case "h264_amf" or "hevc_amf" or "av1_amf":
                Set("quality", "balanced");
                Set("rc", cq ? "cqp" : s.RateControl == RateControl.Cbr ? "cbr" : "vbr_peak");
                if (cq) { Set("qp_i", q); Set("qp_p", q); Set("qp_b", q); }
                break;
            case "h264_qsv" or "hevc_qsv" or "av1_qsv" or "vp9_qsv" or "mpeg2_qsv" or "mjpeg_qsv":
                Set("preset", "medium");
                if (cq) c->global_quality = Math.Max(1, s.Quality);
                break;
            case "h264_mf" or "hevc_mf" or "av1_mf":
                Set("rate_control", cq ? "quality" : s.RateControl == RateControl.Cbr ? "cbr" : "u_vbr");
                if (cq) Set("quality", (100 - s.Quality * 2).ToString(CultureInfo.InvariantCulture));
                Set("hw_encoding", "1");
                break;
            case "libsvtav1":
                Set("preset", "8");
                // SVT-AV1 só aceita taxa máxima no modo CRF; VBR/CBR são escolhidos pelo parâmetro rc.
                c->rc_max_rate = 0;
                c->rc_min_rate = 0;
                c->rc_buffer_size = 0;
                if (!cq) Set("svtav1-params", s.RateControl == RateControl.Cbr ? "rc=2" : "rc=1");
                if (cq) Set("crf", Math.Min(63, s.Quality + 10).ToString(CultureInfo.InvariantCulture));
                break;
            case "libaom-av1":
                Set("cpu-used", "6");
                Set("row-mt", "1");
                if (cq) { Set("crf", Math.Min(63, s.Quality + 10).ToString(CultureInfo.InvariantCulture)); c->bit_rate = 0; }
                break;
            case "libvpx-vp9":
                Set("deadline", "good");
                Set("cpu-used", "4");
                Set("row-mt", "1");
                if (cq) { Set("crf", Math.Min(63, s.Quality + 8).ToString(CultureInfo.InvariantCulture)); c->bit_rate = 0; }
                if (spec.Alpha) Set("auto-alt-ref", "0");
                break;
            case "prores_ks":
                Set("profile", spec.Alpha ? "4444" : "hq");
                Set("vendor", "apl0");
                break;
            case "ffv1":
                Set("level", "3");
                Set("slicecrc", "1");
                break;
        }
    }

    private static readonly AVPixelFormat[] OpaquePreference =
    [
        AVPixelFormat.AV_PIX_FMT_YUV420P, AVPixelFormat.AV_PIX_FMT_NV12, AVPixelFormat.AV_PIX_FMT_YUVJ420P,
        AVPixelFormat.AV_PIX_FMT_YUV422P10LE, AVPixelFormat.AV_PIX_FMT_RGB24, AVPixelFormat.AV_PIX_FMT_BGR0,
    ];

    private static readonly AVPixelFormat[] AlphaPreference =
    [
        AVPixelFormat.AV_PIX_FMT_YUVA420P, AVPixelFormat.AV_PIX_FMT_YUVA444P10LE, AVPixelFormat.AV_PIX_FMT_ARGB,
        AVPixelFormat.AV_PIX_FMT_BGRA, AVPixelFormat.AV_PIX_FMT_RGBA,
    ];

    private static AVPixelFormat ChoosePixelFormat(AVCodecContext* c, AVCodec* codec, bool alpha, string name)
    {
        var supported = SupportedPixelFormats(c, codec);
        if (supported.Count == 0) return AVPixelFormat.AV_PIX_FMT_YUV420P;
        foreach (var f in alpha ? AlphaPreference.Concat(OpaquePreference) : OpaquePreference)
            if (supported.Contains(f)) return f;
        return supported[0];
    }

    internal static List<AVPixelFormat> SupportedPixelFormats(AVCodecContext* c, AVCodec* codec)
    {
        var list = new List<AVPixelFormat>();
        void* configs = null;
        var count = 0;
        if (ffmpeg.avcodec_get_supported_config(c, codec, AVCodecConfig.AV_CODEC_CONFIG_PIX_FORMAT, 0, &configs, &count) < 0 || configs == null)
            return list;
        var p = (AVPixelFormat*)configs;
        for (var i = 0; i < count; i++) list.Add(p[i]);
        return list;
    }

    private static bool IsGifName(string name) => name == "gif";

    private static bool IsRgb(AVPixelFormat f) => f is AVPixelFormat.AV_PIX_FMT_RGB24 or AVPixelFormat.AV_PIX_FMT_ARGB or AVPixelFormat.AV_PIX_FMT_BGRA
        or AVPixelFormat.AV_PIX_FMT_RGBA or AVPixelFormat.AV_PIX_FMT_BGR0 or AVPixelFormat.AV_PIX_FMT_RGB555LE or AVPixelFormat.AV_PIX_FMT_PAL8;

    /// <summary>GIF: paleta própria por quadro (streaming) com pontilhado; reserva uma cor para transparência.</summary>
    private void SetupPaletteGraph(VideoEncodeSpec spec)
    {
        _graph = ffmpeg.avfilter_graph_alloc();
        var args = string.Create(CultureInfo.InvariantCulture,
            $"video_size={spec.Width}x{spec.Height}:pix_fmt={(int)AVPixelFormat.AV_PIX_FMT_BGRA}:time_base={_ctx->time_base.num}/{_ctx->time_base.den}:pixel_aspect=1/1");
        AVFilterContext* src;
        AVFilterContext* sink;
        Av.Check(ffmpeg.avfilter_graph_create_filter(&src, ffmpeg.avfilter_get_by_name("buffer"), "in", args, null, _graph), "criar filtro de entrada");
        Av.Check(ffmpeg.avfilter_graph_create_filter(&sink, ffmpeg.avfilter_get_by_name("buffersink"), "out", null, null, _graph), "criar filtro de saída");
        _src = src;
        _sink = sink;

        var outputs = ffmpeg.avfilter_inout_alloc();
        var inputs = ffmpeg.avfilter_inout_alloc();
        outputs->name = ffmpeg.av_strdup("in");
        outputs->filter_ctx = src;
        outputs->pad_idx = 0;
        outputs->next = null;
        inputs->name = ffmpeg.av_strdup("out");
        inputs->filter_ctx = sink;
        inputs->pad_idx = 0;
        inputs->next = null;
        var desc = spec.Alpha
            ? "split[a][b];[a]palettegen=stats_mode=single:reserve_transparent=1[p];[b][p]paletteuse=new=1:alpha_threshold=128"
            : "split[a][b];[a]palettegen=stats_mode=single:reserve_transparent=0[p];[b][p]paletteuse=new=1:dither=sierra2_4a";
        try
        {
            Av.Check(ffmpeg.avfilter_graph_parse_ptr(_graph, desc, &inputs, &outputs, null), "montar paleta do GIF");
            Av.Check(ffmpeg.avfilter_graph_config(_graph, null), "configurar paleta do GIF");
        }
        finally
        {
            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
        }
    }

    // ------------------------------------------------------------------ codificação

    /// <summary>Codifica um quadro com carimbo <paramref name="pts"/> (em quadros, na base de tempo do codificador).</summary>
    public void Encode(BgraImage image, long pts, PacketSink sink)
    {
        if (image.Width != _ctx->width || image.Height != _ctx->height)
            throw new ArgumentException("Quadro com tamanho diferente do codificador.", nameof(image));

        fixed (byte* p = image.Pixels)
        {
            if (IsGif)
            {
                EncodeThroughPalette(p, image, pts, sink);
                return;
            }

            Av.Check(ffmpeg.av_frame_make_writable(_frame), "preparar quadro");
            var srcData = new byte*[] { p, null, null, null };
            var srcStride = new[] { image.Stride, 0, 0, 0 };
            Av.Check(ffmpeg.sws_scale(_sws, srcData, srcStride, 0, image.Height, _frame->data, _frame->linesize), "converter cores");
            _frame->pts = pts;
            Send(_frame, sink);
        }
    }

    private void EncodeThroughPalette(byte* pixels, BgraImage image, long pts, PacketSink sink)
    {
        ffmpeg.av_frame_unref(_bgra);
        _bgra->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        _bgra->width = image.Width;
        _bgra->height = image.Height;
        Av.Check(ffmpeg.av_frame_get_buffer(_bgra, 0), "alocar quadro");
        for (var y = 0; y < image.Height; y++)
            Buffer.MemoryCopy(pixels + y * image.Stride, _bgra->data[0] + y * _bgra->linesize[0], image.Stride, image.Stride);
        _bgra->pts = pts;
        Av.Check(ffmpeg.av_buffersrc_add_frame_flags(_src, _bgra, 0), "enviar quadro à paleta");
        DrainGraph(sink);
    }

    private void DrainGraph(PacketSink sink)
    {
        while (true)
        {
            var r = ffmpeg.av_buffersink_get_frame(_sink, _filtered);
            if (r == Av.EAgain || r == ffmpeg.AVERROR_EOF) return;
            Av.Check(r, "gerar paleta do GIF");
            Send(_filtered, sink);
            ffmpeg.av_frame_unref(_filtered);
        }
    }

    private void Send(AVFrame* frame, PacketSink sink)
    {
        Av.Check(ffmpeg.avcodec_send_frame(_ctx, frame), "codificar quadro");
        Receive(sink);
    }

    private void Receive(PacketSink sink)
    {
        while (true)
        {
            var r = ffmpeg.avcodec_receive_packet(_ctx, _packet);
            if (r == Av.EAgain || r == ffmpeg.AVERROR_EOF) return;
            Av.Check(r, "receber pacote do codificador");
            sink(_packet);
            ffmpeg.av_packet_unref(_packet);
        }
    }

    /// <summary>Esvazia o codificador (quadros atrasados por B-frames/lookahead).</summary>
    public void Flush(PacketSink sink)
    {
        if (IsGif)
        {
            Av.Check(ffmpeg.av_buffersrc_add_frame_flags(_src, null, 0), "finalizar paleta");
            DrainGraph(sink);
        }
        var r = ffmpeg.avcodec_send_frame(_ctx, null);
        if (r != ffmpeg.AVERROR_EOF) Av.Check(r, "finalizar codificador");
        Receive(sink);
    }

    public void Dispose()
    {
        if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
        if (_graph != null) { var g = _graph; ffmpeg.avfilter_graph_free(&g); _graph = null; }
        if (_filtered != null) { var f = _filtered; ffmpeg.av_frame_free(&f); _filtered = null; }
        if (_bgra != null) { var f = _bgra; ffmpeg.av_frame_free(&f); _bgra = null; }
        if (_frame != null) { var f = _frame; ffmpeg.av_frame_free(&f); _frame = null; }
        if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
        if (_ctx != null) { var c = _ctx; ffmpeg.avcodec_free_context(&c); _ctx = null; }
    }
}

/// <summary>Falha interna de configuração do codificador (convertida em mensagem pt-BR pelo motor de renderização).</summary>
public sealed class EncoderSetupException(string message) : InvalidOperationException(message);
