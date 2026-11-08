using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Native;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine.Playback;

public sealed class VideoFrameSourceFactory(ILoggerFactory loggers) : IVideoFrameSourceFactory
{
    public IVideoFrameSource Create(bool hardwareDecoding) =>
        new VideoFrameSource(hardwareDecoding, loggers.CreateLogger<VideoFrameSource>());
}

/// <summary>
/// Quadros para a pré-visualização: um decodificador aberto por mídia, reaproveitado entre quadros
/// (leitura sequencial sem busca durante a reprodução) e o último quadro convertido em cache.
/// </summary>
public sealed class VideoFrameSource(bool hardwareDecoding, ILogger<VideoFrameSource> logger) : IVideoFrameSource
{
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly HashSet<Guid> _failed = [];

    /// <summary>Quantas mídias estão sendo decodificadas pela GPU (diagnóstico).</summary>
    public int HardwareDecoders => _entries.Values.Count(e => e.Reader?.UsesHardware == true);

    public BgraImage? GetFrame(MediaAsset asset, string path, MediaTime sourceTime, int maxWidth, int maxHeight)
    {
        if (!asset.HasVideo || _failed.Contains(asset.Id)) return null;
        try
        {
            if (!_entries.TryGetValue(asset.Id, out var entry) || entry.Path != path)
            {
                entry?.Dispose();
                entry = new Entry(path);
                _entries[asset.Id] = entry;
            }

            return asset.Kind switch
            {
                MediaKind.Image => entry.Still(path, maxWidth, maxHeight),
                MediaKind.ImageSequence when asset.Sequence is { } seq => entry.SequenceFrame(seq, asset.FrameRate ?? FrameRate.Fps30, sourceTime, maxWidth, maxHeight),
                _ => entry.VideoFrame(sourceTime, maxWidth, maxHeight, hardwareDecoding),
            };
        }
        catch (Exception ex) when (ex is FFmpegException or DecoderNotFoundException or InvalidOperationException or IOException)
        {
            // Mídia problemática não deve travar a reprodução: registra e passa a ignorá-la nesta sessão.
            logger.LogWarning(ex, "Falha ao decodificar {Name} para a pré-visualização.", asset.Name);
            _failed.Add(asset.Id);
            if (_entries.Remove(asset.Id, out var broken)) broken.Dispose();
            return null;
        }
    }

    public void Trim(IReadOnlySet<Guid> inUse)
    {
        foreach (var id in _entries.Keys.Where(k => !inUse.Contains(k)).ToList())
        {
            _entries[id].Dispose();
            _entries.Remove(id);
        }
        _failed.IntersectWith(inUse);
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values) e.Dispose();
        _entries.Clear();
    }

    private sealed class Entry(string path) : IDisposable
    {
        public string Path { get; } = path;
        public VideoFrameReader? Reader { get; private set; }
        private MediaFile? _file;
        private int _rotation;
        private BgraImage? _image;
        private (int W, int H) _imageSize;
        private int _sequenceIndex = -1;

        public BgraImage? VideoFrame(MediaTime at, int maxW, int maxH, bool hardware)
        {
            if (Reader is null) Open(Path, hardware);
            var reader = Reader!;
            bool positioned, changed;
            try
            {
                positioned = reader.Position(at, out changed);
            }
            catch (FFmpegException) when (reader.UsesHardware)
            {
                // A GPU recusou este fluxo (perfil/resolução): volta para decodificação por software.
                Close();
                Open(Path, hardware: false);
                reader = Reader!;
                positioned = reader.Position(at, out changed);
            }
            if (!positioned) return _image;
            if (!changed && _image is not null && _imageSize == (maxW, maxH)) return _image;
            _image = reader.ToBgraFit(maxW, maxH, _rotation);
            _imageSize = (maxW, maxH);
            return _image;
        }

        public BgraImage? Still(string path, int maxW, int maxH)
        {
            if (_image is not null && _imageSize == (maxW, maxH)) return _image;
            Close();
            Open(path, hardware: false);
            _image = Reader!.Position(MediaTime.Zero, out _) ? Reader.ToBgraFit(maxW, maxH, _rotation) : null;
            _imageSize = (maxW, maxH);
            Close(); // imagem estática: não precisa manter o arquivo aberto
            return _image;
        }

        public BgraImage? SequenceFrame(ImageSequenceInfo seq, FrameRate rate, MediaTime at, int maxW, int maxH)
        {
            var index = (int)Math.Clamp(rate.TimeToFrame(at), 0, seq.FrameCount - 1);
            if (index == _sequenceIndex && _image is not null && _imageSize == (maxW, maxH)) return _image;
            var file = seq.PathOf(seq.FirstNumber + index);
            if (!File.Exists(file)) return _image;
            _image = null;
            var image = Still(file, maxW, maxH);
            _sequenceIndex = index;
            return image;
        }

        private unsafe void Open(string path, bool hardware)
        {
            _file = MediaFile.Open(path);
            var stream = _file.BestStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
            if (stream < 0) throw new InvalidOperationException("A mídia não tem faixa de vídeo.");
            _rotation = Av.RotationOf(_file.Stream(stream));
            try
            {
                Reader = new VideoFrameReader(_file, stream, threads: 0, hardware);
            }
            catch (FFmpegException) when (hardware)
            {
                Reader = new VideoFrameReader(_file, stream, threads: 0, hardware: false);
            }
        }

        private void Close()
        {
            Reader?.Dispose();
            Reader = null;
            _file?.Dispose();
            _file = null;
        }

        public void Dispose() => Close();
    }
}
