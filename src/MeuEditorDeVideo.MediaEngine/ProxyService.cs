using System.Collections.Concurrent;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.MediaEngine.Export;
using MeuEditorDeVideo.MediaEngine.Native;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.MediaEngine;

/// <summary>
/// Gera um proxy de edição: decodifica o original quadro a quadro na taxa da mídia (CFR), aplica a rotação,
/// reduz para 540p (lado menor) e codifica em H.264 MP4 só com vídeo, GOP de ~0,5 s para busca rápida.
/// O quadro i do proxy corresponde exatamente ao instante i / fps do original.
/// </summary>
public static unsafe class ProxyGenerator
{
    public const int BitrateKbps = 2500;

    public static void Generate(MediaAsset asset, string sourcePath, string outputPath, Action<double>? progress, CancellationToken ct)
    {
        FFmpegRuntime.EnsureInitialized();
        var fps = asset.FrameRate is { Value: > 0 } f ? f : FrameRate.Fps30;
        var totalFrames = Math.Max(1, (long)Math.Ceiling(asset.Duration.TotalSeconds * fps.Value - 1e-6));

        var dir = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(dir);
        var segment = Path.Combine(dir, "proxy.medseg");
        var temp = outputPath + ".tmp.mp4";

        try
        {
            using var file = MediaFile.Open(sourcePath);
            var stream = file.BestStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
            if (stream < 0) throw new InvalidOperationException("A mídia não tem faixa de vídeo.");
            var rotation = Av.RotationOf(file.Stream(stream));
            using var reader = new VideoFrameReader(file, stream, threads: 0, hardware: false);
            if (!reader.Position(MediaTime.Zero, out _)) throw new InvalidOperationException("Não foi possível ler o primeiro quadro.");

            // Tamanho exibido (com rotação) → tamanho do proxy.
            var probe = reader.ToBgraFit(1 << 14, 1 << 14, rotation);
            var (w, h) = ProxyPolicy.SizeFor(probe.Width, probe.Height);

            var settings = new ExportSettings
            {
                Container = ExportContainer.Mp4, VideoCodec = VideoCodec.H264, AudioCodec = AudioCodec.None,
                RateControl = RateControl.Vbr, VideoBitrateKbps = BitrateKbps,
            };
            var spec = new VideoEncodeSpec(settings, w, h, fps, BitrateKbps)
            {
                GopSize = Math.Max(1, (int)Math.Round(fps.Value / 2)),
                FastPreset = true,
            };

            Exception? last = null;
            foreach (var encoder in EncoderCatalog.Candidates(VideoCodec.H264, EncoderPreference.Auto, alpha: false))
            {
                try
                {
                    Encode(reader, rotation, spec, encoder, segment, totalFrames, fps, progress, ct);
                    last = null;
                    break;
                }
                catch (Exception ex) when (ex is FFmpegException or EncoderSetupException && EncoderCatalog.IsHardware(encoder))
                {
                    last = ex; // tenta o próximo (software no fim da lista)
                    reader.Position(MediaTime.Zero, out _);
                }
            }
            if (last is not null) throw last;

            ExportMuxer.Mux(temp, settings, [segment], 0, null, null, ct);
            File.Move(temp, outputPath, overwrite: true);
            progress?.Invoke(1);
        }
        finally
        {
            TryDelete(segment);
            TryDelete(temp);
        }
    }

    private static void Encode(VideoFrameReader reader, int rotation, VideoEncodeSpec spec, string encoder, string segment,
        long totalFrames, FrameRate fps, Action<double>? progress, CancellationToken ct)
    {
        using var enc = new VideoSegmentEncoder(segment, spec, encoder, 0);
        for (long i = 0; i < totalFrames; i++)
        {
            ct.ThrowIfCancellationRequested();
            reader.Position(fps.FrameToTime(i), out _);
            var image = reader.ToBgraFit(spec.Width, spec.Height, rotation);
            enc.Write(Fit(image, spec.Width, spec.Height));
            if (i % 15 == 0) progress?.Invoke(0.98 * i / totalFrames);
        }
        enc.Complete();
    }

    /// <summary>Garante o tamanho exato (o ajuste proporcional pode diferir em 1 pixel): repete a borda.</summary>
    private static BgraImage Fit(BgraImage image, int width, int height)
    {
        if (image.Width == width && image.Height == height) return image;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Min(image.Height - 1, (int)((long)y * image.Height / height));
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Min(image.Width - 1, (int)((long)x * image.Width / width));
                Buffer.BlockCopy(image.Pixels, (sy * image.Width + sx) * 4, pixels, (y * width + x) * 4, 4);
            }
        }
        return new BgraImage(width, height, pixels);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Fila de geração de proxies em segundo plano (uma mídia por vez, prioridade baixa).</summary>
public sealed class ProxyService : IProxyService, IDisposable
{
    private const string FileName = "proxy_540p_v1.mp4";

    private readonly MediaCache _cache;
    private readonly ILogger<ProxyService> _logger;
    private readonly ConcurrentDictionary<Guid, ProxyStatus> _status = new();
    private readonly BlockingCollection<(MediaAsset Asset, string Source)> _queue = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly Thread _worker;

    public ProxyService(MediaCache cache, ILogger<ProxyService> logger)
    {
        _cache = cache;
        _logger = logger;
        _worker = new Thread(Work) { IsBackground = true, Name = "Proxies", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    public event EventHandler<ProxyStatus>? StatusChanged;

    public string PathFor(MediaAsset asset, string sourcePath) => Path.Combine(_cache.FolderFor(asset, sourcePath), FileName);

    public ProxyStatus StatusOf(MediaAsset asset, string sourcePath)
    {
        if (_status.TryGetValue(asset.Id, out var s) && s.State is ProxyState.Queued or ProxyState.Generating or ProxyState.Failed) return s;
        return new ProxyStatus(asset.Id, ProxyPolicy.Supports(asset) && File.Exists(PathFor(asset, sourcePath)) ? ProxyState.Ready : ProxyState.None);
    }

    public string? ReadyPath(MediaAsset asset, string sourcePath)
    {
        if (!ProxyPolicy.Supports(asset)) return null;
        var path = PathFor(asset, sourcePath);
        return File.Exists(path) ? path : null;
    }

    public void Request(MediaAsset asset, string sourcePath)
    {
        if (!ProxyPolicy.Supports(asset)) return;
        var current = StatusOf(asset, sourcePath).State;
        if (current is ProxyState.Ready or ProxyState.Queued or ProxyState.Generating) return;
        Publish(new ProxyStatus(asset.Id, ProxyState.Queued));
        _queue.Add((asset, sourcePath));
    }

    public void Delete(MediaAsset asset, string sourcePath)
    {
        if (_running.TryRemove(asset.Id, out var cts)) cts.Cancel();
        _status.TryRemove(asset.Id, out _);
        try
        {
            var path = PathFor(asset, sourcePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Não foi possível apagar o proxy de {Name}.", asset.Name);
        }
        Publish(new ProxyStatus(asset.Id, ProxyState.None));
    }

    private void Work()
    {
        foreach (var (asset, source) in _queue.GetConsumingEnumerable())
        {
            // Pedido cancelado (Delete) enquanto aguardava na fila.
            if (!_status.TryGetValue(asset.Id, out var s) || s.State != ProxyState.Queued) continue;
            using var cts = new CancellationTokenSource();
            _running[asset.Id] = cts;
            var output = PathFor(asset, source);
            var lastReport = 0.0;
            try
            {
                Publish(new ProxyStatus(asset.Id, ProxyState.Generating));
                ProxyGenerator.Generate(asset, source, output, p =>
                {
                    if (p - lastReport < 0.02 && p < 1) return;
                    lastReport = p;
                    Publish(new ProxyStatus(asset.Id, ProxyState.Generating, p));
                }, cts.Token);
                _status.TryRemove(asset.Id, out _);
                Publish(new ProxyStatus(asset.Id, ProxyState.Ready, 1));
                _logger.LogInformation("Proxy gerado para {Name}.", asset.Name);
            }
            catch (OperationCanceledException)
            {
                _status.TryRemove(asset.Id, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gerar o proxy de {Name}.", asset.Name);
                Publish(new ProxyStatus(asset.Id, ProxyState.Failed, 0, ex.Message));
            }
            finally
            {
                _running.TryRemove(asset.Id, out _);
            }
        }
    }

    private void Publish(ProxyStatus status)
    {
        if (status.State is ProxyState.None or ProxyState.Ready) _status.TryRemove(status.AssetId, out _);
        else _status[status.AssetId] = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        foreach (var cts in _running.Values) cts.Cancel();
        _queue.CompleteAdding();
        _worker.Join(2000);
        _queue.Dispose();
    }
}
