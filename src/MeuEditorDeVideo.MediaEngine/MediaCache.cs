using System.Security.Cryptography;
using System.Text;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine;

/// <summary>
/// Cache em disco de miniaturas e formas de onda em %LOCALAPPDATA%\MeuEditorDeVideo\cache\media\{chave}.
/// A chave depende do conteúdo (impressão digital) e não do caminho, então sobrevive a relocalizações.
/// Arquivos inválidos são ignorados e recriados.
/// </summary>
public sealed class MediaCache(IAppPaths paths, ILogger<MediaCache> logger)
{
    private const int ThumbMagic = 0x42_47_52_41; // "BGRA"
    private const int WaveMagic = 0x50_45_41_4B;  // "PEAK"
    private const int FormatVersion = 1;

    public string FolderFor(MediaAsset asset, string sourcePath)
    {
        var key = asset.Fingerprint ?? HashKey(sourcePath, asset.FileSize, asset.LastWriteUtc);
        if (asset.Sequence is { } seq) key = HashKey(key, seq.FirstNumber, seq.FrameCount);
        return Path.Combine(paths.Cache, "media", key.Length > 32 ? key[..32] : key);
    }

    public string ThumbnailPath(MediaAsset asset, string sourcePath, MediaTime at, int maxWidth) =>
        Path.Combine(FolderFor(asset, sourcePath), $"thumb_{at.Ticks / 705_600}_{maxWidth}.bgra");

    public string WaveformPath(MediaAsset asset, string sourcePath) =>
        Path.Combine(FolderFor(asset, sourcePath), "waveform.peaks");

    public BgraImage? ReadThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var r = new BinaryReader(File.OpenRead(path));
            if (r.ReadInt32() != ThumbMagic || r.ReadInt32() != FormatVersion) return null;
            int w = r.ReadInt32(), h = r.ReadInt32();
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
            var pixels = r.ReadBytes(w * h * 4);
            return pixels.Length == w * h * 4 ? new BgraImage(w, h, pixels) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            logger.LogDebug(ex, "Miniatura em cache ilegível.");
            return null;
        }
    }

    public void WriteThumbnail(string path, BgraImage image) => WriteAtomic(path, w =>
    {
        w.Write(ThumbMagic); w.Write(FormatVersion);
        w.Write(image.Width); w.Write(image.Height);
        w.Write(image.Pixels);
    });

    public WaveformData? ReadWaveform(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var r = new BinaryReader(File.OpenRead(path));
            if (r.ReadInt32() != WaveMagic || r.ReadInt32() != FormatVersion) return null;
            var pps = r.ReadInt32();
            var duration = new MediaTime(r.ReadInt64());
            var count = r.ReadInt32();
            if (pps <= 0 || count < 0 || count > 50_000_000) return null;
            var min = new float[count];
            var max = new float[count];
            for (var i = 0; i < count; i++) min[i] = r.ReadSingle();
            for (var i = 0; i < count; i++) max[i] = r.ReadSingle();
            return new WaveformData(pps, min, max, duration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            logger.LogDebug(ex, "Forma de onda em cache ilegível.");
            return null;
        }
    }

    public void WriteWaveform(string path, WaveformData data) => WriteAtomic(path, w =>
    {
        w.Write(WaveMagic); w.Write(FormatVersion);
        w.Write(data.PeaksPerSecond);
        w.Write(data.Duration.Ticks);
        w.Write(data.Count);
        foreach (var v in data.Min) w.Write(v);
        foreach (var v in data.Max) w.Write(v);
    });

    private void WriteAtomic(string path, Action<BinaryWriter> write)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            using (var w = new BinaryWriter(File.Create(temp)))
                write(w);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O cache é opcional: falha ao gravar não impede o uso do resultado.
            logger.LogWarning(ex, "Não foi possível gravar no cache de mídia.");
        }
    }

    private static string HashKey(params object[] parts)
    {
        var text = string.Join("|", parts.Select(p => p switch
        {
            DateTime d => d.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => p.ToString()?.ToUpperInvariant(),
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
