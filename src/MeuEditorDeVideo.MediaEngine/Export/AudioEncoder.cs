using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Export;

/// <summary>
/// Codificador de áudio: recebe a mixagem (estéreo float 48 kHz intercalado), converte com swr para o formato,
/// taxa e canais do codificador, agrupa em quadros do tamanho exigido e entrega os pacotes.
/// </summary>
internal sealed unsafe class AudioEncoder : IDisposable
{
    private AVCodecContext* _ctx;
    private SwrContext* _swr;
    private AVAudioFifo* _fifo;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private byte** _convert;
    private int _convertCapacity;
    private long _pts;

    public static string EncoderNameFor(AudioCodec codec, ExportContainer container) => codec switch
    {
        AudioCodec.Aac => "aac",
        AudioCodec.Opus => "libopus",
        AudioCodec.Flac => "flac",
        AudioCodec.Pcm => container == ExportContainer.Mov ? "pcm_s24le" : "pcm_s16le",
        AudioCodec.Vorbis => "libvorbis",
        AudioCodec.Mp3 => "libmp3lame",
        AudioCodec.Mp2 => "mp2",
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };

    public AudioEncoder(ExportSettings settings, bool globalHeader)
    {
        FFmpegRuntime.EnsureInitialized();
        EncoderName = EncoderNameFor(settings.AudioCodec, settings.Container);
        var codec = ffmpeg.avcodec_find_encoder_by_name(EncoderName);
        if (codec is null) throw new EncoderSetupException($"Codificador de áudio \"{EncoderName}\" ausente.");

        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        try
        {
            var channels = settings.AudioCodec is AudioCodec.Mp2 or AudioCodec.Mp3 ? Math.Min(2, settings.AudioChannels) : settings.AudioChannels;
            ffmpeg.av_channel_layout_default(&_ctx->ch_layout, channels);
            _ctx->sample_rate = ChooseSampleRate(_ctx, codec, settings.SampleRate);
            _ctx->sample_fmt = ChooseSampleFormat(_ctx, codec);
            _ctx->time_base = new AVRational { num = 1, den = _ctx->sample_rate };
            if (ExportFormats.HasAudioBitrate(settings.AudioCodec)) _ctx->bit_rate = settings.AudioBitrateKbps * 1000L;
            if (globalHeader) _ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            AVDictionary* opts = null;
            if (settings.AudioCodec == AudioCodec.Opus && channels > 2) ffmpeg.av_dict_set(&opts, "mapping_family", "1", 0);
            try
            {
                Av.Check(ffmpeg.avcodec_open2(_ctx, codec, &opts), $"abrir codificador {EncoderName}");
            }
            finally
            {
                ffmpeg.av_dict_free(&opts);
            }

            AVChannelLayout stereo;
            ffmpeg.av_channel_layout_default(&stereo, AudioFormat.Channels);
            SwrContext* swr = null;
            Av.Check(ffmpeg.swr_alloc_set_opts2(&swr, &_ctx->ch_layout, _ctx->sample_fmt, _ctx->sample_rate,
                &stereo, AVSampleFormat.AV_SAMPLE_FMT_FLT, AudioFormat.SampleRate, 0, null), "configurar conversão de áudio");
            _swr = swr;
            Av.Check(ffmpeg.swr_init(_swr), "iniciar conversão de áudio");

            FrameSize = _ctx->frame_size > 0 && (codec->capabilities & ffmpeg.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) == 0 ? _ctx->frame_size : 1024;
            _fifo = ffmpeg.av_audio_fifo_alloc(_ctx->sample_fmt, _ctx->ch_layout.nb_channels, FrameSize * 4);
            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
            SmallLastFrame = (codec->capabilities & (ffmpeg.AV_CODEC_CAP_SMALL_LAST_FRAME | ffmpeg.AV_CODEC_CAP_VARIABLE_FRAME_SIZE)) != 0;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string EncoderName { get; }

    public int FrameSize { get; }

    private bool SmallLastFrame { get; }

    public AVCodecContext* Context => _ctx;

    public AVRational TimeBase => _ctx->time_base;

    public int SampleRate => _ctx->sample_rate;

    private static int ChooseSampleRate(AVCodecContext* c, AVCodec* codec, int wanted)
    {
        void* configs = null;
        var count = 0;
        if (ffmpeg.avcodec_get_supported_config(c, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_RATE, 0, &configs, &count) < 0 || configs == null || count == 0)
            return wanted;
        var rates = new Span<int>(configs, count).ToArray();
        if (rates.Contains(wanted)) return wanted;
        if (rates.Contains(48_000)) return 48_000;
        return rates.OrderBy(r => Math.Abs(r - wanted)).First();
    }

    private static AVSampleFormat ChooseSampleFormat(AVCodecContext* c, AVCodec* codec)
    {
        void* configs = null;
        var count = 0;
        if (ffmpeg.avcodec_get_supported_config(c, codec, AVCodecConfig.AV_CODEC_CONFIG_SAMPLE_FORMAT, 0, &configs, &count) < 0 || configs == null || count == 0)
            return AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        var formats = new Span<AVSampleFormat>(configs, count).ToArray();
        foreach (var f in new[] { AVSampleFormat.AV_SAMPLE_FMT_FLTP, AVSampleFormat.AV_SAMPLE_FMT_FLT, AVSampleFormat.AV_SAMPLE_FMT_S32, AVSampleFormat.AV_SAMPLE_FMT_S16, AVSampleFormat.AV_SAMPLE_FMT_S16P, AVSampleFormat.AV_SAMPLE_FMT_S32P })
            if (formats.Contains(f)) return f;
        return formats[0];
    }

    /// <summary>Envia <paramref name="frames"/> quadros de áudio estéreo intercalados (48 kHz).</summary>
    public void Write(ReadOnlySpan<float> interleaved, PacketSink sink)
    {
        var frames = interleaved.Length / AudioFormat.Channels;
        if (frames == 0) return;
        fixed (float* p = interleaved)
        {
            var input = (byte*)p;
            Convert(&input, frames);
        }
        Drain(sink, final: false);
    }

    private void Convert(byte** input, int frames)
    {
        var outCount = (int)ffmpeg.swr_get_out_samples(_swr, frames) + 32;
        EnsureConvertCapacity(outCount);
        var got = Av.Check(ffmpeg.swr_convert(_swr, _convert, _convertCapacity, input, frames), "converter áudio");
        if (got > 0) Av.Check(ffmpeg.av_audio_fifo_write(_fifo, (void**)_convert, got), "armazenar áudio");
    }

    private void EnsureConvertCapacity(int samples)
    {
        if (_convert != null && _convertCapacity >= samples) return;
        if (_convert != null)
        {
            ffmpeg.av_freep(&_convert[0]);
            var old = _convert;
            ffmpeg.av_freep(&old);
            _convert = null;
        }
        byte** data = null;
        var linesize = 0;
        Av.Check(ffmpeg.av_samples_alloc_array_and_samples(&data, &linesize, _ctx->ch_layout.nb_channels, samples, _ctx->sample_fmt, 0), "alocar áudio");
        _convert = data;
        _convertCapacity = samples;
    }

    private void Drain(PacketSink sink, bool final)
    {
        while (ffmpeg.av_audio_fifo_size(_fifo) >= FrameSize || (final && ffmpeg.av_audio_fifo_size(_fifo) > 0))
        {
            var available = ffmpeg.av_audio_fifo_size(_fifo);
            var n = Math.Min(FrameSize, available);
            ffmpeg.av_frame_unref(_frame);
            _frame->nb_samples = n < FrameSize && !SmallLastFrame ? FrameSize : n;
            _frame->format = (int)_ctx->sample_fmt;
            _frame->sample_rate = _ctx->sample_rate;
            Av.Check(ffmpeg.av_channel_layout_copy(&_frame->ch_layout, &_ctx->ch_layout), "copiar canais");
            Av.Check(ffmpeg.av_frame_get_buffer(_frame, 0), "alocar quadro de áudio");
            if (_frame->nb_samples > n)
                ffmpeg.av_samples_set_silence(_frame->extended_data, 0, _frame->nb_samples, _ctx->ch_layout.nb_channels, _ctx->sample_fmt);
            Av.Check(ffmpeg.av_audio_fifo_read(_fifo, (void**)_frame->extended_data, n), "ler áudio");
            _frame->pts = _pts;
            _pts += _frame->nb_samples;
            Av.Check(ffmpeg.avcodec_send_frame(_ctx, _frame), "codificar áudio");
            Receive(sink);
        }
    }

    private void Receive(PacketSink sink)
    {
        while (true)
        {
            var r = ffmpeg.avcodec_receive_packet(_ctx, _packet);
            if (r == Av.EAgain || r == ffmpeg.AVERROR_EOF) return;
            Av.Check(r, "receber pacote de áudio");
            sink(_packet);
            ffmpeg.av_packet_unref(_packet);
        }
    }

    /// <summary>Esvazia a conversão, completa o último quadro e finaliza o codificador.</summary>
    public void Flush(PacketSink sink)
    {
        Convert(null, 0);
        Drain(sink, final: true);
        var r = ffmpeg.avcodec_send_frame(_ctx, null);
        if (r != ffmpeg.AVERROR_EOF) Av.Check(r, "finalizar áudio");
        Receive(sink);
    }

    public void Dispose()
    {
        if (_convert != null)
        {
            ffmpeg.av_freep(&_convert[0]);
            var c = _convert;
            ffmpeg.av_freep(&c);
            _convert = null;
        }
        if (_fifo != null) { ffmpeg.av_audio_fifo_free(_fifo); _fifo = null; }
        if (_swr != null) { var s = _swr; ffmpeg.swr_free(&s); _swr = null; }
        if (_frame != null) { var f = _frame; ffmpeg.av_frame_free(&f); _frame = null; }
        if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
        if (_ctx != null) { var x = _ctx; ffmpeg.avcodec_free_context(&x); _ctx = null; }
    }
}
