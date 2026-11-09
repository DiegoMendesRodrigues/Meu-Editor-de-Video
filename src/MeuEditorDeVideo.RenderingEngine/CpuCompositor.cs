using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>Imagem já decodificada de uma camada, pronta para compor.</summary>
/// <param name="NativeSize">A imagem já está em pixels de saída (textos): não é ajustada ao quadro.</param>
public readonly record struct LayerImage(VisualLayer Layer, BgraImage Image, bool HasAlpha, bool NativeSize = false);

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
        var fit = layer.NativeSize ? 1 : Math.Min((double)dw / img.Width, (double)dh / img.Height);
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

        var simple = !hasCrop && mask is null && layer.Layer.BlendMode == BlendMode.Normal && !t.Is3D;
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

        var h = Projection(img.Width, img.Height, cx, cy, sx, sy, angle, t.RotationX, t.RotationY, t.Depth * projectScale, dw);
        DrawProjective(dst, dw, dh, img, h, opacity, crop, mask, layer.Layer.BlendMode);
    }

    private readonly record struct CropRect(double Left, double Top, double Right, double Bottom);

    /// <summary>Distância focal da câmera virtual da composição 2,5D, em larguras do quadro.</summary>
    public const double FocalWidths = 2.0;

    /// <summary>
    /// Homografia (3×3, linha a linha) que leva um ponto (u, v, 1) da imagem de origem — em pixels — ao quadro de saída
    /// em coordenadas homogêneas: escala/espelhamento, giros 3D (Y, depois X), giro no plano, profundidade e perspectiva.
    /// Sem giros 3D nem profundidade, é a transformação afim usual (w = 1).
    /// </summary>
    public static double[] Projection(int iw, int ih, double cx, double cy, double sx, double sy, double angle,
        double rotX, double rotY, double depth, int frameWidth)
    {
        double rx = rotX * Math.PI / 180, ry = rotY * Math.PI / 180;
        double crx = Math.Cos(rx), srx = Math.Sin(rx), cry = Math.Cos(ry), sry = Math.Sin(ry);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        // Coeficientes em (x, y) locais, com x = sx·(u − iw/2) e y = sy·(v − ih/2).
        double xa = cry * cos - sry * srx * sin, xb = -crx * sin;
        double ya = cry * sin + sry * srx * cos, yb = crx * cos;
        double za = -sry * crx, zb = srx;
        double hw = iw / 2.0, hh = ih / 2.0;
        var f = FocalWidths * frameWidth;

        // Linhas afins em (u, v, 1) para X, Y, Z.
        double[] X = [xa * sx, xb * sy, -(xa * sx * hw + xb * sy * hh)];
        double[] Y = [ya * sx, yb * sy, -(ya * sx * hw + yb * sy * hh)];
        double[] Z = [za * sx, zb * sy, -(za * sx * hw + zb * sy * hh) + depth];
        // w = (f + Z) / f; saída = (X + cx·w, Y + cy·w, w).
        double[] w = [Z[0] / f, Z[1] / f, 1 + Z[2] / f];
        return
        [
            X[0] + cx * w[0], X[1] + cx * w[1], X[2] + cx * w[2],
            Y[0] + cy * w[0], Y[1] + cy * w[1], Y[2] + cy * w[2],
            w[0], w[1], w[2],
        ];
    }

    /// <summary>Inversa de uma matriz 3×3 (nula se singular).</summary>
    public static double[]? Invert(double[] m)
    {
        var a = m[4] * m[8] - m[5] * m[7];
        var b = m[5] * m[6] - m[3] * m[8];
        var c = m[3] * m[7] - m[4] * m[6];
        var det = m[0] * a + m[1] * b + m[2] * c;
        if (Math.Abs(det) < 1e-12) return null;
        var k = 1 / det;
        return
        [
            a * k, (m[2] * m[7] - m[1] * m[8]) * k, (m[1] * m[5] - m[2] * m[4]) * k,
            b * k, (m[0] * m[8] - m[2] * m[6]) * k, (m[2] * m[3] - m[0] * m[5]) * k,
            c * k, (m[1] * m[6] - m[0] * m[7]) * k, (m[0] * m[4] - m[1] * m[3]) * k,
        ];
    }

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
    /// Mapeamento inverso (homografia) com interpolação bilinear: escala, giros (2D e 3D), perspectiva, recorte da
    /// origem, máscara de transição (em coordenadas de saída) e modo de mistura.
    /// </summary>
    private static void DrawProjective(byte[] dst, int dw, int dh, BgraImage img, double[] h, double opacity, CropRect crop,
        TransitionMask? mask, BlendMode mode)
    {
        if (Invert(h) is not { } inv) return;
        const double minW = 0.05; // atrás da câmera (ou rente a ela): não desenha

        // Caixa delimitadora do retângulo (recortado) projetado.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var behind = false;
        foreach (var (u, v) in new[] { (crop.Left, crop.Top), (crop.Right, crop.Top), (crop.Right, crop.Bottom), (crop.Left, crop.Bottom) })
        {
            var w = h[6] * u + h[7] * v + h[8];
            if (w <= minW)
            {
                behind = true;
                continue;
            }
            var x = (h[0] * u + h[1] * v + h[2]) / w;
            var y = (h[3] * u + h[4] * v + h[5]) / w;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        int x0, x1, y0, y1;
        if (behind)
        {
            x0 = 0; y0 = 0; x1 = dw; y1 = dh;
        }
        else
        {
            x0 = Math.Max(0, (int)Math.Floor(minX));
            x1 = Math.Min(dw, (int)Math.Ceiling(maxX));
            y0 = Math.Max(0, (int)Math.Floor(minY));
            y1 = Math.Min(dh, (int)Math.Ceiling(maxY));
        }
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
            var py = y + 0.5;
            for (var x = x0; x < x1; x++)
            {
                // Destino → origem, amostrando no centro do pixel.
                var px = x + 0.5;
                var hz = inv[6] * px + inv[7] * py + inv[8];
                if (Math.Abs(hz) < 1e-12) continue;
                var u = (inv[0] * px + inv[1] * py + inv[2]) / hz;
                var v = (inv[3] * px + inv[4] * py + inv[5]) / hz;
                if (h[6] * u + h[7] * v + h[8] <= minW) continue;
                var ux = u - 0.5;
                var uy = v - 0.5;
                if (ux < cl || uy < ct || ux > cr || uy > cb) continue;

                var coverage = 1.0;
                if (mask is { } m)
                {
                    coverage = TransitionMasks.Coverage(m, px / dw, py / dh, (double)dw / dh);
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
