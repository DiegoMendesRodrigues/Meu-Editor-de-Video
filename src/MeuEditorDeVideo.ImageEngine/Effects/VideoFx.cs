using MeuEditorDeVideo.Domain.Model;
using static MeuEditorDeVideo.Domain.Effects.EffectCatalog;
using V = MeuEditorDeVideo.Domain.Effects.VideoEffects;

namespace MeuEditorDeVideo.ImageEngine.Effects;

/// <summary>
/// Efeitos de vídeo animados da biblioteca (<see cref="V"/>): tudo é desenhado em código sobre a imagem BGRA, sem
/// texturas externas. A animação usa o instante dentro do clipe (<see cref="EffectContext.ClipTime"/>), então o mesmo
/// quadro sempre sai igual na prévia e na exportação.
/// </summary>
internal static class VideoFx
{
    private delegate (double X, double Y) Map(int x, int y);

    private delegate double Shape(double u, double v);

    private readonly record struct Rgb(double R, double G, double B);

    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb Black = new(0, 0, 0);

    public static bool Handles(string type) => V.IsVideoEffect(type);

    public static byte[] Apply(byte[] px, int w, int h, EffectInstance e, EffectContext ctx)
    {
        if (w < 4 || h < 4) return px;
        var style = (int)Math.Round(Value(e, "style"));
        var amount = Value(e, "amount") / 100;
        var speed = Find(e.Type)!.Parameters.Any(p => p.Key == "speed") ? Value(e, "speed") / 100 : 1;
        var t = Math.Max(0, ctx.ClipTime);
        var dur = ctx.ClipDuration > 0 ? ctx.ClipDuration : 5;
        var ts = t * speed;
        return e.Type switch
        {
            V.Shake => Shake(px, w, h, style, amount, ts),
            V.Glitch => Glitch(px, w, h, style, amount, ts),
            V.Flicker => Flicker(px, style, amount, ts),
            V.Border => Border(px, w, h, style, Value(e, "size") / 100, ColorOf(e), t),
            V.Light => Light(px, w, h, style, amount, ts),
            V.Particles => Particles(px, w, h, style, amount, Value(e, "size") / 100, ts),
            V.Retro => Retro(px, w, h, style, amount, t, ctx.Frame),
            V.Grunge => Grunge(px, w, h, style, amount, t),
            V.Magnify => Magnify(px, w, h, style, amount, Value(e, "x") / 100, Value(e, "y") / 100, t, dur),
            V.Distort => Distort(px, w, h, style, amount, ts),
            V.Split => Split(px, w, h, style, Value(e, "gap") / 100, ColorOf(e)),
            V.Viewfinder => Viewfinder(px, w, h, style, amount, t, ctx.Frame),
            V.Spotlight => Spotlight(px, w, h, style, amount, Value(e, "x") / 100, Value(e, "y") / 100, Value(e, "size") / 100, ColorOf(e)),
            V.Timer => Timer(px, w, h, style, (int)Math.Round(Value(e, "position")), Value(e, "size") / 100, ColorOf(e), t, dur),
            V.Opener => Reveal(px, w, h, style, t / Math.Max(0.05, Value(e, "duration"))),
            V.Ending => Reveal(px, w, h, style, (dur - t) / Math.Max(0.05, Value(e, "duration"))),
            _ => px,
        };
    }

    private static Rgb ColorOf(EffectInstance e)
    {
        var (r, g, b) = ColorValue(e, "color");
        return new Rgb(r * 255, g * 255, b * 255);
    }

    // ------------------------------------------------------------------ tremido

    private static byte[] Shake(byte[] px, int w, int h, int style, double a, double ts)
    {
        double ax = 0, ay = 0, rot = 0, pulse = 0, f = 6;
        var jumpy = false;
        switch (style)
        {
            case 0: ax = ay = 0.012; f = 2.5; break;
            case 1: ax = ay = 0.05; f = 9; break;
            case 2: ax = ay = 0.045; rot = 0.06; f = 14; jumpy = true; break;
            case 3: ay = 0.045; f = 7; break;
            case 4: ax = 0.045; f = 7; break;
            case 5: rot = 0.07; f = 4; break;
            default: pulse = 0.09; f = 2; break;
        }
        ax *= a;
        ay *= a;
        rot *= a;
        double nx, ny, nr;
        if (jumpy)
        {
            var step = (int)Math.Floor(ts * f);
            nx = Hash(step, 5) * 2 - 1;
            ny = Hash(step, 6) * 2 - 1;
            nr = Hash(step, 7) * 2 - 1;
        }
        else
        {
            nx = Smooth(ts * f, 11);
            ny = Smooth(ts * f, 23);
            nr = Smooth(ts * f, 37);
        }
        var dx = ax * w * nx;
        var dy = ay * h * ny;
        var r = rot * nr;
        // Amplia o suficiente para as bordas nunca aparecerem (pela amplitude máxima: o zoom fica estável).
        var cover = 1 + 2 * Math.Max(ax, ay) + rot * 1.3;
        var beat = pulse > 0 ? 1 + pulse * a * Math.Pow(Math.Abs(Math.Sin(ts * Math.PI * f / 2)), 4) : 1;
        var outPx = Affine(px, w, h, dx, dy, cover * beat, r);
        if (jumpy) outPx = ChannelShift(outPx, w, h, (int)Math.Round(dx * 0.35), 0);
        return outPx;
    }

    // ------------------------------------------------------------------ falha digital

    private static byte[] Glitch(byte[] px, int w, int h, int style, double a, double ts)
    {
        var seed = (int)Math.Floor(ts * 10);
        var on = Hash(seed, 3) < 0.35 + 0.6 * a;
        var k = a * (on ? 1 : 0.2);
        if (k <= 0) return px;
        switch (style)
        {
            case 0:
            {
                var outPx = (byte[])px.Clone();
                var bands = 3 + (int)(6 * a);
                for (var j = 0; j < bands; j++)
                {
                    var y0 = (int)(Hash(seed, j * 3) * h);
                    var bh = (int)((0.02 + 0.08 * Hash(seed, j * 3 + 1)) * h);
                    var off = (int)((Hash(seed, j * 3 + 2) * 2 - 1) * 0.12 * w * k);
                    ShiftRows(px, outPx, w, h, y0, y0 + bh, off);
                }
                outPx = ChannelShift(outPx, w, h, (int)Math.Round(0.012 * w * k), 0);
                for (var j = 0; j < 2 + (int)(3 * a); j++)
                {
                    if (Hash(seed, 90 + j) > 0.6) continue;
                    double x0 = Hash(seed, 100 + j) * w, y0 = Hash(seed, 110 + j) * h;
                    var c = Hash(seed, 120 + j) < 0.5 ? new Rgb(0, 255, 255) : new Rgb(255, 0, 200);
                    FillRect(outPx, w, h, x0, y0, x0 + (0.05 + 0.2 * Hash(seed, 130 + j)) * w, y0 + 0.02 * h, c, 0.35 * k, screen: true);
                }
                return outPx;
            }
            case 1:
                return ChannelShift(px, w, h, (int)Math.Round((0.006 + 0.02 * k) * w * (1 + 0.5 * Math.Sin(ts * 7))), (int)Math.Round(0.004 * h * k * Smooth(ts * 5, 2)));
            case 2:
            {
                var outPx = (byte[])px.Clone();
                var band = Math.Max(2, h / 160);
                for (var y0 = 0; y0 < h; y0 += band)
                {
                    var b = y0 / band;
                    if (Hash(b, seed + 7) < 0.25 * k + 0.05)
                        ShiftRows(px, outPx, w, h, y0, y0 + band, (int)((Hash(b, seed) * 2 - 1) * 0.08 * w * k));
                }
                return outPx;
            }
            case 3:
            {
                var outPx = (byte[])px.Clone();
                var n = 6 + (int)(12 * a);
                for (var j = 0; j < n; j++)
                {
                    if (!on && j > 2) break;
                    int bw = (int)((0.04 + 0.12 * Hash(seed, 200 + j)) * w), bh = (int)((0.03 + 0.08 * Hash(seed, 210 + j)) * h);
                    int x0 = (int)(Hash(seed, 220 + j) * (w - bw)), y0 = (int)(Hash(seed, 230 + j) * (h - bh));
                    int sx = (int)(Hash(seed, 240 + j) * (w - bw)), sy = (int)(Hash(seed, 250 + j) * (h - bh));
                    var swap = Hash(seed, 260 + j) < 0.5;
                    for (var y = 0; y < bh; y++)
                    {
                        for (var x = 0; x < bw; x++)
                        {
                            var d = ((y0 + y) * w + x0 + x) * 4;
                            var s = ((sy + y) * w + sx + x) * 4;
                            outPx[d] = px[swap ? s + 2 : s];
                            outPx[d + 1] = px[s + 1];
                            outPx[d + 2] = px[swap ? s : s + 2];
                        }
                    }
                }
                return ChannelShift(outPx, w, h, (int)Math.Round(0.006 * w * k), 0);
            }
            case 4:
            {
                var roll = (int)((ts * 0.35 % 1) * h * 0.15 * k);
                var outPx = Warp(px, w, h, (x, y) => (x + 0.01 * w * k * Math.Sin(y * 0.08 + ts * 20), (y + roll) % h));
                Desaturate(outPx, 0.4 * k);
                EffectProcessor.NoiseFx(outPx, w, h, 0.35 * k, seed);
                Scanlines(outPx, w, h, 0.25 * k, Math.Max(2, h / 200));
                return outPx;
            }
            default:
            {
                var outPx = (byte[])px.Clone();
                var block = Math.Max(4, Math.Min(w, h) / 30);
                for (var by = 0; by < h; by += block)
                {
                    for (var bx = 0; bx < w; bx += block)
                    {
                        if (Hash(bx / block + by / block * 977, seed) > 0.1 * k + 0.02) continue;
                        var s = (Math.Min(h - 1, by + block / 2) * w + Math.Min(w - 1, bx + block / 2)) * 4;
                        var bright = Hash(bx, by + seed) < 0.3;
                        for (var y = by; y < Math.Min(h, by + block); y++)
                        {
                            for (var x = bx; x < Math.Min(w, bx + block); x++)
                            {
                                var d = (y * w + x) * 4;
                                outPx[d] = bright ? (byte)255 : (byte)(px[s] & 0xC0);
                                outPx[d + 1] = bright ? (byte)0 : (byte)(px[s + 1] & 0xC0);
                                outPx[d + 2] = bright ? (byte)255 : (byte)(px[s + 2] & 0xC0);
                            }
                        }
                    }
                }
                return outPx;
            }
        }
    }

    // ------------------------------------------------------------------ cintilação

    private static byte[] Flicker(byte[] px, int style, double a, double ts)
    {
        switch (style)
        {
            case 0:
                if (Hash((int)Math.Floor(ts * 8), 1) < 0.45) Multiply(px, 1 - a * 0.75, 1 - a * 0.75, 1 - a * 0.75);
                break;
            case 1:
                if (ts * 6 % 1 < 0.25) BlendAll(px, White, a * 0.85);
                break;
            case 2:
            {
                var m = 1 - a * 0.55 * (0.5 + 0.5 * Smooth(ts * 12, 4));
                Multiply(px, m, m, m);
                break;
            }
            case 3:
                if (Hash((int)Math.Floor(ts * 5), 2) < 0.5) Desaturate(px, a);
                break;
            default:
            {
                Rgb[] tints = [new(255, 60, 200), new(60, 230, 255), new(255, 230, 60)];
                var c = tints[(int)Math.Floor(ts * 6) % 3];
                var k = a * 0.5;
                Multiply(px, 1 - k + k * c.R / 255, 1 - k + k * c.G / 255, 1 - k + k * c.B / 255);
                break;
            }
        }
        return px;
    }

    // ------------------------------------------------------------------ bordas

    private static byte[] Border(byte[] px, int w, int h, int style, double size, Rgb color, double t)
    {
        var m = Math.Min(w, h);
        var bw = Math.Max(1, size * m);
        switch (style)
        {
            case 0:
                FrameFill(px, w, h, (x, y) => x < bw || y < bw || x >= w - bw || y >= h - bw, color);
                return px;
            case 1:
            {
                double cx = w / 2.0, cy = h / 2.0, hx = w / 2.0 - bw * 1.2, hy = h / 2.0 - bw * 1.2, spread = bw * 0.45;
                var pulse = 0.8 + 0.2 * Math.Sin(t * 4);
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = Math.Abs(SdBox(x - cx, y - cy, hx, hy));
                        var glow = Math.Exp(-(d * d) / (spread * spread)) * pulse;
                        if (glow < 0.01) continue;
                        var i = (y * w + x) * 4;
                        Screen(px, i, color, glow);
                        if (d < bw * 0.1) Screen(px, i, White, 0.8);
                    }
                });
                return px;
            }
            case 2:
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (!(x < bw || y < bw || x >= w - bw || y >= h - bw)) continue;
                        Blend(px, (y * w + x) * 4, HueRgb((x + y) / (double)(w + h) * 360 * 2 + t * 120), 1);
                    }
                });
                return px;
            case 3:
            {
                var band = bw * 1.6;
                var gap = bw * 1.4;
                var scroll = t * h * 0.3 % gap;
                var film = new Rgb(18, 18, 18);
                FrameFill(px, w, h, (x, y) => x < band || x >= w - band, film);
                for (var y0 = -gap + scroll; y0 < h; y0 += gap)
                {
                    foreach (var x0 in new[] { band * 0.25, w - band * 0.75 })
                        RoundRect(px, w, h, x0, y0, x0 + band * 0.5, y0 + gap * 0.55, bw * 0.12, new Rgb(235, 232, 220), 1);
                }
                return px;
            }
            case 4:
            {
                double l = bw, top = bw, r = w - bw, bottom = h - bw * 3.2;
                var outPx = Inset(px, w, h, l, top, r, bottom, color);
                return outPx;
            }
            case 5:
            {
                var radius = bw * 2.5;
                double cx = w / 2.0, cy = h / 2.0, hx = w / 2.0 - bw, hy = h / 2.0 - bw;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = SdRoundBox(x + 0.5 - cx, y + 0.5 - cy, hx, hy, radius);
                        if (d > -0.5) Blend(px, (y * w + x) * 4, color, Math.Clamp(d + 0.5, 0, 1));
                    }
                });
                return px;
            }
            default:
            {
                var bg = (byte[])px.Clone();
                EffectProcessor.BlurFx(bg, w, h, Math.Max(2, 0.03 * w));
                Multiply(bg, 0.85, 0.85, 0.85);
                var s = Math.Max(0.4, 1 - 2 * size);
                double ix = w * (1 - s) / 2, iy = h * (1 - s) / 2;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (x < ix || y < iy || x >= w - ix || y >= h - iy) continue;
                        Sample(px, w, h, (x - ix) / s, (y - iy) / s, bg, (y * w + x) * 4, false);
                    }
                });
                return bg;
            }
        }
    }

    // ------------------------------------------------------------------ luz

    private static byte[] Light(byte[] px, int w, int h, int style, double a, double ts)
    {
        var m = (double)Math.Min(w, h);
        switch (style)
        {
            case 0:
            {
                (double X, double Y, Rgb C, double R)[] blobs =
                [
                    (0.1 + 0.15 * Math.Sin(ts * 0.7), 0.2 + 0.1 * Math.Cos(ts * 0.5), new Rgb(255, 120, 40), 0.6),
                    (0.95 - 0.1 * Math.Sin(ts * 0.6), 0.85, new Rgb(255, 50, 90), 0.5),
                    (0.5 + 0.35 * Math.Sin(ts * 0.3), -0.1, new Rgb(255, 205, 90), 0.55),
                ];
                LightField(px, w, h, (fx, fy) =>
                {
                    double r = 0, g = 0, b = 0;
                    foreach (var (bx, by, c, rad) in blobs)
                    {
                        double dx = (fx - bx) * w / m, dy = (fy - by) * h / m;
                        var k = Math.Exp(-(dx * dx + dy * dy) / (rad * rad)) * a;
                        r += c.R * k; g += c.G * k; b += c.B * k;
                    }
                    return new Rgb(r, g, b);
                });
                return px;
            }
            case 1:
            {
                double sx = (0.25 + 0.5 * (0.5 + 0.5 * Math.Sin(ts * 0.4))) * w / m, sy = 0.25 * h / m;
                double cx = 0.5 * w / m, cy = 0.5 * h / m;
                double[] ghosts = [0.55, 0.8, 1.25, 1.6];
                LightField(px, w, h, (fx, fy) =>
                {
                    double x = fx * w / m, y = fy * h / m, dx = x - sx, dy = y - sy, d2 = dx * dx + dy * dy;
                    var core = Math.Exp(-d2 / 0.003) * 255 + Math.Exp(-d2 / 0.04) * 120;
                    var streak = Math.Exp(-dy * dy / 0.00025) * Math.Exp(-dx * dx / 0.2) * 170;
                    double r = core + streak * 0.7, g = core * 0.92 + streak * 0.8, b = core * 0.8 + streak;
                    for (var i = 0; i < ghosts.Length; i++)
                    {
                        double gx = sx + (cx - sx) * 2 * ghosts[i], gy = sy + (cy - sy) * 2 * ghosts[i];
                        var rad = 0.03 + 0.025 * i;
                        var d = Math.Sqrt((x - gx) * (x - gx) + (y - gy) * (y - gy));
                        if (d >= rad) continue;
                        var k = (0.35 + 0.65 * d / rad) * 60;
                        var c = HueRgb(30 + i * 70);
                        r += c.R / 255 * k; g += c.G / 255 * k; b += c.B / 255 * k;
                    }
                    return new Rgb(r * a, g * a, b * a);
                });
                return px;
            }
            case 2:
            {
                double ox = -0.1 * w, oy = -0.15 * h;
                LightField(px, w, h, (fx, fy) =>
                {
                    double x = fx * w - ox, y = fy * h - oy;
                    var ang = Math.Atan2(y, x);
                    var rays = Math.Pow(0.5 + 0.5 * Math.Sin(ang * 34 + Smooth(ang * 9 + ts * 0.6, 3) * 2.5 + ts * 0.4), 6);
                    var fall = Math.Exp(-Math.Sqrt(x * x + y * y) / (1.1 * Math.Max(w, h)));
                    var k = rays * fall * a * 0.9;
                    return new Rgb(255 * k, 235 * k, 175 * k);
                });
                return px;
            }
            case 3:
            case 6:
            {
                var pink = style == 6;
                if (pink)
                {
                    LightField(px, w, h, (fx, fy) =>
                    {
                        var k = Math.Exp(-(fx * fx + fy * fy) / 0.6) * a * 0.7 + 0.12 * a;
                        return new Rgb(255 * k, 110 * k, 170 * k);
                    });
                }
                Rgb[] palette = pink
                    ? [new(255, 140, 200), new(255, 200, 230), new(255, 90, 160)]
                    : [new(255, 205, 120), new(255, 120, 170), new(110, 220, 255), new(255, 245, 210)];
                var n = 18;
                for (var i = 0; i < n; i++)
                {
                    var x = (Hash(i, 51) + ts * 0.015 * (Hash(i, 52) - 0.5)) % 1 * w;
                    var y = (Hash(i, 53) + 0.02 * Math.Sin(ts * 0.8 + i)) * h;
                    var rad = (0.04 + 0.08 * Hash(i, 54)) * m;
                    var c = palette[i % palette.Length];
                    Stamp(px, w, h, x, y, rad, BokehDisc, c, a * (0.25 + 0.25 * Hash(i, 55)), screen: true);
                }
                return px;
            }
            case 4:
                LightField(px, w, h, (fx, fy) =>
                {
                    double dx = (fx - 0.85) * w / m, dy = (fy - 0.08) * h / m;
                    var k = Math.Exp(-(dx * dx + dy * dy) / 0.15) * a + 0.08 * a;
                    return new Rgb(255 * k, 205 * k, 140 * k);
                });
                return px;
            default:
            {
                var dim = 1 - 0.3 * a;
                Multiply(px, dim, dim, dim);
                var spots = new (double X, double Y, Rgb C)[6];
                for (var i = 0; i < spots.Length; i++)
                {
                    spots[i] = (0.5 + 0.45 * Math.Sin(ts * (0.6 + 0.15 * i) + i * 1.3), 0.5 + 0.4 * Math.Cos(ts * (0.5 + 0.12 * i) + i * 2.1), HueRgb(i * 60 + ts * 40));
                }
                LightField(px, w, h, (fx, fy) =>
                {
                    double r = 0, g = 0, b = 0;
                    foreach (var (sx, sy, c) in spots)
                    {
                        double dx = (fx - sx) * w / m, dy = (fy - sy) * h / m;
                        var k = Math.Exp(-(dx * dx + dy * dy) / 0.06) * a * 0.75;
                        r += c.R * k; g += c.G * k; b += c.B * k;
                    }
                    return new Rgb(r, g, b);
                });
                return px;
            }
        }
    }

    // ------------------------------------------------------------------ partículas

    private static byte[] Particles(byte[] px, int w, int h, int style, double a, double size, double ts)
    {
        var m = (double)Math.Min(w, h);
        var n = 6 + (int)(a * 54);
        var baseR = m * 0.035 * size;
        Rgb[] party = [new(255, 70, 90), new(255, 200, 40), new(60, 200, 255), new(120, 230, 90), new(200, 90, 255), new(255, 130, 40)];
        switch (style)
        {
            case 0:
                for (var i = 0; i < n; i++)
                {
                    double hs = Hash(i, 303), y = (1.1 - (Hash(i, 202) + ts * (0.08 + 0.08 * hs)) % 1.2) * h;
                    var x = (Hash(i, 101) + 0.03 * Math.Sin(ts * 2 + i)) * w;
                    var c = Hash(i, 404) < 0.5 ? new Rgb(255, 60, 110) : new Rgb(255, 140, 180);
                    Stamp(px, w, h, x, y, baseR * (0.6 + 0.8 * hs), Heart, c, 0.9);
                }
                break;
            case 1:
                for (var i = 0; i < n; i++)
                {
                    double hs = Hash(i, 303), y = ((Hash(i, 202) + ts * (0.15 + 0.15 * hs)) % 1.2 - 0.1) * h;
                    var x = (Hash(i, 101) + 0.02 * Math.Sin(ts * 3 + i)) * w;
                    var ang = ts * 5 * (hs - 0.5) * 2 + i;
                    double cos = Math.Cos(ang), sin = Math.Sin(ang);
                    Stamp(px, w, h, x, y, baseR * 0.5 * (0.7 + 0.6 * hs), (u, v) => Box(u * cos + v * sin, -u * sin + v * cos, 1, 0.5),
                        party[i % party.Length], 0.95);
                }
                break;
            case 2:
                for (var i = 0; i < n; i++)
                {
                    var hs = Hash(i, 303);
                    var tw = Math.Pow(Math.Max(0, Math.Sin(ts * 4 * (0.5 + hs) + i * 1.7)), 2);
                    var c = Hash(i, 404) < 0.5 ? White : new Rgb(255, 220, 120);
                    Stamp(px, w, h, Hash(i, 101) * w, Hash(i, 202) * h, baseR * (0.6 + hs), Sparkle, c, tw, screen: true);
                }
                break;
            case 3:
                for (var i = 0; i < n * 2; i++)
                {
                    double hs = Hash(i, 303), y = ((Hash(i, 202) + ts * (0.06 + 0.06 * hs)) % 1.1 - 0.05) * h;
                    var x = (Hash(i, 101) + 0.03 * Math.Sin(ts * 1.5 + i)) * w;
                    Stamp(px, w, h, x, y, baseR * 0.35 * (0.5 + hs), SoftDisc, White, 0.9);
                }
                break;
            case 4:
                for (var i = 0; i < n; i++)
                {
                    var hs = Hash(i, 303);
                    var tw = 0.45 + 0.55 * Math.Abs(Math.Sin(ts * 2.5 * (0.5 + hs) + i));
                    Stamp(px, w, h, Hash(i, 101) * w, Hash(i, 202) * h, baseR * 0.7 * (0.6 + hs), Star, new Rgb(255, 225, 70), tw);
                }
                break;
            case 5:
                for (var i = 0; i < n; i++)
                {
                    double hs = Hash(i, 303), y = (1.1 - (Hash(i, 202) + ts * (0.06 + 0.07 * hs)) % 1.2) * h;
                    var x = (Hash(i, 101) + 0.025 * Math.Sin(ts * 2 + i)) * w;
                    var r = baseR * (0.5 + hs);
                    Stamp(px, w, h, x, y, r, Ring, new Rgb(235, 245, 255), 0.75);
                    Stamp(px, w, h, x - r * 0.35, y - r * 0.35, r * 0.22, SoftDisc, White, 0.8);
                }
                break;
            case 6:
            {
                var bursts = 2 + (int)(a * 4);
                const double Period = 1.6;
                for (var b = 0; b < bursts; b++)
                {
                    var shifted = ts + b * Period / bursts;
                    var cycle = (int)Math.Floor(shifted / Period);
                    var lt = shifted - cycle * Period;
                    double cx = (0.15 + 0.7 * Hash(cycle, b + 10)) * w, cy = (0.12 + 0.35 * Hash(cycle, b + 20)) * h;
                    var c = party[(cycle + b) % party.Length];
                    var p = Math.Min(1, lt / 1.1);
                    var reach = baseR * 6 * (1 - Math.Pow(1 - p, 3));
                    var fade = Math.Max(0, 1 - lt / Period);
                    const int Sparks = 26;
                    for (var j = 0; j < Sparks; j++)
                    {
                        var ang = Math.PI * 2 * j / Sparks;
                        double ex = cx + Math.Cos(ang) * reach, ey = cy + Math.Sin(ang) * reach + p * p * baseR * 1.5;
                        Line(px, w, h, cx + Math.Cos(ang) * reach * 0.65, cy + Math.Sin(ang) * reach * 0.65 + p * p * baseR, ex, ey,
                            Math.Max(1, baseR * 0.12), c, fade * 0.8, screen: true);
                        Stamp(px, w, h, ex, ey, Math.Max(1.5, baseR * 0.2), SoftDisc, White, fade, screen: true);
                    }
                }
                break;
            }
            default:
            {
                var dim = 1 - 0.18 * a;
                Multiply(px, dim, dim, dim * 1.02);
                for (var i = 0; i < n * 2; i++)
                {
                    var hs = Hash(i, 303);
                    var y = ((Hash(i, 202) + ts * (1.2 + 0.6 * hs)) % 1.2 - 0.1) * h;
                    var x = Hash(i, 101) * w;
                    var len = baseR * (1.2 + hs);
                    Line(px, w, h, x, y, x - len * 0.15, y + len, Math.Max(1, baseR * 0.06), new Rgb(205, 215, 235), 0.55);
                }
                break;
            }
        }
        return px;
    }

    // ------------------------------------------------------------------ retrô

    private static byte[] Retro(byte[] px, int w, int h, int style, double a, double t, long frame)
    {
        var lineH = Math.Max(2, h / 200);
        switch (style)
        {
            case 0:
            case 4:
            {
                var damaged = style == 4;
                var outPx = ChannelShift(px, w, h, (int)Math.Round(0.006 * w * a * (damaged ? 2 : 1)), 0);
                EffectProcessor.BlurFx(outPx, w, h, Math.Max(0.6, 0.0012 * w));
                Desaturate(outPx, 0.2 * a);
                var dst = (byte[])outPx.Clone();
                var bandY = (t * 0.25 % 1.3 - 0.15) * h;
                var bandH = (damaged ? 0.08 : 0.035) * h;
                var seed = (int)(frame / 2);
                for (var y = Math.Max(0, (int)bandY); y < Math.Min(h, (int)(bandY + bandH)); y++)
                    ShiftRows(outPx, dst, w, h, y, y + 1, (int)((Hash(y, seed) * 2 - 1) * 0.03 * w * a * (damaged ? 3 : 1)));
                if (damaged)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        var y0 = (int)(Hash(seed, j + 40) * h);
                        ShiftRows(outPx, dst, w, h, y0, y0 + (int)(0.03 * h * Hash(seed, j + 50)), (int)((Hash(seed, j + 60) * 2 - 1) * 0.1 * w * a));
                    }
                }
                Scanlines(dst, w, h, 0.15 * a, lineH);
                EffectProcessor.NoiseFx(dst, w, h, 0.15 * a, frame);
                // Ruído de rastreamento na base da imagem.
                var noiseTop = (int)(h * (damaged ? 0.9 : 0.95));
                for (var y = noiseTop; y < h; y++)
                    for (var x = 0; x < w; x++)
                        if (Hash(x / 3 + y * 1013, seed) < 0.25 * a) Blend(dst, (y * w + x) * 4, White, 0.7);
                return dst;
            }
            case 1:
            {
                Sepia(px, a * 0.85);
                var flick = 1 + (Hash((int)frame, 9) - 0.5) * 0.16 * a;
                Multiply(px, flick, flick, flick);
                EffectProcessor.NoiseFx(px, w, h, 0.35 * a, frame);
                var seed = (int)(t * 8);
                for (var j = 0; j < 3; j++)
                {
                    if (Hash(seed, j) > 0.7) continue;
                    var x = Hash(seed, j + 10) * w;
                    Line(px, w, h, x, 0, x + (Hash(seed, j + 20) - 0.5) * 0.02 * w, h, Math.Max(1, w / 900.0), new Rgb(230, 220, 200), 0.5 * a);
                }
                for (var j = 0; j < 12 * a; j++)
                    Stamp(px, w, h, Hash(seed, j + 30) * w, Hash(seed, j + 40) * h, Math.Max(1, Math.Min(w, h) * 0.004 * (0.5 + Hash(seed, j + 50))), SoftDisc, new Rgb(30, 25, 20), 0.7);
                EffectProcessor.VignetteFx(px, w, h, 0.6 * a, 0.5);
                return px;
            }
            case 2:
            {
                var k = 0.12 * a;
                var outPx = Warp(px, w, h, (x, y) =>
                {
                    double u = x / (double)w * 2 - 1, v = y / (double)h * 2 - 1;
                    var r2 = u * u + v * v;
                    var f = 1 + k * r2;
                    return ((u * f + 1) / 2 * w, (v * f + 1) / 2 * h);
                }, black: true);
                Desaturate(outPx, 0.3 * a);
                outPx = ChannelShift(outPx, w, h, (int)Math.Round(0.002 * w * a), 0);
                Scanlines(outPx, w, h, 0.28 * a, lineH);
                EffectProcessor.VignetteFx(outPx, w, h, 0.55 * a, 0.6);
                return outPx;
            }
            case 3:
                Scanlines(px, w, h, 0.45 * a, lineH);
                return px;
            default:
            {
                var outPx = Affine(px, w, h, (Hash((int)frame, 3) - 0.5) * 0.006 * w * a, (Hash((int)frame, 4) - 0.5) * 0.006 * h * a, 1.02, 0);
                Multiply(outPx, 1.06, 1.0, 0.86);
                Desaturate(outPx, 0.15 * a);
                EffectProcessor.NoiseFx(outPx, w, h, 0.3 * a, frame);
                EffectProcessor.VignetteFx(outPx, w, h, 0.8 * a, 0.4);
                return outPx;
            }
        }
    }

    // ------------------------------------------------------------------ grunge

    private static byte[] Grunge(byte[] px, int w, int h, int style, double a, double t)
    {
        var m = (double)Math.Min(w, h);
        var seed = (int)(t * 8);
        switch (style)
        {
            case 0:
                for (var j = 0; j < 4 + (int)(25 * a); j++)
                {
                    double x = Hash(seed, j) * w, y = Hash(seed, j + 100) * h, len = (0.1 + 0.5 * Hash(seed, j + 200)) * h;
                    var ang = Math.PI / 2 + (Hash(seed, j + 300) - 0.5) * 0.5;
                    var c = Hash(seed, j + 400) < 0.6 ? new Rgb(235, 230, 220) : new Rgb(25, 20, 15);
                    Line(px, w, h, x, y, x + Math.Cos(ang) * len, y + Math.Sin(ang) * len, Math.Max(1, m / 700), c, 0.55);
                }
                break;
            case 1:
            {
                var dustSeed = (int)(t * 12);
                for (var j = 0; j < 10 + (int)(150 * a); j++)
                {
                    var c = Hash(dustSeed, j + 500) < 0.7 ? new Rgb(20, 18, 15) : new Rgb(240, 236, 228);
                    Stamp(px, w, h, Hash(dustSeed, j) * w, Hash(dustSeed, j + 1000) * h, Math.Max(0.8, m * 0.004 * (0.3 + Hash(dustSeed, j + 2000))), SoftDisc, c, 0.75);
                }
                break;
            }
            case 2:
                Desaturate(px, 0.3 * a);
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var n = 0.82 + 0.18 * Fbm(x / m * 6, y / m * 6, 7);
                        var i = (y * w + x) * 4;
                        var k = a;
                        px[i] = ToByte(px[i] * (1 - k + k * 190 / 255.0 * n));
                        px[i + 1] = ToByte(px[i + 1] * (1 - k + k * 220 / 255.0 * n));
                        px[i + 2] = ToByte(px[i + 2] * (1 - k + k * 240 / 255.0 * n));
                    }
                });
                EffectProcessor.VignetteFx(px, w, h, 0.5 * a, 0.5);
                break;
            case 3:
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var blot = Math.Clamp((Fbm(x / m * 5, y / m * 5, 11) - 0.45) * 3, 0, 1) * 0.6 * a;
                        if (blot <= 0) continue;
                        var i = (y * w + x) * 4;
                        px[i] = ToByte(px[i] * (1 - blot * 1.1));
                        px[i + 1] = ToByte(px[i + 1] * (1 - blot));
                        px[i + 2] = ToByte(px[i + 2] * (1 - blot * 0.85));
                    }
                });
                break;
            case 4:
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var edge = Math.Min(Math.Min(x, w - x), Math.Min(y, h - y)) / m + (Fbm(x / m * 8, y / m * 8, 13) - 0.5) * 0.08;
                        var reach = 0.03 + 0.12 * a;
                        if (edge > reach) continue;
                        var i = (y * w + x) * 4;
                        var k = 1 - edge / reach;
                        if (k > 0.55) Blend(px, i, new Rgb(12, 6, 2), Math.Min(1, (k - 0.55) * 4));
                        else Blend(px, i, new Rgb(140, 70, 20), k / 0.55 * 0.6);
                    }
                });
                break;
            default:
            {
                var paper = new Rgb(242, 238, 228);
                var depth = (0.03 + 0.06 * a) * h;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var jag = Hash(x / Math.Max(1, w / 120), 17) * 0.4 + Fbm(x / m * 10, 0.5, 19) * 0.6;
                        var topEdge = depth * (0.6 + 0.8 * jag);
                        var botEdge = h - depth * (0.6 + 0.8 * Fbm(x / m * 10, 3.5, 23));
                        var i = (y * w + x) * 4;
                        if (y < topEdge || y > botEdge) Blend(px, i, paper, 1);
                        else if (y < topEdge + 3 || y > botEdge - 3) Blend(px, i, Black, 0.25);
                    }
                });
                break;
            }
        }
        return px;
    }

    // ------------------------------------------------------------------ lupa e zoom

    private static byte[] Magnify(byte[] px, int w, int h, int style, double a, double cx, double cy, double t, double dur)
    {
        var p = Math.Clamp(t / dur, 0, 1);
        var m = (double)Math.Min(w, h);
        switch (style)
        {
            case 0: return View(px, w, h, cx, cy, 1 + a * 1.5);
            case 1:
            {
                double ox = cx * w, oy = cy * h, rad = (0.15 + 0.15 * a) * m, zoom = 1.6 + a * 1.2;
                var outPx = Warp(px, w, h, (x, y) =>
                {
                    double dx = x - ox, dy = y - oy;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d >= rad) return (x, y);
                    var k = 1 / zoom * (1 + 0.25 * Math.Pow(d / rad, 4));
                    return (ox + dx * k, oy + dy * k);
                });
                Stamp(outPx, w, h, ox, oy, rad * 1.08, (u, v) => Math.Sqrt(u * u + v * v) is var r && r > 0.92 && r < 1 ? 1 : 0, new Rgb(40, 40, 45), 1);
                Stamp(outPx, w, h, ox - rad * 0.4, oy - rad * 0.45, rad * 0.25, SoftDisc, White, 0.35, screen: true);
                return outPx;
            }
            case 2:
            {
                double ox = cx * w, oy = cy * h, rmax = Math.Sqrt(w * w + h * h) / 2, k = 0.4 + a * 0.9;
                return Warp(px, w, h, (x, y) =>
                {
                    double dx = x - ox, dy = y - oy, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 1e-6) return (x, y);
                    var f = Math.Pow(d / rmax, k);
                    return (ox + dx / d * f * rmax, oy + dy / d * f * rmax);
                }, black: true);
            }
            case 3: return View(px, w, h, cx, cy, 1 + a * 0.3 * Math.Pow(Math.Abs(Math.Sin(t * Math.PI * 2)), 3));
            case 4: return View(px, w, h, cx, cy, 1 + a * 0.4 * p);
            case 5: return View(px, w, h, cx, cy, 1 + a * 0.4 * (1 - p));
            default:
            {
                var s = 1 + a * 0.4;
                double lo = 0.5 / s, hi = 1 - 0.5 / s;
                return style switch
                {
                    6 => View(px, w, h, hi + (lo - hi) * p, 0.5, s),
                    7 => View(px, w, h, lo + (hi - lo) * p, 0.5, s),
                    8 => View(px, w, h, 0.5, hi + (lo - hi) * p, s),
                    _ => View(px, w, h, 0.5, lo + (hi - lo) * p, s),
                };
            }
        }
    }

    /// <summary>Mostra a imagem ampliada <paramref name="s"/> vezes, com o centro da vista em (vx, vy) (frações), sem expor bordas.</summary>
    private static byte[] View(byte[] px, int w, int h, double vx, double vy, double s)
    {
        s = Math.Max(1, s);
        vx = Math.Clamp(vx, 0.5 / s, 1 - 0.5 / s) * w;
        vy = Math.Clamp(vy, 0.5 / s, 1 - 0.5 / s) * h;
        return Warp(px, w, h, (x, y) => (vx + (x - w / 2.0) / s, vy + (y - h / 2.0) / s));
    }

    // ------------------------------------------------------------------ distorção

    private static byte[] Distort(byte[] px, int w, int h, int style, double a, double ts)
    {
        double cx = w / 2.0, cy = h / 2.0, m = Math.Min(w, h);
        return style switch
        {
            0 => Warp(px, w, h, (x, y) => (x + a * 0.03 * w * Math.Sin(y / (double)h * Math.PI * 6 + ts * 3), y + a * 0.015 * h * Math.Sin(x / (double)w * Math.PI * 4 + ts * 2))),
            1 => Warp(px, w, h, (x, y) =>
            {
                double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy), rad = 0.55 * m;
                if (d >= rad) return (x, y);
                var ang = a * 2.8 * Math.Pow(1 - d / rad, 2) * (1 + 0.25 * Math.Sin(ts * 1.5));
                double c = Math.Cos(ang), s = Math.Sin(ang);
                return (cx + dx * c - dy * s, cy + dx * s + dy * c);
            }),
            2 => Warp(px, w, h, (x, y) =>
            {
                double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1e-6) return (x, y);
                var off = a * 0.015 * m * Math.Sin(d / m * 40 - ts * 6);
                return (x + dx / d * off, y + dy / d * off);
            }),
            3 => Warp(px, w, h, (x, y) =>
            {
                double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy), rad = (0.35 + 0.05 * Math.Sin(ts * 2)) * m;
                if (d >= rad || d < 1e-6) return (x, y);
                var f = Math.Pow(d / rad, a * 0.8);
                return (cx + dx / d * f * rad, cy + dy / d * f * rad);
            }),
            4 => Warp(px, w, h, (x, y) => (x > w / 2 ? w - 1 - x : x, y)),
            5 => Warp(px, w, h, (x, y) =>
            {
                double dx = x - cx, dy = y - cy, r = Math.Sqrt(dx * dx + dy * dy) * (1 - 0.3 * a);
                var seg = Math.PI / 3;
                var ang = Math.Atan2(dy, dx) + ts * 0.3 * a;
                ang = ((ang % seg) + seg) % seg;
                if (ang > seg / 2) ang = seg - ang;
                return (cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r);
            }),
            _ => Warp(px, w, h, (x, y) => (x + a * 0.005 * w * Math.Sin(y * 0.12 + ts * 9) * Math.Sin(x * 0.03 + ts * 3), y + a * 0.004 * h * Math.Sin(x * 0.1 + ts * 7))),
        };
    }

    // ------------------------------------------------------------------ tela dividida

    private static byte[] Split(byte[] px, int w, int h, int style, double gap, Rgb color)
    {
        var (cols, rows) = style switch { 0 or 5 => (2, 1), 1 => (3, 1), 2 or 6 => (1, 2), 3 => (2, 2), _ => (3, 3) };
        var g = gap * Math.Min(w, h);
        double cw = (w - g * (cols + 1)) / cols, ch = (h - g * (rows + 1)) / rows;
        if (cw < 2 || ch < 2) return px;
        var scale = Math.Max(cw / w, ch / h);
        var dst = new byte[px.Length];
        Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                double gx = x - g, gy = y - g;
                var col = (int)Math.Floor(gx / (cw + g));
                var row = (int)Math.Floor(gy / (ch + g));
                double lx = gx - col * (cw + g), ly = gy - row * (ch + g);
                if (col < 0 || row < 0 || col >= cols || row >= rows || lx >= cw || ly >= ch)
                {
                    dst[i] = (byte)color.B; dst[i + 1] = (byte)color.G; dst[i + 2] = (byte)color.R; dst[i + 3] = 255;
                    continue;
                }
                var sx = w / 2.0 + (lx - cw / 2) / scale;
                var sy = h / 2.0 + (ly - ch / 2) / scale;
                if (style == 5 && col == 1) sx = w - 1 - sx;
                if (style == 6 && row == 1) sy = h - 1 - sy;
                Sample(px, w, h, sx, sy, dst, i, false);
            }
        });
        return dst;
    }

    // ------------------------------------------------------------------ visores

    private static byte[] Viewfinder(byte[] px, int w, int h, int style, double a, double t, long frame)
    {
        var m = (double)Math.Min(w, h);
        var thick = Math.Max(1.5, m * 0.007);
        switch (style)
        {
            case 0:
            {
                Brackets(px, w, h, m * 0.06, m * 0.1, thick, White, a);
                if (t % 1 < 0.6) Stamp(px, w, h, m * 0.09, m * 0.1, m * 0.02, SoftEdgeDisc, new Rgb(235, 30, 30), a);
                Text(px, w, h, "REC", m * 0.125, m * 0.085, m * 0.0045, White, a);
                Battery(px, w, h, w - m * 0.2, m * 0.08, m * 0.09, m * 0.04, thick * 0.7, a);
                Digits(px, w, h, Clock(t, false), m * 0.09, h - m * 0.14, m * 0.045, White, a);
                Line(px, w, h, w / 2.0 - m * 0.03, h / 2.0, w / 2.0 + m * 0.03, h / 2.0, thick * 0.6, White, 0.8 * a);
                Line(px, w, h, w / 2.0, h / 2.0 - m * 0.03, w / 2.0, h / 2.0 + m * 0.03, thick * 0.6, White, 0.8 * a);
                return px;
            }
            case 1:
            {
                var bw = m * 0.07;
                var body = new Rgb(22, 22, 26);
                var outPx = Inset(px, w, h, bw * 1.6, bw, w - bw * 1.6, h - bw, body, bw * 0.8);
                Stamp(outPx, w, h, bw * 0.8, h / 2.0, bw * 0.18, SoftEdgeDisc, new Rgb(60, 60, 70), 1);
                RoundRect(outPx, w, h, w - bw * 1.0, h / 2.0 - bw * 1.2, w - bw * 0.75, h / 2.0 + bw * 1.2, bw * 0.12, new Rgb(55, 55, 62), 1);
                return outPx;
            }
            case 2:
            {
                var green = new Rgb(120, 255, 140);
                double cx = w / 2.0, cy = h / 2.0;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / m;
                        if (d > 0.42) Blend(px, (y * w + x) * 4, Black, Math.Min(0.55, (d - 0.42) * 6) * a);
                    }
                });
                Stamp(px, w, h, cx, cy, m * 0.2, (u, v) => Math.Abs(Math.Sqrt(u * u + v * v) - 0.95) < 0.025 ? 1 : 0, green, a);
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    Line(px, w, h, cx + dx * m * 0.04, cy + dy * m * 0.04, cx + dx * m * 0.3, cy + dy * m * 0.3, thick * 0.6, green, a);
                Stamp(px, w, h, cx, cy, m * 0.008, SoftEdgeDisc, new Rgb(255, 50, 50), a);
                return px;
            }
            case 3:
            {
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var i = (y * w + x) * 4;
                        var l = 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
                        Blend(px, i, new Rgb(l * 0.25, Math.Min(255, l * 1.25 + 25), l * 0.3), a);
                    }
                });
                EffectProcessor.NoiseFx(px, w, h, 0.3 * a, frame);
                Scanlines(px, w, h, 0.15 * a, Math.Max(2, h / 220));
                double cx = w / 2.0, cy = h / 2.0;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (m * 0.62);
                        if (d > 0.75) Blend(px, (y * w + x) * 4, Black, Math.Min(1, (d - 0.75) * 3) * a);
                    }
                });
                return px;
            }
            case 4:
            {
                var r = 0.42 * h;
                double c1 = w / 2.0 - 0.3 * h, c2 = w / 2.0 + 0.3 * h, cy = h / 2.0;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = Math.Min(Math.Sqrt((x - c1) * (x - c1) + (y - cy) * (y - cy)), Math.Sqrt((x - c2) * (x - c2) + (y - cy) * (y - cy)));
                        var k = Math.Clamp((d - r) / (m * 0.02) + 0.5, 0, 1);
                        if (k > 0) Blend(px, (y * w + x) * 4, Black, k * a);
                    }
                });
                return px;
            }
            case 5:
            {
                Desaturate(px, 0.85 * a);
                EffectProcessor.NoiseFx(px, w, h, 0.2 * a, frame);
                Scanlines(px, w, h, 0.2 * a, Math.Max(2, h / 180));
                Text(px, w, h, "CAM", m * 0.06, m * 0.06, m * 0.005, White, a);
                Digits(px, w, h, "01", m * 0.06 + m * 0.005 * 22, m * 0.06, m * 0.035, White, a);
                Digits(px, w, h, Clock(t, true), w - m * 0.06 - DigitsWidth(Clock(t, true), m * 0.04), h - m * 0.1, m * 0.04, White, a);
                if (t % 1 < 0.6) Stamp(px, w, h, w - m * 0.08, m * 0.08, m * 0.016, SoftEdgeDisc, new Rgb(235, 30, 30), a);
                return px;
            }
            default:
            {
                var lockT = Math.Min(1, t / 0.6);
                var ease = 1 - Math.Pow(1 - lockT, 3);
                double hw = (0.45 - 0.2 * ease) * m, hh = hw * 0.75, cx = w / 2.0, cy = h / 2.0;
                var c = lockT < 1 ? new Rgb(255, 220, 60) : (t * 3 % 1 < 0.5 || t > 1.5 ? new Rgb(90, 255, 120) : White);
                var len = hw * 0.3;
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    double x0 = cx + sx * hw, y0 = cy + sy * hh;
                    Line(px, w, h, x0, y0, x0 - sx * len, y0, thick, c, a);
                    Line(px, w, h, x0, y0, x0, y0 - sy * len, thick, c, a);
                }
                Brackets(px, w, h, m * 0.05, m * 0.06, thick * 0.7, White, 0.6 * a);
                return px;
            }
        }
    }

    // ------------------------------------------------------------------ destaque

    private static byte[] Spotlight(byte[] px, int w, int h, int style, double a, double fx, double fy, double size, Rgb color)
    {
        double cx = fx * w, cy = fy * h, rad = size * Math.Max(w, h) * 0.5, feather = Math.Max(2, rad * 0.35);
        double Outside(int x, int y) => SmoothStep(rad, rad + feather, Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));
        switch (style)
        {
            case 0:
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var k = Outside(x, y);
                        var i = (y * w + x) * 4;
                        if (k > 0) Blend(px, i, Black, k * 0.85 * a);
                        if (k < 1) Screen(px, i, new Rgb(255, 240, 210), (1 - k) * 0.15 * a);
                    }
                });
                return px;
            case 1:
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var k = Outside(x, y);
                        if (k > 0) Blend(px, (y * w + x) * 4, Black, k * 0.4 * a);
                    }
                });
                Stamp(px, w, h, cx, cy, rad * 1.05, (u, v) => Math.Abs(Math.Sqrt(u * u + v * v) - 0.97) < 0.03 ? 1 : 0, color, 1);
                return px;
            case 2:
            {
                var blurred = (byte[])px.Clone();
                EffectProcessor.BlurFx(blurred, w, h, Math.Max(1, (0.005 + 0.025 * a) * w));
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var k = Outside(x, y);
                        var i = (y * w + x) * 4;
                        for (var c = 0; c < 3; c++) px[i + c] = (byte)(px[i + c] + (blurred[i + c] - px[i + c]) * k);
                    }
                });
                return px;
            }
            case 3:
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var k = Outside(x, y) * a;
                        if (k <= 0) continue;
                        var i = (y * w + x) * 4;
                        var l = 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
                        Blend(px, i, new Rgb(l, l, l), k);
                    }
                });
                return px;
            default:
            {
                double hx = rad * 1.2, hy = rad * 0.8;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = SdBox(x - cx, y - cy, hx, hy);
                        var i = (y * w + x) * 4;
                        if (d > 0) Blend(px, i, Black, Math.Min(1, d / feather) * 0.6 * a);
                        if (Math.Abs(d) < Math.Max(1.5, Math.Min(w, h) * 0.004)) Blend(px, i, color, 1);
                    }
                });
                return px;
            }
        }
    }

    // ------------------------------------------------------------------ temporizador

    private static byte[] Timer(byte[] px, int w, int h, int style, int position, double size, Rgb color, double t, double dur)
    {
        var m = (double)Math.Min(w, h);
        var remaining = Math.Max(0, dur - t);
        (double X, double Y) At(double bw, double bh) => position switch
        {
            1 => (w - bw - m * 0.05, h - bh - m * 0.05),
            2 => ((w - bw) / 2, h - bh - m * 0.06),
            _ => ((w - bw) / 2, (h - bh) / 2),
        };
        switch (style)
        {
            case 0:
            {
                var text = ((int)Math.Ceiling(remaining - 1e-9)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var dh = m * 0.34 * size;
                var (x, y) = At(dh * 1.3, dh * 1.3);
                double cx = x + dh * 0.65, cy = y + dh * 0.65;
                Stamp(px, w, h, cx, cy, dh * 0.8, SoftEdgeDisc, Black, 0.45);
                Arc(px, w, h, cx, cy, dh * 0.74, dh * 0.07, remaining % 1 == 0 && remaining > 0 ? 1 : remaining % 1, color, 1);
                Digits(px, w, h, text, cx - DigitsWidth(text, dh * 0.7) / 2, cy - dh * 0.35, dh * 0.7, color, 1);
                return px;
            }
            case 1:
            {
                var text = Clock(t, false, tenths: true);
                var dh = m * 0.13 * size;
                var tw = DigitsWidth(text, dh);
                var (x, y) = At(tw + dh * 0.6, dh * 1.6);
                RoundRect(px, w, h, x, y, x + tw + dh * 0.6, y + dh * 1.6, dh * 0.3, Black, 0.5);
                Digits(px, w, h, text, x + dh * 0.3, y + dh * 0.3, dh, color, 1);
                return px;
            }
            case 2:
            {
                var bh = Math.Max(3, m * 0.025 * size);
                var y = position == 0 ? h - bh * 3 : h - bh * 2;
                FillRect(px, w, h, 0, y, w, y + bh, Black, 0.45);
                FillRect(px, w, h, 0, y, w * Math.Clamp(t / dur, 0, 1), y + bh, color, 1);
                return px;
            }
            case 3:
            {
                var rad = m * 0.2 * size;
                var (x, y) = At(rad * 2, rad * 2);
                double cx = x + rad, cy = y + rad;
                Stamp(px, w, h, cx, cy, rad * 1.1, SoftEdgeDisc, Black, 0.4);
                Arc(px, w, h, cx, cy, rad * 0.85, rad * 0.14, 1, new Rgb(120, 120, 120), 0.6);
                Arc(px, w, h, cx, cy, rad * 0.85, rad * 0.14, Math.Clamp(t / dur, 0, 1), color, 1);
                var text = ((int)Math.Ceiling(remaining - 1e-9)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Digits(px, w, h, text, cx - DigitsWidth(text, rad * 0.6) / 2, cy - rad * 0.3, rad * 0.6, color, 1);
                return px;
            }
            default:
            {
                if (t >= 3) return px;
                var n = 3 - (int)Math.Floor(t);
                var local = t % 1;
                var pop = 1.35 - 0.35 * (1 - Math.Pow(1 - Math.Min(1, local / 0.3), 3));
                var alpha = 1 - Math.Max(0, (local - 0.75) / 0.25);
                var dh = m * 0.42 * size * pop;
                var text = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var (x, y) = At(DigitsWidth(text, dh), dh);
                Digits(px, w, h, text, x, y, dh, color, alpha);
                return px;
            }
        }
    }

    // ------------------------------------------------------------------ aberturas e encerramentos

    /// <summary>Revela a imagem conforme o progresso <paramref name="p"/> (0 = escondida, 1 = inteira).</summary>
    private static byte[] Reveal(byte[] px, int w, int h, int style, double p)
    {
        if (p >= 1) return px;
        p = Math.Clamp(p, 0, 1);
        var e = p * p * (3 - 2 * p);
        switch (style)
        {
            case 0:
                Multiply(px, e, e, e);
                return px;
            case 1:
            {
                var open = e * w / 2;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = Math.Abs(x + 0.5 - w / 2.0);
                        if (d < open) continue;
                        var fold = 0.75 + 0.25 * Math.Sin((d - open) / Math.Max(1, w * 0.012));
                        Blend(px, (y * w + x) * 4, new Rgb(130 * fold, 12 * fold, 22 * fold), 1);
                    }
                });
                return px;
            }
            case 2:
            {
                var rad = e * Math.Sqrt(w * w + h * h) / 2;
                Rows(h, y =>
                {
                    for (var x = 0; x < w; x++)
                    {
                        var d = Math.Sqrt((x - w / 2.0) * (x - w / 2.0) + (y - h / 2.0) * (y - h / 2.0));
                        var k = Math.Clamp(d - rad + 0.5, 0, 1);
                        if (k > 0) Blend(px, (y * w + x) * 4, Black, k);
                    }
                });
                return px;
            }
            case 3:
            {
                var bar = (1 - e) * h / 2;
                FrameFill(px, w, h, (x, y) => y < bar || y >= h - bar, Black);
                return px;
            }
            case 4:
                EffectProcessor.BlurFx(px, w, h, (1 - e) * 0.04 * w);
                Multiply(px, 0.3 + 0.7 * e, 0.3 + 0.7 * e, 0.3 + 0.7 * e);
                return px;
            case 5:
            {
                var outPx = Affine(px, w, h, 0, 0, 1 + (1 - e) * 0.6, 0);
                var k = 0.2 + 0.8 * e;
                Multiply(outPx, k, k, k);
                return outPx;
            }
            case 6:
                BlendAll(px, White, 1 - e);
                return px;
            case 7:
                return Affine(px, w, h, -(1 - e) * w, 0, 1, 0, black: true);
            default:
            {
                var stripe = Math.Max(2, h / 10.0);
                FrameFill(px, w, h, (x, y) => y % stripe >= e * stripe, Black);
                return px;
            }
        }
    }

    // ------------------------------------------------------------------ primitivas

    private static double Hash(int a, int b = 0)
    {
        unchecked
        {
            var x = (uint)(a * 374761393 + b * 668265263 + 1442695041);
            x = (x ^ (x >> 13)) * 1274126177u;
            x ^= x >> 16;
            return x / (double)uint.MaxValue;
        }
    }

    /// <summary>Ruído suave em −1…1.</summary>
    private static double Smooth(double t, int seed)
    {
        var i = (int)Math.Floor(t);
        var f = t - i;
        f = f * f * (3 - 2 * f);
        var h0 = Hash(i, seed);
        return (h0 + (Hash(i + 1, seed) - h0) * f) * 2 - 1;
    }

    private static double Value2(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = Hash(ix + iy * 7919, seed), b = Hash(ix + 1 + iy * 7919, seed), c = Hash(ix + (iy + 1) * 7919, seed), d = Hash(ix + 1 + (iy + 1) * 7919, seed);
        var top = a + (b - a) * fx;
        return top + (c + (d - c) * fx - top) * fy;
    }

    /// <summary>Ruído fractal em 0…1 (texturas de papel, sujeira, bordas queimadas).</summary>
    private static double Fbm(double x, double y, int seed)
    {
        double sum = 0, amp = 0.5, norm = 0;
        for (var o = 0; o < 4; o++)
        {
            sum += Value2(x, y, seed + o * 31) * amp;
            norm += amp;
            x *= 2.03;
            y *= 2.03;
            amp *= 0.5;
        }
        return sum / norm;
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static byte ToByte(double v) => (byte)(v <= 0 ? 0 : v >= 255 ? 255 : v + 0.5);

    private static Rgb HueRgb(double degrees)
    {
        var h = ((degrees % 360) + 360) % 360 / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch { 0 => (1d, x, 0d), 1 => (x, 1d, 0d), 2 => (0d, 1d, x), 3 => (0d, x, 1d), 4 => (x, 0d, 1d), _ => (1d, 0d, x) };
        return new Rgb(r * 255, g * 255, b * 255);
    }

    private static void Rows(int h, Action<int> row) => Parallel.For(0, h, row);

    private static byte[] Warp(byte[] src, int w, int h, Map map, bool black = false)
    {
        var dst = new byte[src.Length];
        Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var (sx, sy) = map(x, y);
                Sample(src, w, h, sx, sy, dst, (y * w + x) * 4, black);
            }
        });
        return dst;
    }

    private static void Sample(byte[] s, int w, int h, double x, double y, byte[] d, int di, bool black)
    {
        if (black && (x < -0.5 || y < -0.5 || x > w - 0.5 || y > h - 0.5))
        {
            d[di] = d[di + 1] = d[di + 2] = 0;
            d[di + 3] = 255;
            return;
        }
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double fx = x - x0, fy = y - y0;
        int i00 = (y0 * w + x0) * 4, i10 = (y0 * w + x1) * 4, i01 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;
        for (var c = 0; c < 4; c++)
        {
            var top = s[i00 + c] + (s[i10 + c] - s[i00 + c]) * fx;
            var bottom = s[i01 + c] + (s[i11 + c] - s[i01 + c]) * fx;
            d[di + c] = (byte)(top + (bottom - top) * fy + 0.5);
        }
    }

    /// <summary>Desloca (px), amplia em torno do centro e gira (rad).</summary>
    private static byte[] Affine(byte[] src, int w, int h, double dx, double dy, double scale, double rot, bool black = false)
    {
        double ox = w / 2.0, oy = h / 2.0, cos = Math.Cos(-rot) / scale, sin = Math.Sin(-rot) / scale;
        return Warp(src, w, h, (x, y) =>
        {
            double u = x - ox - dx, v = y - oy - dy;
            return (ox + u * cos - v * sin, oy + u * sin + v * cos);
        }, black);
    }

    /// <summary>Vermelho deslocado para um lado e azul para o outro.</summary>
    private static byte[] ChannelShift(byte[] src, int w, int h, int sx, int sy)
    {
        if (sx == 0 && sy == 0) return src;
        var dst = (byte[])src.Clone();
        Rows(h, y =>
        {
            int ry = Math.Clamp(y - sy, 0, h - 1), by = Math.Clamp(y + sy, 0, h - 1);
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                dst[i + 2] = src[(ry * w + Math.Clamp(x - sx, 0, w - 1)) * 4 + 2];
                dst[i] = src[(by * w + Math.Clamp(x + sx, 0, w - 1)) * 4];
            }
        });
        return dst;
    }

    /// <summary>Copia as linhas [y0, y1) de <paramref name="src"/> para <paramref name="dst"/> deslocadas na horizontal (com volta).</summary>
    private static void ShiftRows(byte[] src, byte[] dst, int w, int h, int y0, int y1, int offset)
    {
        offset = ((offset % w) + w) % w;
        for (var y = Math.Max(0, y0); y < Math.Min(h, y1); y++)
        {
            var row = y * w * 4;
            var first = (w - offset) * 4;
            Buffer.BlockCopy(src, row, dst, row + offset * 4, first);
            Buffer.BlockCopy(src, row + first, dst, row, offset * 4);
        }
    }

    private static void Multiply(byte[] px, double r, double g, double b) =>
        Rows(px.Length / 4 / 256 + 1, blk =>
        {
            var end = Math.Min(px.Length, (blk + 1) * 1024);
            for (var i = blk * 1024; i < end; i += 4)
            {
                px[i] = ToByte(px[i] * b);
                px[i + 1] = ToByte(px[i + 1] * g);
                px[i + 2] = ToByte(px[i + 2] * r);
            }
        });

    private static void BlendAll(byte[] px, Rgb c, double a)
    {
        if (a <= 0) return;
        for (var i = 0; i < px.Length; i += 4) Blend(px, i, c, a);
    }

    private static void Desaturate(byte[] px, double k)
    {
        if (k <= 0) return;
        for (var i = 0; i < px.Length; i += 4)
        {
            var l = 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
            Blend(px, i, new Rgb(l, l, l), k);
        }
    }

    private static void Sepia(byte[] px, double k)
    {
        for (var i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            Blend(px, i, new Rgb(r * 0.393 + g * 0.769 + b * 0.189, r * 0.349 + g * 0.686 + b * 0.168, r * 0.272 + g * 0.534 + b * 0.131), k);
        }
    }

    private static void Scanlines(byte[] px, int w, int h, double strength, int period)
    {
        if (strength <= 0) return;
        var k = 1 - strength;
        for (var y = 0; y < h; y++)
        {
            if (y / Math.Max(1, period / 2) % 2 == 0) continue;
            for (var i = y * w * 4; i < (y + 1) * w * 4; i += 4)
            {
                px[i] = (byte)(px[i] * k);
                px[i + 1] = (byte)(px[i + 1] * k);
                px[i + 2] = (byte)(px[i + 2] * k);
            }
        }
    }

    private static void Blend(byte[] px, int i, Rgb c, double a)
    {
        if (a <= 0) return;
        if (a > 1) a = 1;
        px[i] = ToByte(px[i] + (c.B - px[i]) * a);
        px[i + 1] = ToByte(px[i + 1] + (c.G - px[i + 1]) * a);
        px[i + 2] = ToByte(px[i + 2] + (c.R - px[i + 2]) * a);
    }

    private static void Screen(byte[] px, int i, Rgb c, double a)
    {
        if (a <= 0) return;
        px[i] = ToByte(255 - (255 - px[i]) * (255 - Math.Min(255, c.B * a)) / 255);
        px[i + 1] = ToByte(255 - (255 - px[i + 1]) * (255 - Math.Min(255, c.G * a)) / 255);
        px[i + 2] = ToByte(255 - (255 - px[i + 2]) * (255 - Math.Min(255, c.R * a)) / 255);
    }

    /// <summary>Soma de luz em cada pixel (modo tela); a função recebe a posição em frações da imagem.</summary>
    private static void LightField(byte[] px, int w, int h, Func<double, double, Rgb> light) =>
        Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var c = light((x + 0.5) / w, (y + 0.5) / h);
                if (c.R + c.G + c.B < 0.5) continue;
                Screen(px, (y * w + x) * 4, c, 1);
            }
        });

    private static void FrameFill(byte[] px, int w, int h, Func<int, int, bool> inside, Rgb c) =>
        Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
                if (inside(x, y)) Blend(px, (y * w + x) * 4, c, 1);
        });

    private static void FillRect(byte[] px, int w, int h, double x0, double y0, double x1, double y1, Rgb c, double a, bool screen = false)
    {
        int ax = Math.Max(0, (int)x0), bx = Math.Min(w, (int)Math.Ceiling(x1)), ay = Math.Max(0, (int)y0), by = Math.Min(h, (int)Math.Ceiling(y1));
        for (var y = ay; y < by; y++)
        {
            for (var x = ax; x < bx; x++)
            {
                var i = (y * w + x) * 4;
                if (screen) Screen(px, i, c, a);
                else Blend(px, i, c, a);
            }
        }
    }

    private static void RoundRect(byte[] px, int w, int h, double x0, double y0, double x1, double y1, double radius, Rgb c, double a)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = (x1 - x0) / 2, hy = (y1 - y0) / 2;
        int ax = Math.Max(0, (int)x0), bx = Math.Min(w, (int)Math.Ceiling(x1)), ay = Math.Max(0, (int)y0), by = Math.Min(h, (int)Math.Ceiling(y1));
        for (var y = ay; y < by; y++)
        {
            for (var x = ax; x < bx; x++)
            {
                var d = SdRoundBox(x + 0.5 - cx, y + 0.5 - cy, hx, hy, Math.Min(radius, Math.Min(hx, hy)));
                if (d < 0.5) Blend(px, (y * w + x) * 4, c, a * Math.Clamp(0.5 - d, 0, 1));
            }
        }
    }

    /// <summary>Encolhe a imagem para dentro do retângulo dado (cobrindo-o) e pinta o resto com a cor (moldura).</summary>
    private static byte[] Inset(byte[] px, int w, int h, double l, double top, double r, double bottom, Rgb fill, double radius = 0)
    {
        double iw = r - l, ih = bottom - top;
        if (iw < 2 || ih < 2) return px;
        var scale = Math.Max(iw / w, ih / h);
        double cx = (l + r) / 2, cy = (top + bottom) / 2;
        var dst = new byte[px.Length];
        Rows(h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var d = radius > 0 ? SdRoundBox(x + 0.5 - cx, y + 0.5 - cy, iw / 2, ih / 2, radius) : SdBox(x + 0.5 - cx, y + 0.5 - cy, iw / 2, ih / 2);
                if (d < 0.5) Sample(px, w, h, w / 2.0 + (x - cx) / scale, h / 2.0 + (y - cy) / scale, dst, i, false);
                if (d > -0.5)
                {
                    if (d >= 0.5) dst[i + 3] = 255;
                    Blend(dst, i, fill, Math.Clamp(d + 0.5, 0, 1));
                }
            }
        });
        return dst;
    }

    private static double SdBox(double x, double y, double hx, double hy)
    {
        double qx = Math.Abs(x) - hx, qy = Math.Abs(y) - hy;
        return Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0);
    }

    private static double SdRoundBox(double x, double y, double hx, double hy, double r) => SdBox(x, y, hx - r, hy - r) - r;

    private static void Stamp(byte[] px, int w, int h, double cx, double cy, double radius, Shape shape, Rgb c, double alpha, bool screen = false)
    {
        if (radius < 0.5 || alpha <= 0) return;
        int x0 = Math.Max(0, (int)(cx - radius)), x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + radius));
        int y0 = Math.Max(0, (int)(cy - radius)), y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + radius));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var cov = shape((x + 0.5 - cx) / radius, (y + 0.5 - cy) / radius);
                if (cov <= 0) continue;
                var i = (y * w + x) * 4;
                if (screen) Screen(px, i, c, cov * alpha);
                else Blend(px, i, c, cov * alpha);
            }
        }
    }

    private static double SoftDisc(double u, double v) => Math.Sqrt(u * u + v * v) is var r && r < 1 ? Math.Min(1, (1 - r) * 2.5) : 0;

    private static double SoftEdgeDisc(double u, double v) => Math.Sqrt(u * u + v * v) is var r && r < 1 ? Math.Min(1, (1 - r) * 12) : 0;

    private static double BokehDisc(double u, double v)
    {
        var r = Math.Sqrt(u * u + v * v);
        return r >= 1 ? 0 : Math.Min(1, (1 - r) * 8) * (0.55 + 0.45 * r * r);
    }

    private static double Ring(double u, double v) => Math.Max(0, 1 - Math.Abs(Math.Sqrt(u * u + v * v) - 0.85) / 0.1);

    private static double Box(double u, double v, double hx, double hy) => Math.Abs(u) <= hx && Math.Abs(v) <= hy ? 1 : 0;

    private static double Heart(double u, double v)
    {
        double x = u * 1.2, y = -v * 1.2 + 0.25;
        var a = x * x + y * y - 1;
        return a * a * a - x * x * y * y * y <= 0 ? 1 : 0;
    }

    private static double Star(double u, double v)
    {
        var r = Math.Sqrt(u * u + v * v);
        var seg = Math.PI * 2 / 5;
        var ang = Math.Atan2(v, u) + Math.PI / 2;
        var k = ((ang % seg) + seg) % seg / seg;
        var edge = 0.42 + (1 - 0.42) * Math.Abs(k - 0.5) * 2;
        return r <= edge ? 1 : 0;
    }

    private static double Sparkle(double u, double v)
    {
        var d = Math.Sqrt(Math.Abs(u)) + Math.Sqrt(Math.Abs(v));
        return d < 1 ? Math.Min(1, (1 - d) * 3) : 0;
    }

    private static void Line(byte[] px, int w, int h, double x0, double y0, double x1, double y1, double width, Rgb c, double a, bool screen = false)
    {
        if (a <= 0) return;
        var half = width / 2;
        int ax = Math.Max(0, (int)(Math.Min(x0, x1) - half - 1)), bx = Math.Min(w - 1, (int)(Math.Max(x0, x1) + half + 1));
        int ay = Math.Max(0, (int)(Math.Min(y0, y1) - half - 1)), by = Math.Min(h - 1, (int)(Math.Max(y0, y1) + half + 1));
        double dx = x1 - x0, dy = y1 - y0, len2 = Math.Max(1e-9, dx * dx + dy * dy);
        for (var y = ay; y <= by; y++)
        {
            for (var x = ax; x <= bx; x++)
            {
                double px0 = x + 0.5 - x0, py0 = y + 0.5 - y0;
                var t = Math.Clamp((px0 * dx + py0 * dy) / len2, 0, 1);
                double ex = px0 - t * dx, ey = py0 - t * dy;
                var cov = Math.Clamp(half - Math.Sqrt(ex * ex + ey * ey) + 0.5, 0, 1);
                if (cov <= 0) continue;
                var i = (y * w + x) * 4;
                if (screen) Screen(px, i, c, cov * a);
                else Blend(px, i, c, cov * a);
            }
        }
    }

    /// <summary>Arco a partir do topo, no sentido horário, cobrindo a fração <paramref name="fraction"/> do círculo.</summary>
    private static void Arc(byte[] px, int w, int h, double cx, double cy, double radius, double thickness, double fraction, Rgb c, double a)
    {
        if (fraction <= 0) return;
        var outer = radius + thickness / 2;
        int x0 = Math.Max(0, (int)(cx - outer - 1)), x1 = Math.Min(w - 1, (int)(cx + outer + 1));
        int y0 = Math.Max(0, (int)(cy - outer - 1)), y1 = Math.Min(h - 1, (int)(cy + outer + 1));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                var cov = Math.Clamp(thickness / 2 - Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius) + 0.5, 0, 1);
                if (cov <= 0) continue;
                var ang = Math.Atan2(dx, -dy);
                if (ang < 0) ang += Math.PI * 2;
                if (ang / (Math.PI * 2) > fraction) continue;
                Blend(px, (y * w + x) * 4, c, cov * a);
            }
        }
    }

    private static void Brackets(byte[] px, int w, int h, double inset, double len, double thick, Rgb c, double a)
    {
        foreach (var (sx, sy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            double x = sx == 0 ? inset : w - inset, y = sy == 0 ? inset : h - inset;
            double dx = sx == 0 ? len : -len, dy = sy == 0 ? len : -len;
            Line(px, w, h, x, y, x + dx, y, thick, c, a);
            Line(px, w, h, x, y, x, y + dy, thick, c, a);
        }
    }

    private static void Battery(byte[] px, int w, int h, double x, double y, double bw, double bh, double thick, double a)
    {
        Line(px, w, h, x, y, x + bw, y, thick, White, a);
        Line(px, w, h, x, y + bh, x + bw, y + bh, thick, White, a);
        Line(px, w, h, x, y, x, y + bh, thick, White, a);
        Line(px, w, h, x + bw, y, x + bw, y + bh, thick, White, a);
        FillRect(px, w, h, x + bw, y + bh * 0.3, x + bw + thick * 2, y + bh * 0.7, White, a);
        for (var i = 0; i < 3; i++)
        {
            var cx = x + thick * 2 + i * (bw - thick * 2) / 3;
            FillRect(px, w, h, cx, y + thick * 2, cx + (bw - thick * 2) / 3 - thick * 1.5, y + bh - thick * 2, White, a);
        }
    }

    private static string Clock(double t, bool hours, bool tenths = false)
    {
        var total = (int)Math.Floor(t);
        var s = total % 60;
        var mnt = total / 60 % 60;
        var text = hours
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{total / 3600:00}:{mnt:00}:{s:00}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mnt:00}:{s:00}");
        return tenths ? text + "." + ((int)(t * 10) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : text;
    }

    // Segmentos: a (topo), b (alto à direita), c (baixo à direita), d (base), e (baixo à esquerda), f (alto à esquerda), g (meio).
    private static readonly string[] SegmentsOf = ["abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg"];

    private static double CharWidth(char ch, double dh) => ch is ':' or '.' ? dh * 0.3 : dh * 0.72;

    private static double DigitsWidth(string text, double dh) => text.Sum(ch => CharWidth(ch, dh));

    /// <summary>Números em sete segmentos (relógios e contagens), sem depender de fontes instaladas.</summary>
    private static void Digits(byte[] px, int w, int h, string text, double x, double y, double dh, Rgb c, double a)
    {
        var t = Math.Max(1, dh * 0.12);
        var dw = dh * 0.55;
        foreach (var ch in text)
        {
            if (ch is ':' or '.')
            {
                var cx = x + dh * 0.12;
                if (ch == ':') RoundRect(px, w, h, cx, y + dh * 0.28, cx + t, y + dh * 0.28 + t, t * 0.3, c, a);
                RoundRect(px, w, h, cx, y + dh * 0.72, cx + t, y + dh * 0.72 + t, t * 0.3, c, a);
            }
            else if (ch is >= '0' and <= '9')
            {
                foreach (var s in SegmentsOf[ch - '0'])
                {
                    var (x0, y0, x1, y1) = s switch
                    {
                        'a' => (x + t, y, x + dw - t, y + t),
                        'd' => (x + t, y + dh - t, x + dw - t, y + dh),
                        'g' => (x + t, y + dh / 2 - t / 2, x + dw - t, y + dh / 2 + t / 2),
                        'f' => (x, y + t * 0.6, x + t, y + dh / 2 - t * 0.2),
                        'b' => (x + dw - t, y + t * 0.6, x + dw, y + dh / 2 - t * 0.2),
                        'e' => (x, y + dh / 2 + t * 0.2, x + t, y + dh - t * 0.6),
                        _ => (x + dw - t, y + dh / 2 + t * 0.2, x + dw, y + dh - t * 0.6),
                    };
                    RoundRect(px, w, h, x0, y0, x1, y1, t * 0.35, c, a);
                }
            }
            x += CharWidth(ch, dh);
        }
    }

    // Letras 5×7 usadas nos visores (REC, CAM).
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    };

    private static void Text(byte[] px, int w, int h, string text, double x, double y, double cell, Rgb c, double a)
    {
        foreach (var ch in text)
        {
            if (Font.TryGetValue(ch, out var rows))
            {
                for (var r = 0; r < rows.Length; r++)
                    for (var k = 0; k < rows[r].Length; k++)
                        if (rows[r][k] == '1') FillRect(px, w, h, x + k * cell, y + r * cell, x + (k + 1) * cell, y + (r + 1) * cell, c, a);
            }
            x += cell * 6;
        }
    }
}
