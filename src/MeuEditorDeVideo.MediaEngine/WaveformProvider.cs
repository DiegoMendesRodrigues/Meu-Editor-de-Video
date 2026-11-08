using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.MediaEngine.Native;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine;

/// <summary>Gera formas de onda decodificando todo o áudio uma vez, com cache em disco.</summary>
public sealed class WaveformProvider(MediaCache cache, ILogger<WaveformProvider> logger) : IWaveformProvider
{
    /// <summary>Resolução: 100 picos por segundo (10 ms) — suficiente para zoom de quadro em 60 FPS.</summary>
    public const int PeaksPerSecond = 100;

    public async Task<WaveformData?> GetWaveformAsync(MediaAsset asset, string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!asset.HasAudio) return null;

        var cachePath = cache.WaveformPath(asset, sourcePath);
        var cached = cache.ReadWaveform(cachePath);
        if (cached is not null) return cached;

        var data = await MediaWorkers.RunAsync(() => Decode(asset, sourcePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (data is not null) cache.WriteWaveform(cachePath, data);
        return data;
    }

    private WaveformData? Decode(MediaAsset asset, string sourcePath, CancellationToken ct)
    {
        try
        {
            using var file = MediaFile.Open(sourcePath);
            var stream = file.BestStream(AVMediaType.AVMEDIA_TYPE_AUDIO);
            if (stream < 0) return null;
            using var reader = new AudioPeakReader(file, stream);
            return reader.Read(PeaksPerSecond, ct);
        }
        catch (Exception ex) when (ex is FFmpegException or DecoderNotFoundException or InvalidOperationException)
        {
            logger.LogInformation(ex, "Não foi possível gerar a forma de onda de {Name}.", asset.Name);
            return null;
        }
    }
}
