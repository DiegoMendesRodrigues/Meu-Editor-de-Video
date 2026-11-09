using System.Text;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Export;

/// <summary>
/// Trecho de vídeo já codificado (formato interno .medseg): parâmetros do codec + pacotes com carimbos exatos
/// na base de tempo do codificador. Um trecho só vale se tiver o rodapé de conclusão — assim, uma exportação
/// interrompida (queda de energia, fechamento) é retomada a partir do último trecho completo.
/// </summary>
internal static unsafe class SegmentFile
{
    private const string Magic = "MEDSEG01";
    private const int EndMarker = -1;

    public sealed class Header
    {
        public required string EncoderName { get; init; }
        public AVCodecID CodecId { get; init; }
        public int Format { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public AVRational TimeBase { get; init; }
        public AVRational FrameRate { get; init; }
        public long BitRate { get; init; }
        public int Profile { get; init; }
        public int Level { get; init; }
        public int VideoDelay { get; init; }
        public AVColorRange ColorRange { get; init; }
        public AVColorPrimaries ColorPrimaries { get; init; }
        public AVColorTransferCharacteristic ColorTrc { get; init; }
        public AVColorSpace ColorSpace { get; init; }
        public AVChromaLocation ChromaLocation { get; init; }
        public byte[] ExtraData { get; init; } = [];

        public static Header From(AVCodecContext* c, string encoderName) => new()
        {
            EncoderName = encoderName,
            CodecId = c->codec_id,
            Format = (int)c->pix_fmt,
            Width = c->width,
            Height = c->height,
            TimeBase = c->time_base,
            FrameRate = c->framerate,
            BitRate = c->bit_rate,
            Profile = c->profile,
            Level = c->level,
            VideoDelay = c->has_b_frames,
            ColorRange = c->color_range,
            ColorPrimaries = c->color_primaries,
            ColorTrc = c->color_trc,
            ColorSpace = c->colorspace,
            ChromaLocation = c->chroma_sample_location,
            ExtraData = c->extradata_size > 0 ? new ReadOnlySpan<byte>(c->extradata, c->extradata_size).ToArray() : [],
        };

        /// <summary>Preenche os parâmetros de uma faixa de saída.</summary>
        public void ApplyTo(AVCodecParameters* p)
        {
            p->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            p->codec_id = CodecId;
            p->codec_tag = 0;
            p->format = Format;
            p->width = Width;
            p->height = Height;
            p->bit_rate = BitRate;
            p->profile = Profile;
            p->level = Level;
            p->video_delay = VideoDelay;
            p->color_range = ColorRange;
            p->color_primaries = ColorPrimaries;
            p->color_trc = ColorTrc;
            p->color_space = ColorSpace;
            p->chroma_location = ChromaLocation;
            p->sample_aspect_ratio = new AVRational { num = 1, den = 1 };
            p->framerate = FrameRate;
            if (ExtraData.Length > 0)
            {
                p->extradata = (byte*)ffmpeg.av_mallocz((ulong)(ExtraData.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
                ExtraData.CopyTo(new Span<byte>(p->extradata, ExtraData.Length));
                p->extradata_size = ExtraData.Length;
            }
        }
    }

    public sealed class Writer : IDisposable
    {
        private readonly FileStream _stream;
        private readonly BinaryWriter _w;
        private bool _completed;

        public Writer(string path, Header header)
        {
            Path = path;
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            _w = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
            _w.Write(Encoding.ASCII.GetBytes(Magic));
            _w.Write(header.EncoderName);
            _w.Write((int)header.CodecId);
            _w.Write(header.Format);
            _w.Write(header.Width);
            _w.Write(header.Height);
            _w.Write(header.TimeBase.num); _w.Write(header.TimeBase.den);
            _w.Write(header.FrameRate.num); _w.Write(header.FrameRate.den);
            _w.Write(header.BitRate);
            _w.Write(header.Profile);
            _w.Write(header.Level);
            _w.Write(header.VideoDelay);
            _w.Write((int)header.ColorRange);
            _w.Write((int)header.ColorPrimaries);
            _w.Write((int)header.ColorTrc);
            _w.Write((int)header.ColorSpace);
            _w.Write((int)header.ChromaLocation);
            _w.Write(header.ExtraData.Length);
            _w.Write(header.ExtraData);
        }

        public string Path { get; }

        public long Packets { get; private set; }

        public void Write(AVPacket* p)
        {
            _w.Write(p->size);
            _w.Write(p->pts);
            _w.Write(p->dts);
            _w.Write(p->duration);
            _w.Write(p->flags);
            _w.Write(new ReadOnlySpan<byte>(p->data, p->size));
            _w.Write(p->side_data_elems);
            for (var i = 0; i < p->side_data_elems; i++)
            {
                var sd = p->side_data[i];
                _w.Write((int)sd.type);
                _w.Write((int)sd.size);
                _w.Write(new ReadOnlySpan<byte>(sd.data, (int)sd.size));
            }
            Packets++;
        }

        /// <summary>Grava o rodapé e força a gravação em disco: a partir daqui o trecho é reaproveitável.</summary>
        public void Complete(long frames)
        {
            _w.Write(EndMarker);
            _w.Write(Packets);
            _w.Write(frames);
            _w.Flush();
            _stream.Flush(flushToDisk: true);
            _completed = true;
        }

        public void Dispose()
        {
            _w.Dispose();
            _stream.Dispose();
            if (!_completed)
            {
                try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static Header ReadHeader(BinaryReader r)
    {
        var magic = Encoding.ASCII.GetString(r.ReadBytes(Magic.Length));
        if (magic != Magic) throw new InvalidDataException("Trecho de renderização inválido.");
        return new Header
        {
            EncoderName = r.ReadString(),
            CodecId = (AVCodecID)r.ReadInt32(),
            Format = r.ReadInt32(),
            Width = r.ReadInt32(),
            Height = r.ReadInt32(),
            TimeBase = new AVRational { num = r.ReadInt32(), den = r.ReadInt32() },
            FrameRate = new AVRational { num = r.ReadInt32(), den = r.ReadInt32() },
            BitRate = r.ReadInt64(),
            Profile = r.ReadInt32(),
            Level = r.ReadInt32(),
            VideoDelay = r.ReadInt32(),
            ColorRange = (AVColorRange)r.ReadInt32(),
            ColorPrimaries = (AVColorPrimaries)r.ReadInt32(),
            ColorTrc = (AVColorTransferCharacteristic)r.ReadInt32(),
            ColorSpace = (AVColorSpace)r.ReadInt32(),
            ChromaLocation = (AVChromaLocation)r.ReadInt32(),
            ExtraData = r.ReadBytes(r.ReadInt32()),
        };
    }

    /// <summary>Lê o cabeçalho de um trecho; null se não existir, estiver incompleto ou danificado.</summary>
    public static Header? TryReadComplete(string path, out long frames)
    {
        frames = 0;
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 20) return null;
            using var r = new BinaryReader(fs, Encoding.UTF8);
            var header = ReadHeader(r);
            fs.Seek(-(4 + 8 + 8), SeekOrigin.End);
            if (r.ReadInt32() != EndMarker) return null;
            r.ReadInt64();
            frames = r.ReadInt64();
            return header;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Percorre os pacotes de um trecho completo; o pacote só é válido durante a chamada.</summary>
    public static void ReadPackets(string path, Action<IntPtr> onPacket, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var r = new BinaryReader(fs, Encoding.UTF8);
        ReadHeader(r);
        var pkt = ffmpeg.av_packet_alloc();
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var size = r.ReadInt32();
                if (size == EndMarker) return;
                Av.Check(ffmpeg.av_new_packet(pkt, size), "alocar pacote");
                pkt->pts = r.ReadInt64();
                pkt->dts = r.ReadInt64();
                pkt->duration = r.ReadInt64();
                pkt->flags = r.ReadInt32();
                r.BaseStream.ReadExactly(new Span<byte>(pkt->data, size));
                var sideCount = r.ReadInt32();
                for (var i = 0; i < sideCount; i++)
                {
                    var type = (AVPacketSideDataType)r.ReadInt32();
                    var len = r.ReadInt32();
                    var dst = ffmpeg.av_packet_new_side_data(pkt, type, (ulong)len);
                    if (dst == null) throw new OutOfMemoryException();
                    r.BaseStream.ReadExactly(new Span<byte>(dst, len));
                }
                onPacket((IntPtr)pkt);
                ffmpeg.av_packet_unref(pkt);
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&pkt);
        }
    }
}

/// <summary>Codifica um trecho da renderização em arquivo .medseg (usado pelo motor de renderização).</summary>
public sealed unsafe class VideoSegmentEncoder : IDisposable
{
    private readonly VideoEncoder _encoder;
    private readonly SegmentFile.Writer _writer;
    private long _next;
    private long _frames;

    public VideoSegmentEncoder(string path, VideoEncodeSpec spec, string encoderName, long firstFrame)
    {
        _encoder = new VideoEncoder(spec, encoderName, ExportMuxer.NeedsGlobalHeader(spec.Settings.Container));
        try
        {
            _writer = new SegmentFile.Writer(path, SegmentFile.Header.From(_encoder.Context, encoderName));
        }
        catch
        {
            _encoder.Dispose();
            throw;
        }
        _next = firstFrame;
    }

    public string EncoderName => _encoder.EncoderName;

    public void Write(BgraImage frame)
    {
        _encoder.Encode(frame, _next++, p => _writer.Write(p));
        _frames++;
    }

    public void Complete()
    {
        _encoder.Flush(p => _writer.Write(p));
        _writer.Complete(_frames);
    }

    /// <summary>Quadros de um trecho completo, ou -1 se ele não existir/estiver incompleto ou usar outro codificador.</summary>
    public static long CompletedFrames(string path, string encoderName)
    {
        var header = SegmentFile.TryReadComplete(path, out var frames);
        return header is not null && header.EncoderName == encoderName ? frames : -1;
    }

    public void Dispose()
    {
        _writer.Dispose();
        _encoder.Dispose();
    }
}
