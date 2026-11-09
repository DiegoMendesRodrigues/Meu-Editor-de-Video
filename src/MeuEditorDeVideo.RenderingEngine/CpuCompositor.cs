using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>Imagem já decodificada de uma camada, pronta para compor.</summary>
public readonly record struct LayerImage(VisualLayer Layer, BgraImage Image, bool HasAlpha);

/// <summary>Compositor de quadros: recebe as camadas de baixo para cima e produz o quadro final.</summary>
public interface ICompositor
{
    /// <param name="transparentBackground">Fundo transparente (exportação com canal alfa) em vez de preto opaco.</param>
    BgraImage Compose(int width, int height, int projectWidth, IReadOnlyList<LayerImage> layers, bool transparentBackground = false);
}

/// <summary>
/// Compositor em CPU (fallback previsto no plano para a interop D3D11 ↔ WPF).
/// Cada camada é ajustada ao quadro (contain), depois recebe a transformação do clipe
/// (escala, posição em pixels do projeto, rotação, espelhamento) e opacidade, com mistura alfa.
/// Caminho rápido para camadas sem transformação (cópia direta de linhas).
/// </summary>
public sealed class CpuCompositor : ICompositor
{
    public BgraImage Compose(int width, int height, int projectWidth, IReadOnlyList<LayerImage> layers, bool transparentBackground = false)
    {
        var output = new byte[width * height * 4];
        // Fundo preto opaco (ou totalmente transparente na exportação com alfa).
        if (!transparentBackground)
            for (var i = 3; i < output.Length; i += 4) output[i] = 255;

        var projectScale = projectWidth > 0 ? (double)width / projectWidth : 1;
        foreach (var layer in layers)
            Draw(output, width, height, projectScale, layer);

        return new BgraImage(width, height, output);
    }

    private static void Draw(byte[] dst, int dw, int dh, double projectScale, LayerImage layer)
    {
        var img = layer.Image;
        var t = layer.Layer.Transform;
        var opacity = layer.Layer.Opacity;
        if (opacity <= 0 || img.Width <= 0 || img.Height <= 0) return;
        var mask = layer.Layer.Mask;
        if (mask is { } m && TransitionMasks.IsHidden(m)) return;

        // Ajuste "contain" ao quadro, depois a escala do clipe.
        var fit = Math.Min((double)dw / img.Width, (double)dh / img.Height);
        var sx = fit * t.ScaleX * (t.FlipHorizontal ? -1 : 1);
        var sy = fit * t.ScaleY * (t.FlipVertical ? -1 : 1);
        if (Math.Abs(sx) < 1e-9 || Math.Abs(sy) < 1e-9) return;
        var cx = dw / 2.0 + t.X * projectScale;
        var cy = dh / 2.0 + t.Y * projectScale;
        var angle = t.RotationDegrees * Math.PI / 180;

        // Recorte (RF065): frações de cada borda da imagem de origem.
        var crop = new CropRect(
            Math.Clamp(t.CropLeft, 0, 1) * img.Width,
            Math.Clamp(t.CropTop, 0, 1) * img.Height,
            img.Width - Math.Clamp(t.CropRight, 0, 1) * img.Width,
            img.Height - Math.Clamp(t.CropBottom, 0, 1) * img.Height);
        if (crop.Right <= crop.Left || crop.Bottom <= crop.Top) return;
        var hasCrop = t.CropLeft > 0 || t.CropTop > 0 || t.CropRight > 0 || t.CropBottom > 0;

        var simple = !hasCrop && mask is null && layer.Layer.BlendMode == BlendMode.Normal;
        var identityScale = Math.Abs(sx - 1) < 1e-6 && Math.Abs(sy - 1) < 1e-6;
        if (simple && identityScale && Math.Abs(angle) < 1e-9)
        {
            var left = cx - img.Width / 2.0;
            var top = cy - img.Height / 2.0;
            if (Math.Abs(left - Math.Round(left)) < 1e-6 && Math.Abs(top - Math.Round(top)) < 1e-6)
            {
                Blit(dst, dw, dh, img, (int)Math.Round(left), (int)Math.Round(top), opacity, layer.HasAlpha);
                return;
            }
        }

        DrawAffine(dst, dw, dh, img, cx, cy, sx, sy, angle, opacity, crop, mask, layer.Layer.BlendMode);
    }

    private readonly record struct CropRect(double Left, double Top, double Right, double Bottom);

    /// <summary>Cópia 1:1 com recorte nas bordas; mistura alfa só quando necessário.</summary>
    private static void Blit(byte[] dst, int dw, int dh, BgraImage img, int left, int top, double opacity, bool hasAlpha)
    {
        var x0 = Math.Max(0, left);
        var x1 = Math.Min(dw, left + img.Width);
        var y0 = Math.Max(0, top);
        var y1 = Math.Min(dh, top + img.Height);
        if (x1 <= x0 || y1 <= y0) return;
        var src = img.Pixels;
        var opaque = !hasAlpha && opacity >= 0.999;
        var a = (int)Math.Round(opacity * 255);

        Parallel.For(y0, y1, y =>
        {
            var si = ((y - top) * img.Width + (x0 - left)) * 4;
            var di = (y * dw + x0) * 4;
            var count = (x1 - x0) * 4;
            if (opaque)
            {
                Buffer.BlockCopy(src, si, dst, di, count);
                return;
            }
            for (var k = 0; k < count; k += 4)
            {
                var alpha = (hasAlpha ? src[si + k + 3] : 255) * a / 255;
                BlendPixel(dst, di + k, src[si + k], src[si + k + 1], src[si + k + 2], alpha);
            }
        });
    }

    /// <summary>
    /// Mapeamento inverso com interpolação bilinear para escala/rotação arbitrárias, com recorte da origem,
    /// máscara de transição (em coordenadas de saída) e modo de mistura.
    /// </summary>
    private static void DrawAffine(byte[] dst, int dw, int dh, BgraImage img, double cx, double cy, double sx, double sy, double angle,
        double opacity, CropRect crop, TransitionMask? mask, BlendMode mode)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        double hw = img.Width / 2.0, hh = img.Height / 2.0;

        // Caixa delimitadora do retângulo (recortado) transformado.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (px, py) in new[] { (crop.Left - hw, crop.Top - hh), (crop.Right - hw, crop.Top - hh), (crop.Right - hw, crop.Bottom - hh), (crop.Left - hw, crop.Bottom - hh) })
        {
            var x = px * sx;
            var y = py * sy;
            var rx = x * cos - y * sin + cx;
            var ry = x * sin + y * cos + cy;
            minX = Math.Min(minX, rx); maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry); maxY = Math.Max(maxY, ry);
        }
        var x0 = Math.Max(0, (int)Math.Floor(minX));
        var x1 = Math.Min(dw, (int)Math.Ceiling(maxX));
        var y0 = Math.Max(0, (int)Math.Floor(minY));
        var y1 = Math.Min(dh, (int)Math.Ceiling(maxY));
        if (x1 <= x0 || y1 <= y0) return;

        var src = img.Pixels;
        int iw = img.Width, ih = img.Height;
        var a = (int)Math.Round(opacity * 255);
        var cl = crop.Left - 0.5;
        var ct = crop.Top - 0.5;
        var cr = crop.Right - 0.5;
        var cb = crop.Bottom - 0.5;

        Parallel.For(y0, y1, y =>
        {
            for (var x = x0; x < x1; x++)
            {
                // Destino → origem (inverso de rotação e escala), amostrando no centro do pixel.
                var dx = x + 0.5 - cx;
                var dy = y + 0.5 - cy;
                var ux = (dx * cos + dy * sin) / sx + hw - 0.5;
                var uy = (-dx * sin + dy * cos) / sy + hh - 0.5;
                if (ux < cl || uy < ct || ux > cr || uy > cb) continue;

                var coverage = 1.0;
                if (mask is { } m)
                {
                    coverage = TransitionMasks.Coverage(m, (x + 0.5) / dw, (y + 0.5) / dh, (double)dw / dh);
                    if (coverage <= 0) continue;
                }

                var ix = (int)Math.Floor(ux);
                var iy = (int)Math.Floor(uy);
                var fx = ux - ix;
                var fy = uy - iy;
                var ix0 = Math.Clamp(ix, 0, iw - 1);
                var ix1 = Math.Clamp(ix + 1, 0, iw - 1);
                var iy0 = Math.Clamp(iy, 0, ih - 1);
                var iy1 = Math.Clamp(iy + 1, 0, ih - 1);
                int p00 = (iy0 * iw + ix0) * 4, p10 = (iy0 * iw + ix1) * 4, p01 = (iy1 * iw + ix0) * 4, p11 = (iy1 * iw + ix1) * 4;

                double Sample(int c) =>
                    (src[p00 + c] * (1 - fx) + src[p10 + c] * fx) * (1 - fy) + (src[p01 + c] * (1 - fx) + src[p11 + c] * fx) * fy;

                var alpha = (int)(Sample(3) * a / 255 * coverage);
                var di = (y * dw + x) * 4;
                byte b = (byte)Sample(0), g = (byte)Sample(1), r = (byte)Sample(2);
                if (mode != BlendMode.Normal)
                {
                    b = Blend(mode, b, dst[di]);
                    g = Blend(mode, g, dst[di + 1]);
                    r = Blend(mode, r, dst[di + 2]);
                }
                BlendPixel(dst, di, b, g, r, alpha);
            }
        });
    }

    /// <summary>Modos de mistura (RF151), por canal: s = camada, d = fundo.</summary>
    internal static byte Blend(BlendMode mode, byte s, byte d)
    {
        int si = s, di = d;
        var v = mode switch
        {
            BlendMode.Multiply => si * di / 255,
            BlendMode.Screen => 255 - (255 - si) * (255 - di) / 255,
            BlendMode.Overlay => di < 128 ? 2 * si * di / 255 : 255 - 2 * (255 - si) * (255 - di) / 255,
            BlendMode.SoftLight => SoftLight(si, di),
            BlendMode.Add => Math.Min(255, si + di),
            BlendMode.Darken => Math.Min(si, di),
            BlendMode.Lighten => Math.Max(si, di),
            BlendMode.Difference => Math.Abs(si - di),
            _ => si,
        };
        return (byte)v;

        static int SoftLight(int s, int d)
        {
            double sf = s / 255.0, df = d / 255.0;
            var r = sf <= 0.5 ? df - (1 - 2 * sf) * df * (1 - df) : df + (2 * sf - 1) * ((df <= 0.25 ? ((16 * df - 12) * df + 4) * df : Math.Sqrt(df)) - df);
            return (int)Math.Round(Math.Clamp(r, 0, 1) * 255);
        }
    }

    private static void BlendPixel(byte[] dst, int i, byte b, byte g, byte r, int alpha)
    {
        if (alpha <= 0) return;
        if (alpha >= 255)
        {
            dst[i] = b; dst[i + 1] = g; dst[i + 2] = r; dst[i + 3] = 255;
            return;
        }
        var inv = 255 - alpha;
        var da = dst[i + 3];
        if (da == 255)
        {
            dst[i] = (byte)((b * alpha + dst[i] * inv) / 255);
            dst[i + 1] = (byte)((g * alpha + dst[i + 1] * inv) / 255);
            dst[i + 2] = (byte)((r * alpha + dst[i + 2] * inv) / 255);
            return;
        }

        // Destino translúcido (fundo transparente): operador "over" com alfa não pré-multiplicado.
        var wd = da * inv / 255;
        var oa = alpha + wd;
        if (oa <= 0) return;
        dst[i] = (byte)((b * alpha + dst[i] * wd) / oa);
        dst[i + 1] = (byte)((g * alpha + dst[i + 1] * wd) / oa);
        dst[i + 2] = (byte)((r * alpha + dst[i + 2] * wd) / oa);
        dst[i + 3] = (byte)oa;
    }
}
