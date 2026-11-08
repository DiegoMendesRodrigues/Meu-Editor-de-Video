using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Native;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine.Playback;

public sealed class AudioSampleSourceFactory(ILoggerFactory loggers) : IAudioSampleSourceFactory
{
    public IAudioSampleSource Create() => new AudioSampleSource(loggers.CreateLogger<AudioSampleSource>());
}

/// <summary>Amostras de áudio (estéreo float 48 kHz) por mídia, com leitura contínua sem busca durante a reprodução.</summary>
public sealed class AudioSampleSource(ILogger<AudioSampleSource> logger) : IAudioSampleSource
{
    private readonly Dictionary<Guid, (string Path, PlaybackAudioReader Reader, MediaFile File)> _readers = [];
    private readonly HashSet<Guid> _failed = [];

    public int Read(MediaAsset asset, string path, MediaTime sourceTime, Span<float> destination)
    {
        if (!asset.HasAudio || _failed.Contains(asset.Id)) return 0;
        try
        {
            if (!_readers.TryGetValue(asset.Id, out var entry) || entry.Path != path)
            {
                if (entry.Reader is not null) { entry.Reader.Dispose(); entry.File.Dispose(); }
                var file = MediaFile.Open(path);
                var stream = file.BestStream(AVMediaType.AVMEDIA_TYPE_AUDIO);
                if (stream < 0)
                {
                    file.Dispose();
                    _failed.Add(asset.Id);
                    return 0;
                }
                entry = (path, new PlaybackAudioReader(file, stream), file);
                _readers[asset.Id] = entry;
            }
            return entry.Reader.Read(sourceTime, destination);
        }
        catch (Exception ex) when (ex is FFmpegException or DecoderNotFoundException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Falha ao ler o áudio de {Name} para a reprodução.", asset.Name);
            _failed.Add(asset.Id);
            if (_readers.Remove(asset.Id, out var broken)) { broken.Reader.Dispose(); broken.File.Dispose(); }
            return 0;
        }
    }

    public void Trim(IReadOnlySet<Guid> inUse)
    {
        foreach (var id in _readers.Keys.Where(k => !inUse.Contains(k)).ToList())
        {
            var (_, reader, file) = _readers[id];
            reader.Dispose();
            file.Dispose();
            _readers.Remove(id);
        }
        _failed.IntersectWith(inUse);
    }

    public void Dispose()
    {
        foreach (var (_, reader, file) in _readers.Values)
        {
            reader.Dispose();
            file.Dispose();
        }
        _readers.Clear();
    }
}

/// <summary>
/// Decodifica uma faixa de áudio para estéreo float 48 kHz, mantendo um buffer com a posição (em quadros de 48 kHz)
/// das amostras já convertidas. Pedidos contíguos continuam a decodificação; saltos fazem nova busca.
/// </summary>
internal sealed unsafe class PlaybackAudioReader : StreamDecoder
{
    private const int Ch = AudioFormat.Channels;
    private SwrContext* _swr;
    private float[] _buffer = new float[48000 * Ch];
    private long _bufferStart = -1; // posição (quadros) de _buffer[0]; -1 = desconhecida
    private int _bufferFrames;
    private bool _eof;
    private float[] _convert = new float[8192 * Ch];

    public PlaybackAudioReader(MediaFile file, int streamIndex) : base(file, streamIndex, threads: 1)
    {
        AVChannelLayout stereo;
        ffmpeg.av_channel_layout_default(&stereo, Ch);
        SwrContext* swr = null;
        Av.Check(ffmpeg.swr_alloc_set_opts2(&swr,
            &stereo, AVSampleFormat.AV_SAMPLE_FMT_FLT, AudioFormat.SampleRate,
            &Codec->ch_layout, Codec->sample_fmt, Codec->sample_rate, 0, null), "configurar conversão de áudio");
        _swr = swr;
        Av.Check(ffmpeg.swr_init(_swr), "iniciar conversão de áudio");
    }

    public int Read(MediaTime sourceTime, Span<float> destination)
    {
        var wanted = destination.Length / Ch;
        var position = AudioFormat.ToFrames(sourceTime);
        if (position < 0) position = 0;

        // Fora do que está (ou estará) no buffer: reposiciona. Tolerância de 20 ms para evitar buscas por arredondamento.
        var tolerance = AudioFormat.SampleRate / 50;
        var bufferEnd = _bufferStart + _bufferFrames;
        if (_bufferStart < 0 || position < _bufferStart - tolerance || position > bufferEnd + tolerance)
            Reposition(sourceTime, position);
        else if (Math.Abs(position - bufferEnd) <= tolerance && position >= bufferEnd)
            position = Math.Min(position, bufferEnd); // continua exatamente de onde parou

        while (!_eof && (_bufferStart < 0 || _bufferStart + _bufferFrames < position + wanted)) DecodeMore();

        if (_bufferStart < 0) return 0;
        var offset = position - _bufferStart;
        if (offset < 0)
        {
            // Pedido começa um pouco antes do buffer (após busca): preenche com silêncio.
            var silence = (int)Math.Min(-offset, wanted);
            destination[..(silence * Ch)].Clear();
            var copied = CopyOut(0, destination[(silence * Ch)..], wanted - silence);
            return silence + copied;
        }
        return CopyOut((int)offset, destination, wanted);
    }

    private int CopyOut(int offsetFrames, Span<float> destination, int wanted)
    {
        var available = Math.Max(0, _bufferFrames - offsetFrames);
        var n = Math.Min(available, wanted);
        _buffer.AsSpan(offsetFrames * Ch, n * Ch).CopyTo(destination);

        // Descarta o que já foi entregue, mantendo 100 ms para pequenas voltas.
        var consumed = offsetFrames + n - AudioFormat.SampleRate / 10;
        if (consumed > 0)
        {
            Array.Copy(_buffer, consumed * Ch, _buffer, 0, (_bufferFrames - consumed) * Ch);
            _bufferFrames -= consumed;
            _bufferStart += consumed;
        }
        return n;
    }

    private void Reposition(MediaTime sourceTime, long position)
    {
        var tb = Stream->time_base;
        var start = Stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : Stream->start_time;
        Seek(start + ffmpeg.av_rescale_q(Av.ToMicroseconds(sourceTime), Av.TimeBaseQ, tb));
        ffmpeg.swr_close(_swr);
        Av.Check(ffmpeg.swr_init(_swr), "reiniciar conversão de áudio");
        _bufferStart = -1;
        _bufferFrames = 0;
        _eof = false;

        // Decodifica até alcançar a posição e descarta o que vem antes dela.
        while (!_eof && (_bufferStart < 0 || _bufferStart + _bufferFrames <= position)) DecodeMore();
        if (_bufferStart >= 0 && position > _bufferStart)
        {
            var drop = (int)Math.Min(position - _bufferStart, _bufferFrames);
            Array.Copy(_buffer, drop * Ch, _buffer, 0, (_bufferFrames - drop) * Ch);
            _bufferFrames -= drop;
            _bufferStart += drop;
        }
    }

    private void DecodeMore()
    {
        if (!ReadFrame())
        {
            _eof = true;
            return;
        }

        var frame = Frame;
        if (_bufferStart < 0)
        {
            var tb = Stream->time_base;
            var t = Av.StreamToTime(frame->best_effort_timestamp, tb, Stream->start_time);
            _bufferStart = Math.Max(0, AudioFormat.ToFrames(t));
        }

        var needed = (int)ffmpeg.swr_get_out_samples(_swr, frame->nb_samples);
        if (needed * Ch > _convert.Length) _convert = new float[needed * Ch];
        int converted;
        fixed (float* outPtr = _convert)
        {
            var outs = (byte*)outPtr;
            converted = Av.Check(ffmpeg.swr_convert(_swr, &outs, _convert.Length / Ch, frame->extended_data, frame->nb_samples), "converter áudio");
        }

        if ((_bufferFrames + converted) * Ch > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, (_bufferFrames + converted) * Ch));
        Array.Copy(_convert, 0, _buffer, _bufferFrames * Ch, converted * Ch);
        _bufferFrames += converted;
    }

    public override void Dispose()
    {
        if (_swr != null) { var s = _swr; ffmpeg.swr_free(&s); _swr = null; }
        base.Dispose();
    }
}
