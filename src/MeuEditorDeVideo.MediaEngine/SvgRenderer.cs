using MeuEditorDeVideo.Core.Media;
using SkiaSharp;
using Svg.Skia;

namespace MeuEditorDeVideo.MediaEngine;

/// <summary>
/// Rasteriza arquivos SVG com o Skia no tamanho pedido, para que a imagem continue nítida em qualquer escala.
/// Os arquivos analisados ficam em um cache pequeno (por caminho e data de modificação).
/// </summary>
public static class SvgRenderer
{
    /// <summary>Tamanho usado quando o SVG não declara dimensões (largura ou altura zero).</summary>
    private const int FallbackSize = 512;

    private const int MaxCacheEntries = 16;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static long _clock;

    public static bool IsSvg(string path) =>
        string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dimensões naturais do SVG, ou null se o arquivo não puder ser lido.</summary>
    public static (int Width, int Height)? Probe(string path) =>
        WithPicture<(int Width, int Height)?>(path, picture => SizeOf(picture), null);

    /// <summary>
    /// Rasteriza o SVG para caber em <paramref name="maxWidth"/>×<paramref name="maxHeight"/>, preservando a proporção.
    /// Retorna BGRA com alfa não pré-multiplicado, ou null se o arquivo não puder ser lido.
    /// </summary>
    public static BgraImage? Render(string path, int maxWidth, int maxHeight) =>
        WithPicture<BgraImage?>(path, picture => RenderPicture(picture, Math.Max(1, maxWidth), Math.Max(1, maxHeight)), null);

    private static (int Width, int Height) SizeOf(SKPicture picture)
    {
        var rect = picture.CullRect;
        var w = (int)Math.Round(rect.Width);
        var h = (int)Math.Round(rect.Height);
        return w <= 0 || h <= 0 ? (FallbackSize, FallbackSize) : (Math.Max(1, w), Math.Max(1, h));
    }

    private static BgraImage RenderPicture(SKPicture picture, int maxWidth, int maxHeight)
    {
        var (w, h) = SizeOf(picture);
        var scale = Math.Min((double)maxWidth / w, (double)maxHeight / h);
        var outW = Math.Max(1, (int)Math.Round(w * scale));
        var outH = Math.Max(1, (int)Math.Round(h * scale));

        // Desenha pré-multiplicado (modo nativo do Skia) e converte para alfa reto ao final.
        using var bitmap = new SKBitmap(new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)outW / w, (float)outH / h);
            canvas.DrawPicture(picture);
        }

        var src = bitmap.GetPixelSpan();
        var pixels = new byte[outW * outH * 4];
        for (var y = 0; y < outH; y++)
        {
            var srcRow = y * bitmap.RowBytes;
            var dstRow = y * outW * 4;
            for (var x = 0; x < outW; x++)
            {
                var si = srcRow + x * 4;
                var di = dstRow + x * 4;
                var a = src[si + 3];
                pixels[di + 3] = a;
                if (a == 0) continue;
                if (a == 255)
                {
                    pixels[di] = src[si];
                    pixels[di + 1] = src[si + 1];
                    pixels[di + 2] = src[si + 2];
                    continue;
                }
                for (var c = 0; c < 3; c++)
                    pixels[di + c] = (byte)((src[si + c] * 255 + a / 2) / a);
            }
        }
        return new BgraImage(outW, outH, pixels);
    }

    /// <summary>Executa <paramref name="action"/> sobre o SVG analisado, segurando o trava da entrada durante o uso.</summary>
    private static TResult WithPicture<TResult>(string path, Func<SKPicture, TResult> action, TResult fallback)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var entry = GetOrLoad(path);
            if (entry is null) return fallback;
            lock (entry.Gate)
            {
                // Entrada despejada do cache entre a busca e o uso: tenta novamente.
                if (!entry.Disposed) return action(entry.Picture);
            }
        }
        return fallback;
    }

    private static Entry? GetOrLoad(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var writeUtc = File.GetLastWriteTimeUtc(full);

            lock (CacheLock)
            {
                if (Cache.TryGetValue(full, out var hit) && hit.WriteUtc == writeUtc)
                {
                    hit.LastUse = ++_clock;
                    return hit;
                }

                if (Cache.Remove(full, out var stale)) stale.Dispose();

                var svg = new SKSvg();
                SKPicture? picture;
                try
                {
                    picture = svg.Load(full);
                }
                catch (Exception)
                {
                    picture = null;
                }
                if (picture is null)
                {
                    svg.Dispose();
                    return null;
                }

                if (Cache.Count >= MaxCacheEntries)
                {
                    var oldest = Cache.Values.MinBy(e => e.LastUse)!;
                    Cache.Remove(oldest.Path);
                    oldest.Dispose();
                }

                var entry = new Entry(full, writeUtc, svg, picture) { LastUse = ++_clock };
                Cache[full] = entry;
                return entry;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class Entry(string path, DateTime writeUtc, SKSvg svg, SKPicture picture) : IDisposable
    {
        public string Path { get; } = path;
        public DateTime WriteUtc { get; } = writeUtc;
        public SKPicture Picture { get; } = picture;
        public object Gate { get; } = new();
        public bool Disposed { get; private set; }
        public long LastUse { get; set; }

        public void Dispose()
        {
            lock (Gate)
            {
                if (Disposed) return;
                Disposed = true;
                svg.Dispose();
            }
        }
    }
}
