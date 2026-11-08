using FFmpeg.AutoGen;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Sentinela: o codec da faixa não tem decodificador disponível.</summary>
public sealed class DecoderNotFoundException(string codec) : Exception($"Decodificador não disponível para \"{codec}\".")
{
    public string Codec { get; } = codec;
}

/// <summary>Decodificador (software) de uma faixa do arquivo, com leitura de pacotes e envio/recebimento de quadros.</summary>
internal unsafe class StreamDecoder : IDisposable
{
    private AVCodecContext* _codec;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private bool _flushing;

    public StreamDecoder(MediaFile file, int streamIndex, int threads = 2)
    {
        File = file;
        StreamIndex = streamIndex;
        Stream = file.Stream(streamIndex);

        var par = Stream->codecpar;
        var decoder = ffmpeg.avcodec_find_decoder(par->codec_id);
        if (decoder is null) throw new DecoderNotFoundException(Av.CodecName(par->codec_id) ?? "desconhecido");

        _codec = ffmpeg.avcodec_alloc_context3(decoder);
        try
        {
            Av.Check(ffmpeg.avcodec_parameters_to_context(_codec, par), "configurar decodificador");
            _codec->pkt_timebase = Stream->time_base;
            _codec->thread_count = threads;
            Av.Check(ffmpeg.avcodec_open2(_codec, decoder, null), "abrir decodificador");
            _packet = ffmpeg.av_packet_alloc();
            _frame = ffmpeg.av_frame_alloc();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public MediaFile File { get; }

    public int StreamIndex { get; }

    public AVStream* Stream { get; }

    public AVCodecContext* Codec => _codec;

    /// <summary>Quadro decodificado mais recente (válido até a próxima chamada de <see cref="ReadFrame"/>).</summary>
    public AVFrame* Frame => _frame;

    /// <summary>Busca a posição (para trás, no quadro-chave anterior) e limpa o decodificador.</summary>
    public void Seek(long streamTimestamp)
    {
        // Falhas de busca não são fatais (alguns formatos não suportam); a leitura segue do ponto atual.
        ffmpeg.av_seek_frame(File.Context, StreamIndex, streamTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ffmpeg.avcodec_flush_buffers(_codec);
        _flushing = false;
    }

    /// <summary>Decodifica o próximo quadro da faixa. Retorna false no fim do arquivo.</summary>
    public bool ReadFrame()
    {
        ffmpeg.av_frame_unref(_frame);
        while (true)
        {
            var r = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (r == 0) return true;
            if (r == ffmpeg.AVERROR_EOF) return false;
            if (r != Av.EAgain) Av.Check(r, "decodificar");

            if (_flushing) return false;

            // Precisa de mais dados: lê o próximo pacote desta faixa.
            while (true)
            {
                ffmpeg.av_packet_unref(_packet);
                var read = ffmpeg.av_read_frame(File.Context, _packet);
                if (read == ffmpeg.AVERROR_EOF || (read < 0 && File.Context->pb != null && ffmpeg.avio_feof(File.Context->pb) != 0))
                {
                    _flushing = true;
                    Av.Check(ffmpeg.avcodec_send_packet(_codec, null), "finalizar decodificação");
                    break;
                }
                Av.Check(read, "ler pacote");
                if (_packet->stream_index != StreamIndex) continue;

                var sent = ffmpeg.avcodec_send_packet(_codec, _packet);
                if (sent == ffmpeg.AVERROR_INVALIDDATA) continue; // pacote danificado isolado: ignora e segue
                if (sent != Av.EAgain) Av.Check(sent, "enviar pacote");
                break;
            }
        }
    }

    public virtual void Dispose()
    {
        if (_frame != null) { var f = _frame; ffmpeg.av_frame_free(&f); _frame = null; }
        if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
        if (_codec != null) { var c = _codec; ffmpeg.avcodec_free_context(&c); _codec = null; }
        GC.SuppressFinalize(this);
    }
}
