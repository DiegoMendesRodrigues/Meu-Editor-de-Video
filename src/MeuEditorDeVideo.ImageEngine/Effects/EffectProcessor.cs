using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using static MeuEditorDeVideo.Domain.Effects.EffectCatalog;

namespace MeuEditorDeVideo.ImageEngine.Effects;

/// <summary>Contexto do processamento: escala de pixels do projeto para a imagem e número do quadro (granulado).</summary>
public readonly record struct EffectContext(double PixelScale, long Frame);

/// <summary>
/// Aplica a pilha de efeitos de um clipe a uma imagem BGRA (CPU, paralelizado por linhas). A imagem de entrada nunca
/// é alterada (pode estar em cache no decodificador). Efeitos são aplicados em ordem (RF088); falhas de recurso
/// (ex.: LUT ausente) fazem o efeito ser ignorado, sem interromper a reprodução/exportação.
/// </summary>
public static class EffectProcessor
{
    /// <returns>Imagem processada e se ela passou a ter transparência.</returns>
    public static (BgraImage Image, bool AlphaChanged) Apply(BgraImage source, IReadOnlyList<EffectInstance> effects, EffectContext ctx)
    {
        if (effects.Count == 0) return (source, false);
        var px = (byte[])source.Pixels.Clone();
        int w = source.Width, h = source.Height;
        var alpha = false;
        foreach (var e in effects)
        {
            if (IsNeutral(e)) continue;
            switch (e.Type)
            {
                case ColorAdjust: Adjust(px, e); break;
                case WhiteBalance: WhiteBalanceFx(px, e); break;
                case Curves: CurvesFx(px, e); break;
                case Wheels: WheelsFx(px, e); break;
                case Lut: LutFx(px, e); break;
                case Blur: BlurFx(px, w, h, Value(e, "radius") * ctx.PixelScale); break;
                case Sharpen: SharpenFx(px, w, h, Value(e, "amount") / 100, ctx.PixelScale); break;
                case Vignette: VignetteFx(px, w, h, Value(e, "amount") / 100, Value(e, "size") / 100); break;
                case Grayscale: Matrix(px, Value(e, "intensity") / 100, [0.2126f, 0.7152f, 0.0722f, 0.2126f, 0.7152f, 0.0722f, 0.2126f, 0.7152f, 0.0722f]); break;
                case Sepia: Matrix(px, Value(e, "intensity") / 100, [0.393f, 0.769f, 0.189f, 0.349f, 0.686f, 0.168f, 0.272f, 0.534f, 0.131f]); break;
                case Invert: InvertFx(px, Value(e, "intensity") / 100); break;
                case Pixelate: PixelateFx(px, w, h, Math.Max(2, (int)Math.Round(Value(e, "size") * ctx.PixelScale))); break;
                case Noise: NoiseFx(px, w, h, Value(e, "amount") / 100, ctx.Frame); break;
                case Posterize: PosterizeFx(px, (int)Math.Round(Value(e, "levels"))); break;
                case Glow: GlowFx(px, w, h, Value(e, "amount") / 100, Value(e, "radius") * ctx.PixelScale, Value(e, "threshold") / 100); break;
                case ChromaKey: alpha |= ChromaKeyFx(px, w, h, e, ctx.PixelScale); break;
            }
        }
        return (new BgraImage(w, h, px), alpha);
    }

    /// <summary>Mistura duas imagens do mesmo tamanho (câmera lenta suavizada, RF071).</summary>
    public static BgraImage Mix(BgraImage a, BgraImage b, double weight)
    {
        if (a.Width != b.Width || a.Height != b.Height || weight <= 0) return a;
        var outPx = new byte[a.Pixels.Length];
        var wb = (int)Math.Round(Math.Clamp(weight, 0, 1) * 256);
        var wa = 256 - wb;
        var pa = a.Pixels;
        var pb = b.Pixels;
        Parallel.For(0, a.Height, y =>
        {
            var end = (y + 1) * a.Width * 4;
            for (var i = y * a.Width * 4; i < end; i++) outPx[i] = (byte)((pa[i] * wa + pb[i] * wb) >> 8);
        });
        return new BgraImage(a.Width, a.Height, outPx);
    }

    // ------------------------------------------------------------------ cor

    private static byte ToByte(float v) => (byte)(v <= 0 ? 0 : v >= 1 ? 255 : v * 255 + 0.5f);

    private static void PerChannel(byte[] px, float[] lr, float[] lg, float[] lb)
    {
        var br = new byte[256];
        var bg = new byte[256];
        var bb = new byte[256];
        for (var i = 0; i < 256; i++) { br[i] = ToByte(lr[i]); bg[i] = ToByte(lg[i]); bb[i] = ToByte(lb[i]); }
        Rows(px, i =>
        {
            px[i] = bb[px[i]];
            px[i + 1] = bg[px[i + 1]];
            px[i + 2] = br[px[i + 2]];
        });
    }

    /// <summary>Executa a ação para cada pixel (índice do byte B), em paralelo por blocos.</summary>
    private static void Rows(byte[] px, Action<int> pixel)
    {
        var count = px.Length / 4;
        const int Block = 16384;
        Parallel.For(0, (count + Block - 1) / Block, blk =>
        {
            var end = Math.Min(count, (blk + 1) * Block);
            for (var p = blk * Block; p < end; p++) pixel(p * 4);
        });
    }

    private static void Adjust(byte[] px, EffectInstance e)
    {
        var ev = Value(e, "exposure");
        var brightness = Value(e, "brightness") / 100;
        var contrast = Value(e, "contrast") / 100;
        var saturation = Value(e, "saturation") / 100;
        var temperature = Value(e, "temperature") / 100;
        var tint = Value(e, "tint") / 100;
        var hue = Value(e, "hue");
        var shadows = Value(e, "shadows") / 100;
        var highlights = Value(e, "highlights") / 100;

        var gain = Math.Pow(2, ev);
        var cf = contrast >= 0 ? 1 + 2 * contrast : 1 + contrast;
        double[] channelGain = [1 + 0.3 * temperature, 1 - 0.25 * tint, 1 - 0.3 * temperature]; // R, G, B
        var tables = new float[3][];
        for (var c = 0; c < 3; c++)
        {
            tables[c] = new float[256];
            for (var i = 0; i < 256; i++)
            {
                var v = i / 255.0 * channelGain[c] * gain;
                v += brightness * 0.3;
                v = (v - 0.5) * cf + 0.5;
                tables[c][i] = (float)v;
            }
        }

        var cross = Math.Abs(saturation) > 1e-9 || Math.Abs(hue) > 1e-9 || Math.Abs(shadows) > 1e-9 || Math.Abs(highlights) > 1e-9;
        if (!cross)
        {
            PerChannel(px, tables[0], tables[1], tables[2]);
            return;
        }

        var m = HueMatrix(hue);
        float sat = (float)(1 + saturation), sh = (float)shadows, hi = (float)highlights;
        float[] tr = tables[0], tg = tables[1], tb = tables[2];
        Rows(px, i =>
        {
            float r = tr[px[i + 2]], g = tg[px[i + 1]], b = tb[px[i]];
            var l = Math.Clamp(0.2126f * r + 0.7152f * g + 0.0722f * b, 0, 1);
            if (sh != 0 || hi != 0)
            {
                var d = sh * 0.4f * (1 - l) * (1 - l) + hi * 0.4f * l * l;
                r += d; g += d; b += d;
            }
            if (m is not null)
            {
                var nr = m[0] * r + m[1] * g + m[2] * b;
                var ng = m[3] * r + m[4] * g + m[5] * b;
                var nb = m[6] * r + m[7] * g + m[8] * b;
                r = nr; g = ng; b = nb;
            }
            if (sat != 1)
            {
                var y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                r = y + (r - y) * sat; g = y + (g - y) * sat; b = y + (b - y) * sat;
            }
            px[i] = ToByte(b); px[i + 1] = ToByte(g); px[i + 2] = ToByte(r);
        });
    }

    /// <summary>Rotação de matiz em torno do eixo de cinzas.</summary>
    private static float[]? HueMatrix(double degrees)
    {
        if (Math.Abs(degrees) < 1e-9) return null;
        var a = degrees * Math.PI / 180;
        float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
        const float third = 1f / 3;
        var sq = (float)Math.Sqrt(third);
        var k = third * (1 - c);
        return [c + k, k - sq * s, k + sq * s, k + sq * s, c + k, k - sq * s, k - sq * s, k + sq * s, c + k];
    }

    private static void WhiteBalanceFx(byte[] px, EffectInstance e)
    {
        var (r, g, b) = ColorValue(e, "neutral");
        var amount = Value(e, "amount") / 100;
        var mean = (r + g + b) / 3;
        double Gain(double c) => 1 + (Math.Clamp(mean / Math.Max(c, 0.01), 0.25, 4) - 1) * amount;
        PerChannel(px, Ramp(Gain(r)), Ramp(Gain(g)), Ramp(Gain(b)));

        static float[] Ramp(double gain)
        {
            var t = new float[256];
            for (var i = 0; i < 256; i++) t[i] = (float)(i / 255.0 * gain);
            return t;
        }
    }

    private static void CurvesFx(byte[] px, EffectInstance e)
    {
        var master = ToneCurve.Table(CurvePoints(e, "master"));
        float[] Channel(string ch)
        {
            var t = ToneCurve.Table(CurvePoints(e, ch));
            var result = new float[256];
            for (var i = 0; i < 256; i++) result[i] = t[ToByte(master[i])];
            return result;
        }
        PerChannel(px, Channel("r"), Channel("g"), Channel("b"));
    }

    /// <summary>Converte a roda de cor em deslocamentos por canal (R, G, B): matiz pelo ângulo, intensidade pelo raio.</summary>
    public static (double R, double G, double B) WheelOffset(double x, double y, double l)
    {
        var radius = Math.Min(1, Math.Sqrt(x * x + y * y));
        var angle = Math.Atan2(y, x);
        var k = radius * 0.35;
        return (k * Math.Cos(angle) + l * 0.5, k * Math.Cos(angle - 2 * Math.PI / 3) + l * 0.5, k * Math.Cos(angle + 2 * Math.PI / 3) + l * 0.5);
    }

    private static void WheelsFx(byte[] px, EffectInstance e)
    {
        var lw = WheelValue(e, "lift");
        var gw = WheelValue(e, "gamma");
        var nw = WheelValue(e, "gain");
        var lift = WheelOffset(lw.X, lw.Y, lw.L);
        var gamma = WheelOffset(gw.X, gw.Y, gw.L);
        var gain = WheelOffset(nw.X, nw.Y, nw.L);
        float[] Table(double lo, double gm, double gn)
        {
            var t = new float[256];
            for (var i = 0; i < 256; i++)
            {
                var v = i / 255.0;
                v = v + lo * (1 - v);
                v *= 1 + gn;
                v = Math.Pow(Math.Clamp(v, 0, 1), 1 / Math.Max(0.1, 1 + gm));
                t[i] = (float)v;
            }
            return t;
        }
        PerChannel(px, Table(lift.R, gamma.R, gain.R), Table(lift.G, gamma.G, gain.G), Table(lift.B, gamma.B, gain.B));
    }

    private static void LutFx(byte[] px, EffectInstance e)
    {
        CubeLut lut;
        try
        {
            lut = CubeLut.Load(e.Resource!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return;
        }
        var k = (float)(Value(e, "intensity") / 100);
        Rows(px, i =>
        {
            float b = px[i] / 255f, g = px[i + 1] / 255f, r = px[i + 2] / 255f;
            var (lr, lg, lb) = lut.Apply(r, g, b);
            px[i] = ToByte(b + (lb - b) * k);
            px[i + 1] = ToByte(g + (lg - g) * k);
            px[i + 2] = ToByte(r + (lr - r) * k);
        });
    }

    // ------------------------------------------------------------------ estilização

    private static void Matrix(byte[] px, double intensity, float[] m)
    {
        var k = (float)Math.Clamp(intensity, 0, 1);
        Rows(px, i =>
        {
            float b = px[i], g = px[i + 1], r = px[i + 2];
            var nr = m[0] * r + m[1] * g + m[2] * b;
            var ng = m[3] * r + m[4] * g + m[5] * b;
            var nb = m[6] * r + m[7] * g + m[8] * b;
            px[i] = (byte)Math.Clamp(b + (nb - b) * k, 0, 255);
            px[i + 1] = (byte)Math.Clamp(g + (ng - g) * k, 0, 255);
            px[i + 2] = (byte)Math.Clamp(r + (nr - r) * k, 0, 255);
        });
    }

    private static void InvertFx(byte[] px, double intensity)
    {
        var t = new byte[256];
        for (var i = 0; i < 256; i++) t[i] = (byte)Math.Round(i + (255 - 2 * i) * Math.Clamp(intensity, 0, 1));
        Rows(px, i => { px[i] = t[px[i]]; px[i + 1] = t[px[i + 1]]; px[i + 2] = t[px[i + 2]]; });
    }

    private static void PosterizeFx(byte[] px, int levels)
    {
        levels = Math.Clamp(levels, 2, 256);
        var t = new byte[256];
        for (var i = 0; i < 256; i++) t[i] = (byte)Math.Round(Math.Round(i / 255.0 * (levels - 1)) / (levels - 1) * 255);
        Rows(px, i => { px[i] = t[px[i]]; px[i + 1] = t[px[i + 1]]; px[i + 2] = t[px[i + 2]]; });
    }

    private static void VignetteFx(byte[] px, int w, int h, double amount, double size)
    {
        var inner = 0.25 + 0.6 * size;
        Parallel.For(0, h, y =>
        {
            var dy = (y + 0.5) / h * 2 - 1;
            for (var x = 0; x < w; x++)
            {
                var dx = (x + 0.5) / w * 2 - 1;
                var d = Math.Sqrt((dx * dx + dy * dy) / 2);
                var f = 1 - amount * SmoothStep(inner, inner + 0.55, d);
                var i = (y * w + x) * 4;
                px[i] = (byte)(px[i] * f);
                px[i + 1] = (byte)(px[i + 1] * f);
                px[i + 2] = (byte)(px[i + 2] * f);
            }
        });
    }

    private static double SmoothStep(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void PixelateFx(byte[] px, int w, int h, int block)
    {
        var rowsOfBlocks = (h + block - 1) / block;
        Parallel.For(0, rowsOfBlocks, by =>
        {
            var y0 = by * block;
            var y1 = Math.Min(h, y0 + block);
            for (var x0 = 0; x0 < w; x0 += block)
            {
                var x1 = Math.Min(w, x0 + block);
                long sb = 0, sg = 0, sr = 0, sa = 0;
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        var i = (y * w + x) * 4;
                        sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; sa += px[i + 3];
                    }
                var n = (y1 - y0) * (x1 - x0);
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        var i = (y * w + x) * 4;
                        px[i] = (byte)(sb / n); px[i + 1] = (byte)(sg / n); px[i + 2] = (byte)(sr / n); px[i + 3] = (byte)(sa / n);
                    }
            }
        });
    }

    private static void NoiseFx(byte[] px, int w, int h, double amount, long frame)
    {
        var k = amount * 90;
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var hash = (uint)(x * 374761393 + y * 668265263 + (int)frame * 1442695041);
                hash = (hash ^ (hash >> 13)) * 1274126177;
                hash ^= hash >> 16;
                var n = (int)((hash & 0xFFFF) / 65535.0 * 2 * k - k);
                var i = (y * w + x) * 4;
                px[i] = (byte)Math.Clamp(px[i] + n, 0, 255);
                px[i + 1] = (byte)Math.Clamp(px[i + 1] + n, 0, 255);
                px[i + 2] = (byte)Math.Clamp(px[i + 2] + n, 0, 255);
            }
        });
    }

    private static void BlurFx(byte[] px, int w, int h, double radius)
    {
        if (radius < 0.5) return;
        var blurred = GaussianApprox(px, w, h, radius);
        Buffer.BlockCopy(blurred, 0, px, 0, px.Length);
    }

    private static void SharpenFx(byte[] px, int w, int h, double amount, double scale)
    {
        var blurred = GaussianApprox(px, w, h, Math.Max(1, 1.5 * scale));
        var k = (float)amount;
        Rows(px, i =>
        {
            for (var c = 0; c < 3; c++)
                px[i + c] = (byte)Math.Clamp(px[i + c] + (px[i + c] - blurred[i + c]) * k, 0, 255);
        });
    }

    private static void GlowFx(byte[] px, int w, int h, double amount, double radius, double threshold)
    {
        var bright = new byte[px.Length];
        var thr = (float)Math.Clamp(threshold, 0, 0.99);
        Rows(px, i =>
        {
            var l = (0.0722f * px[i] + 0.7152f * px[i + 1] + 0.2126f * px[i + 2]) / 255f;
            var f = Math.Max(0, (l - thr) / (1 - thr));
            bright[i] = (byte)(px[i] * f); bright[i + 1] = (byte)(px[i + 1] * f); bright[i + 2] = (byte)(px[i + 2] * f);
        });
        var glow = GaussianApprox(bright, w, h, Math.Max(1, radius));
        var k = (float)amount;
        Rows(px, i =>
        {
            for (var c = 0; c < 3; c++)
            {
                var v = px[i + c] / 255f;
                var g = glow[i + c] / 255f * k;
                px[i + c] = ToByte(1 - (1 - v) * (1 - g));
            }
        });
    }

    /// <summary>Gaussiano aproximado por três passadas de média móvel separável (4 canais).</summary>
    private static byte[] GaussianApprox(byte[] src, int w, int h, double sigma)
    {
        var box = (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2);
        box = Math.Clamp(box, 1, Math.Max(1, Math.Max(w, h) / 2));
        var a = (byte[])src.Clone();
        var b = new byte[src.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxHorizontal(a, b, w, h, box);
            BoxVertical(b, a, w, h, box);
        }
        return a;
    }

    private static void BoxHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        Parallel.For(0, h, y =>
        {
            var row = y * w * 4;
            Span<int> sum = stackalloc int[4];
            var n = 2 * r + 1;
            for (var c = 0; c < 4; c++)
            {
                var s = 0;
                for (var k = -r; k <= r; k++) s += src[row + Math.Clamp(k, 0, w - 1) * 4 + c];
                sum[c] = s;
            }
            for (var x = 0; x < w; x++)
            {
                var o = row + x * 4;
                var add = row + Math.Min(w - 1, x + r + 1) * 4;
                var rem = row + Math.Max(0, x - r) * 4;
                for (var c = 0; c < 4; c++)
                {
                    dst[o + c] = (byte)(sum[c] / n);
                    sum[c] += src[add + c] - src[rem + c];
                }
            }
        });
    }

    private static void BoxVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        var stride = w * 4;
        Parallel.For(0, w, x =>
        {
            Span<int> sum = stackalloc int[4];
            var n = 2 * r + 1;
            var col = x * 4;
            for (var c = 0; c < 4; c++)
            {
                var s = 0;
                for (var k = -r; k <= r; k++) s += src[Math.Clamp(k, 0, h - 1) * stride + col + c];
                sum[c] = s;
            }
            for (var y = 0; y < h; y++)
            {
                var o = y * stride + col;
                var add = Math.Min(h - 1, y + r + 1) * stride + col;
                var rem = Math.Max(0, y - r) * stride + col;
                for (var c = 0; c < 4; c++)
                {
                    dst[o + c] = (byte)(sum[c] / n);
                    sum[c] += src[add + c] - src[rem + c];
                }
            }
        });
    }

    // ------------------------------------------------------------------ chroma key

    /// <summary>Cor-chave efetiva (predefinições verde/azul ou cor personalizada).</summary>
    public static (double R, double G, double B) KeyColor(EffectInstance e) => (int)Math.Round(Value(e, "preset")) switch
    {
        0 => (0.0, 1.0, 0.0),
        1 => (0.0, 0.0, 1.0),
        _ => ColorValue(e, "keyColor"),
    };

    /// <returns>Se a imagem resultante tem transparência.</returns>
    private static bool ChromaKeyFx(byte[] px, int w, int h, EffectInstance e, double scale)
    {
        var (kr, kg, kb) = KeyColor(e);
        var ky = 0.299 * kr + 0.587 * kg + 0.114 * kb;
        double kcb = (kb - ky) * 0.564, kcr = (kr - ky) * 0.713;
        var klen = Math.Sqrt(kcb * kcb + kcr * kcr);
        if (klen < 0.02) return false; // cor sem croma (cinza): não há o que separar
        float dirB = (float)(kcb / klen), dirR = (float)(kcr / klen);

        var tolerance = Value(e, "tolerance") / 100;
        var threshold = (float)(0.5 * (1 - tolerance) + 0.02);
        var soft = (float)(0.03 + Value(e, "softness") / 100 * 0.3);
        var spill = (float)(Value(e, "spill") / 100);
        var transparency = (float)(Value(e, "transparency") / 100);
        var edge = Value(e, "edge") * scale;
        var solid = (int)Math.Round(Value(e, "background")) == 1;
        var (bgR, bgG, bgB) = ColorValue(e, "backgroundColor");

        var alpha = new float[w * h];
        Rows(px, i =>
        {
            float b = px[i] / 255f, g = px[i + 1] / 255f, r = px[i + 2] / 255f;
            var y = 0.299f * r + 0.587f * g + 0.114f * b;
            float cb = (b - y) * 0.564f, cr = (r - y) * 0.713f;
            var along = cb * dirB + cr * dirR;
            float pb = cb - along * dirB, pr = cr - along * dirR;
            var perp = MathF.Sqrt(pb * pb + pr * pr);
            var score = (along - perp) / Math.Max(y, 0.15f);
            // Fora → 1 (mantém), dentro do limiar → 0 (remove), transição suave entre eles.
            var t = Math.Clamp((threshold - score) / soft, 0, 1);
            var a = t * t * (3 - 2 * t);
            alpha[i / 4] = a;

            if (spill > 0 && along > 0)
            {
                // Remove o reflexo da cor-chave preservando a luminância.
                cb -= spill * along * dirB;
                cr -= spill * along * dirR;
                r = y + cr / 0.713f;
                b = y + cb / 0.564f;
                g = (y - 0.299f * r - 0.114f * b) / 0.587f;
                px[i] = ToByte(b); px[i + 1] = ToByte(g); px[i + 2] = ToByte(r);
            }
        });

        if (edge >= 0.5) alpha = BlurAlpha(alpha, w, h, (int)Math.Round(edge));

        float br = (float)bgR, bgg = (float)bgG, bb = (float)bgB;
        var anyTransparent = false;
        Rows(px, i =>
        {
            var a = 1 - (1 - alpha[i / 4]) * transparency;
            var src = px[i + 3] / 255f;
            if (solid)
            {
                px[i] = ToByte(px[i] / 255f * a + bb * (1 - a));
                px[i + 1] = ToByte(px[i + 1] / 255f * a + bgg * (1 - a));
                px[i + 2] = ToByte(px[i + 2] / 255f * a + br * (1 - a));
                return;
            }
            var na = ToByte(src * a);
            if (na < 255) anyTransparent = true;
            px[i + 3] = na;
        });
        return !solid && anyTransparent;
    }

    private static float[] BlurAlpha(float[] a, int w, int h, int r)
    {
        var tmp = new float[a.Length];
        var outA = new float[a.Length];
        var n = 2f * r + 1;
        Parallel.For(0, h, y =>
        {
            var row = y * w;
            float s = 0;
            for (var k = -r; k <= r; k++) s += a[row + Math.Clamp(k, 0, w - 1)];
            for (var x = 0; x < w; x++)
            {
                tmp[row + x] = s / n;
                s += a[row + Math.Min(w - 1, x + r + 1)] - a[row + Math.Max(0, x - r)];
            }
        });
        Parallel.For(0, w, x =>
        {
            float s = 0;
            for (var k = -r; k <= r; k++) s += tmp[Math.Clamp(k, 0, h - 1) * w + x];
            for (var y = 0; y < h; y++)
            {
                outA[y * w + x] = s / n;
                s += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
            }
        });
        return outA;
    }
}
