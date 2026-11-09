using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Native;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine;

/// <summary>Gera miniaturas (RF028) decodificando um quadro com o FFmpeg, com cache em disco.</summary>
public sealed class ThumbnailProvider(MediaCache cache, ILogger<ThumbnailProvider> logger) : IThumbnailProvider
{
    public async Task<BgraImage?> GetThumbnailAsync(MediaAsset asset, string sourcePath, MediaTime at, int maxWidth, CancellationToken cancellationToken = default)
    {
        if (asset.Kind == MediaKind.Audio) return null;

        // Imagens estáticas têm um único quadro.
        if (asset.Kind == MediaKind.Image) at = MediaTime.Zero;
        if (asset.Duration > MediaTime.Zero && at >= asset.Duration) at = MediaTime.Max(MediaTime.Zero, asset.Duration - new MediaTime(1));

        var cachePath = cache.ThumbnailPath(asset, sourcePath, at, maxWidth);
        var cached = cache.ReadThumbnail(cachePath);
        if (cached is not null) return cached;

        var image = await MediaWorkers.RunAsync(() => Decode(asset, sourcePath, at, maxWidth), cancellationToken).ConfigureAwait(false);
        if (image is not null) cache.WriteThumbnail(cachePath, image);
        return image;
    }

    private BgraImage? Decode(MediaAsset asset, string sourcePath, MediaTime at, int maxWidth)
    {
        // SVG é vetorial: rasterizado diretamente no tamanho da miniatura (quadrado de largura máxima).
        if (SvgRenderer.IsSvg(sourcePath)) return SvgRenderer.Render(sourcePath, maxWidth, maxWidth);

        try
        {
            // Sequência: abre diretamente a imagem correspondente ao instante.
            var path = sourcePath;
            if (asset.Sequence is { } seq && asset.FrameRate is { } rate)
            {
                var index = (int)Math.Clamp(rate.TimeToFrame(at), 0, seq.FrameCount - 1);
                var candidate = seq.PathOf(seq.FirstNumber + index);
                path = File.Exists(candidate) ? candidate : sourcePath;
                at = MediaTime.Zero;
            }

            using var file = MediaFile.Open(path);
            var stream = file.BestStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
            if (stream < 0) return null;
            using var reader = new VideoFrameReader(file, stream);
            unsafe
            {
                var rotation = Av.RotationOf(file.Stream(stream));
                return reader.DecodeAt(at) ? reader.ToBgra(maxWidth, rotation) : null;
            }
        }
        catch (Exception ex) when (ex is FFmpegException or DecoderNotFoundException or InvalidOperationException)
        {
            logger.LogInformation(ex, "Não foi possível gerar miniatura de {Name}.", asset.Name);
            return null;
        }
    }
}
