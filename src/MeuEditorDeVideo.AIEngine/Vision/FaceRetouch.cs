// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Retoque do rosto sobre a malha de 478 pontos (MediaPipe Face Mesh): beleza (<see cref="EffectCatalog.Beauty"/>:
/// pele, linhas de expressão, bolsas e deformações locais de rosto, olhos, nariz, boca e sobrancelhas) e maquiagem
/// (<see cref="EffectCatalog.Makeup"/>). As áreas vêm de polígonos dos pontos, com bordas suavizadas; todo o trabalho
/// fica dentro do retângulo do rosto.
/// </summary>
public static class FaceRetouch
{
    // Índices da malha MediaPipe ("direito" = lado direito da pessoa, à esquerda na imagem).
    internal static readonly int[] Oval = [10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109];
    internal static readonly int[] LipsOuter = [61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185];
    internal static readonly int[] LipsInner = [78, 95, 88, 178, 87, 14, 317, 402, 318, 324, 308, 415, 310, 311, 312, 13, 82, 81, 80, 191];
    internal static readonly int[] EyeRight = [33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246];
    internal static readonly int[] EyeLeft = [263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466];
    internal static readonly int[] BrowRight = [70, 63, 105, 66, 107, 55, 65, 52, 53, 46];
    internal static readonly int[] BrowLeft = [300, 293, 334, 296, 336, 285, 295, 282, 283, 276];
    private static readonly int[] UpperLidRight = [33, 246, 161, 160, 159, 158, 157, 173, 133];
    private static readonly int[] UpperLidLeft = [263, 466, 388, 387, 386, 385, 384, 398, 362];
    private static readonly int[] LowerLidRight = [33, 7, 163, 144, 145, 153, 154, 155, 133];
    private static readonly int[] LowerLidLeft = [263, 249, 390, 373, 374, 380, 381, 382, 362];

    /// <summary>Valores de cada look (cílios, sombra, delineador, sobrancelhas, batom, blush, contorno, realces) e cores.</summary>
    private static readonly (double[] Amounts, (double, double, double) Lip, (double, double, double) Shadow, (double, double, double) Blush)[] Looks =
    [
        ([0, 0, 0, 0, 0, 0, 0, 0], (0, 0, 0), (0, 0, 0), (0, 0, 0)),
        ([30, 25, 15, 20, 35, 25, 15, 20], (0.78, 0.38, 0.40), (0.62, 0.46, 0.40), (0.92, 0.55, 0.50)),
        ([70, 50, 60, 40, 70, 35, 40, 40], (0.72, 0.08, 0.15), (0.45, 0.28, 0.25), (0.88, 0.42, 0.42)),
        ([80, 70, 80, 45, 75, 25, 45, 30], (0.45, 0.05, 0.12), (0.20, 0.16, 0.22), (0.80, 0.40, 0.42)),
        ([40, 35, 20, 20, 50, 45, 10, 30], (0.90, 0.45, 0.55), (0.85, 0.55, 0.60), (0.95, 0.50, 0.60)),
    ];

    private sealed class Face(FaceLandmarks points, Rect rect)
    {
        public Rect Rect { get; } = rect;
        public Point2f P(int i) => new(points.X[i] - Rect.X, points.Y[i] - Rect.Y);
        public Point[] Poly(IEnumerable<int> idx) => [.. idx.Select(i => { var p = P(i); return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)); })];
        public double Dist(int a, int b) => Point2f.Distance(P(a), P(b));
        public double Width => Dist(234, 454);
        public double Height => Dist(10, 152);
    }

    private static Face? Prepare(BgraImage image, FaceLandmarks points)
    {
        var (cx, cy, side) = FaceLandmarkService.Roi(points);
        var x0 = Math.Max(0, (int)(cx - side / 2));
        var y0 = Math.Max(0, (int)(cy - side / 2));
        var x1 = Math.Min(image.Width, (int)Math.Ceiling(cx + side / 2));
        var y1 = Math.Min(image.Height, (int)Math.Ceiling(cy + side / 2));
        if (x1 - x0 < 16 || y1 - y0 < 16) return null;
        var face = new Face(points, new Rect(x0, y0, x1 - x0, y1 - y0));
        return face.Width < 12 ? null : face;
    }

    // ---------------------------------------------------------------- beleza

    public static BgraImage Beauty(BgraImage image, FaceLandmarks points, EffectInstance e)
    {
        if (Prepare(image, points) is not { } f) return image;
        var smooth = (float)(EffectCatalog.Value(e, "smooth") / 100);
        var white = (float)(EffectCatalog.Value(e, "white") / 100);
        var lines = (float)(EffectCatalog.Value(e, "lines") / 100);
        var bags = (float)(EffectCatalog.Value(e, "bags") / 100);

        var px = (byte[])image.Pixels.Clone();
        var r = f.Rect;
        var fw = f.Width;
        using var full = CvImage.ToMat(image);
        using var roi = new Mat(full, r);
        using var bgr = new Mat();
        Cv2.CvtColor(roi, bgr, ColorConversionCodes.BGRA2BGR);

        if (smooth > 0 || lines > 0 || white > 0)
        {
            // Pele: oval do rosto sem olhos, sobrancelhas e boca.
            var skin = Mask(r, m =>
            {
                Cv2.FillPoly(m, [f.Poly(Oval)], Scalar.All(255), LineTypes.AntiAlias);
                Cv2.FillPoly(m, [f.Poly(EyeRight), f.Poly(EyeLeft), f.Poly(BrowRight), f.Poly(BrowLeft), f.Poly(LipsOuter)], Scalar.All(0), LineTypes.AntiAlias);
            }, fw * 0.025, erodeHoles: fw * 0.015);
            using var bilateral = new Mat();
            Cv2.BilateralFilter(bgr, bilateral, Math.Clamp((int)(fw * 0.03) | 1, 5, 25), 25 + 45 * smooth, Math.Max(3, fw * 0.02));
            using var soft = new Mat();
            Cv2.GaussianBlur(bilateral, soft, new Size(0, 0), Math.Max(1, fw * 0.012));
            var b = Bytes3(bilateral);
            var s2 = Bytes3(soft);
            Blend(px, image.Width, r, skin, (v, k, ch, o) =>
            {
                var x = v + (b[k] - v) * smooth * 0.9f;
                x += (s2[k] - x) * lines * 0.5f;
                return x + (255 - x) * white * 0.22f;
            });
        }

        if (bags > 0)
        {
            var eyeH = Math.Max(f.Dist(159, 145), f.Dist(33, 133) * 0.25);
            var under = Mask(r, m =>
            {
                foreach (var lid in new[] { LowerLidRight, LowerLidLeft })
                {
                    var top = f.Poly(lid);
                    var bottom = top.Reverse().Select(p => new Point(p.X, p.Y + (int)(eyeH * 1.3))).ToArray();
                    Cv2.FillPoly(m, [[.. top.Select(p => new Point(p.X, p.Y + (int)(eyeH * 0.25))), .. bottom]], Scalar.All(255), LineTypes.AntiAlias);
                }
            }, eyeH * 0.45);
            using var soft = new Mat();
            Cv2.GaussianBlur(bgr, soft, new Size(0, 0), Math.Max(1, eyeH * 0.5));
            var s = Bytes3(soft);
            Blend(px, image.Width, r, under, (v, k, ch, o) =>
            {
                var x = v + (s[k] - v) * bags * 0.8f;
                return x + (255 - x) * bags * 0.12f;
            });
        }

        var result = new BgraImage(image.Width, image.Height, px);
        return Warp(result, f, e);
    }

    /// <summary>Deformações locais (afinar rosto e nariz, aumentar olhos, boca, erguer sobrancelhas) num único remapeamento.</summary>
    private static BgraImage Warp(BgraImage image, Face f, EffectInstance e)
    {
        var face = EffectCatalog.Value(e, "face") / 100;
        var eyes = EffectCatalog.Value(e, "eyes") / 100;
        var nose = EffectCatalog.Value(e, "nose") / 100;
        var mouth = EffectCatalog.Value(e, "mouth") / 100;
        var brows = EffectCatalog.Value(e, "brows") / 100;
        if (Math.Abs(face) + Math.Abs(eyes) + Math.Abs(nose) + Math.Abs(mouth) + Math.Abs(brows) < 0.005) return image;

        var r = f.Rect;
        var dx = new float[r.Width * r.Height];
        var dy = new float[r.Width * r.Height];
        var fw = f.Width;

        if (face != 0)
        {
            var c = f.P(4);
            foreach (var i in new[] { 172, 136, 150, 397, 365, 379, 93, 323, 132, 361 })
            {
                var p = f.P(i);
                Move(dx, dy, r, p, fw * 0.22, (c.X - p.X) * 0.13 * face, (c.Y - p.Y) * 0.04 * face);
            }
        }
        if (eyes != 0)
        {
            Scale(dx, dy, r, f.P(468), f.Dist(33, 133) * 1.1, 0.22 * eyes);
            Scale(dx, dy, r, f.P(473), f.Dist(263, 362) * 1.1, 0.22 * eyes);
        }
        if (nose != 0)
        {
            var c = f.P(4);
            var nw = f.Dist(98, 327);
            foreach (var i in new[] { 98, 327, 129, 358 })
            {
                var p = f.P(i);
                Move(dx, dy, r, p, nw * 0.6, (c.X - p.X) * 0.35 * nose, 0);
            }
        }
        if (mouth != 0)
        {
            var a = f.P(61);
            var b = f.P(291);
            Scale(dx, dy, r, new Point2f((a.X + b.X) / 2, (a.Y + b.Y) / 2), f.Dist(61, 291) * 0.85, 0.2 * mouth);
        }
        if (brows != 0)
        {
            var lift = -f.Height * 0.035 * brows;
            Move(dx, dy, r, f.P(105), f.Dist(70, 107) * 0.8, 0, lift);
            Move(dx, dy, r, f.P(334), f.Dist(300, 336) * 0.8, 0, lift);
        }

        using var mapX = new Mat(r.Height, r.Width, MatType.CV_32FC1);
        using var mapY = new Mat(r.Height, r.Width, MatType.CV_32FC1);
        var mx = new float[dx.Length];
        var my = new float[dy.Length];
        for (var y = 0; y < r.Height; y++)
            for (var x = 0; x < r.Width; x++)
            {
                var k = y * r.Width + x;
                mx[k] = x + dx[k];
                my[k] = y + dy[k];
            }
        mapX.SetArray(mx);
        mapY.SetArray(my);
        using var full = CvImage.ToMat(image);
        using var roi = new Mat(full, r);
        using var warped = new Mat();
        Cv2.Remap(roi, warped, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Replicate);
        warped.CopyTo(roi);
        return CvImage.ToImage(full);
    }

    /// <summary>Desloca o conteúdo em torno de c por (vx, vy), com queda suave até o raio.</summary>
    private static void Move(float[] dx, float[] dy, Rect r, Point2f c, double radius, double vx, double vy)
    {
        if (radius < 1) return;
        ForDisk(r, c, radius, (k, x, y, t) =>
        {
            var w = (float)(t * t);
            dx[k] -= (float)vx * w;
            dy[k] -= (float)vy * w;
        });
    }

    /// <summary>Amplia (s &gt; 0) ou reduz (s &lt; 0) o conteúdo em torno de c.</summary>
    private static void Scale(float[] dx, float[] dy, Rect r, Point2f c, double radius, double s)
    {
        if (radius < 1) return;
        ForDisk(r, c, radius, (k, x, y, t) =>
        {
            dx[k] -= (float)((x - c.X) * s * t);
            dy[k] -= (float)((y - c.Y) * s * t);
        });
    }

    private static void ForDisk(Rect r, Point2f c, double radius, Action<int, double, double, double> act)
    {
        int x0 = Math.Max(0, (int)(c.X - radius)), x1 = Math.Min(r.Width - 1, (int)(c.X + radius));
        int y0 = Math.Max(0, (int)(c.Y - radius)), y1 = Math.Min(r.Height - 1, (int)(c.Y + radius));
        var r2 = radius * radius;
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                double ddx = x - c.X, ddy = y - c.Y, d2 = ddx * ddx + ddy * ddy;
                if (d2 >= r2) continue;
                act(y * r.Width + x, x, y, 1 - d2 / r2);
            }
    }

    // ---------------------------------------------------------------- maquiagem

    public static BgraImage Makeup(BgraImage image, FaceLandmarks points, EffectInstance e)
    {
        if (Prepare(image, points) is not { } f) return image;
        var look = Looks[Math.Clamp((int)Math.Round(EffectCatalog.Value(e, "look")), 0, Looks.Length - 1)];
        // Cada item: o controle do usuário vale quando > 0; senão, o valor e a cor do look.
        (float A, (double, double, double) C) Item(string key, int lookIndex, string? colorKey, (double, double, double) lookColor)
        {
            var v = EffectCatalog.Value(e, key);
            var color = colorKey is null ? (0.0, 0.0, 0.0) : EffectCatalog.ColorValue(e, colorKey);
            return v > 0.5 ? ((float)(v / 100), color) : ((float)(look.Amounts[lookIndex] / 100), v > 0.5 || lookColor == (0, 0, 0) ? color : lookColor);
        }

        var lashes = Item("lashes", 0, null, (0, 0, 0)).A;
        var shadow = Item("shadow", 1, "shadowColor", look.Shadow);
        var liner = Item("liner", 2, null, (0, 0, 0)).A;
        var brow = Item("browfill", 3, "browColor", (0, 0, 0));
        var lip = Item("lipstick", 4, "lipColor", look.Lip);
        var blush = Item("blush", 5, "blushColor", look.Blush);
        var contour = Item("contour", 6, null, (0, 0, 0)).A;
        var highlight = Item("highlight", 7, null, (0, 0, 0)).A;
        var eyeColor = ((float)(EffectCatalog.Value(e, "eyecolor") / 100), EffectCatalog.ColorValue(e, "irisColor"));
        var freckles = (float)(EffectCatalog.Value(e, "freckles") / 100);

        var px = (byte[])image.Pixels.Clone();
        var r = f.Rect;
        var fw = f.Width;
        var w = image.Width;
        var eyeHr = Math.Max(f.Dist(159, 145), f.Dist(33, 133) * 0.25);
        var eyeHl = Math.Max(f.Dist(386, 374), f.Dist(263, 362) * 0.25);
        var eyeH = (eyeHr + eyeHl) / 2;

        if (contour > 0)
        {
            var m = Mask(r, mm =>
            {
                Stroke(mm, f, 234, 61, 0.15, 0.45, fw * 0.045);
                Stroke(mm, f, 454, 291, 0.15, 0.45, fw * 0.045);
                Cv2.Line(mm, Pt(f.P(193)), Pt(f.P(115)), Scalar.All(140), (int)Math.Max(1, fw * 0.025), LineTypes.AntiAlias);
                Cv2.Line(mm, Pt(f.P(417)), Pt(f.P(344)), Scalar.All(140), (int)Math.Max(1, fw * 0.025), LineTypes.AntiAlias);
            }, fw * 0.04);
            Blend(px, w, r, m, (v, k, ch, o) => v + (v * Channel(ch, (0.62, 0.47, 0.40)) - v) * contour * 0.6f);
        }
        if (highlight > 0)
        {
            var m = Mask(r, mm =>
            {
                Stroke(mm, f, 234, 6, 0.25, 0.55, fw * 0.03);
                Stroke(mm, f, 454, 6, 0.25, 0.55, fw * 0.03);
                Cv2.Line(mm, Pt(f.P(6)), Pt(f.P(4)), Scalar.All(200), (int)Math.Max(1, fw * 0.025), LineTypes.AntiAlias);
                Cv2.Circle(mm, Pt(f.P(151)), (int)(fw * 0.05), Scalar.All(160), -1, LineTypes.AntiAlias);
                Cv2.Circle(mm, Pt(f.P(199)), (int)(fw * 0.035), Scalar.All(160), -1, LineTypes.AntiAlias);
            }, fw * 0.035);
            Blend(px, w, r, m, (v, k, ch, o) => v + (255 - v) * highlight * 0.35f);
        }
        if (blush.A > 0)
        {
            var tint = Normalize(blush.C);
            var m = Mask(r, mm =>
            {
                foreach (var (cheek, edge) in new[] { (205, 234), (425, 454) })
                {
                    var a = f.P(cheek);
                    var b = f.P(edge);
                    Cv2.Circle(mm, Pt(new Point2f(a.X + (b.X - a.X) * 0.25f, a.Y + (b.Y - a.Y) * 0.25f)), (int)(fw * 0.1), Scalar.All(255), -1, LineTypes.AntiAlias);
                }
            }, fw * 0.06);
            Blend(px, w, r, m, (v, k, ch, o) => v + (v * Channel(ch, tint) - v) * blush.A * 0.55f);
        }
        if (freckles > 0)
        {
            var m = Mask(r, mm => Freckles(mm, f), Math.Max(0.5, fw * 0.003));
            Blend(px, w, r, m, (v, k, ch, o) => v + (v * Channel(ch, (0.62, 0.42, 0.30)) - v) * freckles * 0.6f);
        }
        if (shadow.A > 0)
        {
            var m = Mask(r, mm =>
            {
                foreach (var (lid, h) in new[] { (UpperLidRight, eyeHr), (UpperLidLeft, eyeHl) })
                {
                    var low = f.Poly(lid);
                    var high = low.Reverse().Select((p, i) => new Point(p.X, p.Y - (int)(h * (1.1 + 0.6 * Math.Sin(Math.PI * i / (low.Length - 1)))))).ToArray();
                    Cv2.FillPoly(mm, [[.. low, .. high]], Scalar.All(255), LineTypes.AntiAlias);
                }
                Cv2.FillPoly(mm, [f.Poly(EyeRight), f.Poly(EyeLeft)], Scalar.All(0), LineTypes.AntiAlias);
            }, eyeH * 0.45);
            var tint = Normalize(shadow.C);
            Blend(px, w, r, m, (v, k, ch, o) => v + (v * Channel(ch, tint) - v) * shadow.A * 0.7f);
        }
        if (brow.A > 0)
        {
            var m = Mask(r, mm => Cv2.FillPoly(mm, [f.Poly(BrowRight), f.Poly(BrowLeft)], Scalar.All(255), LineTypes.AntiAlias), eyeH * 0.15);
            Blend(px, w, r, m, (v, k, ch, o) => v + (Math.Min(v, 255 * (float)Channel(ch, brow.C) * 1.3f) - v) * brow.A * 0.75f);
        }
        if (liner > 0 || lashes > 0)
        {
            var m = Mask(r, mm =>
            {
                foreach (var (lid, outer, inner, h) in new[] { (UpperLidRight, 33, 133, eyeHr), (UpperLidLeft, 263, 362, eyeHl) })
                {
                    var line = f.Poly(lid);
                    var o = f.P(outer);
                    var n = f.P(inner);
                    var wing = new Point((int)(o.X + (o.X - n.X) * 0.18), (int)(o.Y - h * 0.35));
                    if (liner > 0)
                    {
                        Cv2.Polylines(mm, [line], false, Scalar.All(255 * liner), (int)Math.Max(1, h * 0.16 * (0.5 + liner)), LineTypes.AntiAlias);
                        Cv2.Line(mm, Pt(o), wing, Scalar.All(255 * liner), (int)Math.Max(1, h * 0.12 * (0.5 + liner)), LineTypes.AntiAlias);
                    }
                    if (lashes > 0)
                    {
                        var up = line.Select(p => new Point(p.X, p.Y - (int)(h * 0.08))).ToArray();
                        Cv2.Polylines(mm, [up], false, Scalar.All(255 * lashes), (int)Math.Max(1, h * 0.2 * (0.4 + lashes)), LineTypes.AntiAlias);
                        foreach (var p in up.Skip(1).Take(up.Length - 2))
                            Cv2.Line(mm, p, new Point(p.X + (int)((p.X - n.X) * 0.05), p.Y - (int)(h * 0.35 * lashes)), Scalar.All(200 * lashes), 1, LineTypes.AntiAlias);
                    }
                }
            }, Math.Max(0.6, eyeH * 0.04));
            Blend(px, w, r, m, (v, k, ch, o) => v * 0.12f);
        }
        var snap = px;
        if (eyeColor.Item1 > 0)
        {
            snap = (byte[])px.Clone();
            var m = Mask(r, mm =>
            {
                foreach (var (c, a, b, eye) in new[] { (468, 469, 471, EyeRight), (473, 474, 476, EyeLeft) })
                {
                    var rad = Math.Max(1, f.Dist(a, b) / 2);
                    Cv2.Circle(mm, Pt(f.P(c)), (int)Math.Round(rad), Scalar.All(255), -1, LineTypes.AntiAlias);
                    Cv2.Circle(mm, Pt(f.P(c)), (int)Math.Round(rad * 0.38), Scalar.All(0), -1, LineTypes.AntiAlias);
                }
                using var eyes = new Mat(mm.Size(), MatType.CV_8UC1, Scalar.All(0));
                Cv2.FillPoly(eyes, [f.Poly(EyeRight), f.Poly(EyeLeft)], Scalar.All(255), LineTypes.AntiAlias);
                Cv2.BitwiseAnd(mm, eyes, mm);
            }, Math.Max(0.6, eyeH * 0.06));
            Blend(px, w, r, m, (v, k, ch, o) => v + (Colorize(snap, ch, o, eyeColor.Item2) - v) * eyeColor.Item1 * 0.7f);
        }
        if (lip.A > 0)
        {
            snap = (byte[])px.Clone();
            var m = Mask(r, mm =>
            {
                Cv2.FillPoly(mm, [f.Poly(LipsOuter)], Scalar.All(255), LineTypes.AntiAlias);
                Cv2.FillPoly(mm, [f.Poly(LipsInner)], Scalar.All(0), LineTypes.AntiAlias);
            }, Math.Max(0.8, f.Dist(61, 291) * 0.02));
            Blend(px, w, r, m, (v, k, ch, o) => v + (Colorize(snap, ch, o, lip.C) - v) * lip.A * 0.85f);
        }
        return new BgraImage(image.Width, image.Height, px);
    }

    /// <summary>Sardas: pontos fixos (mesma semente) no nariz e nas maçãs do rosto, presos à malha.</summary>
    private static void Freckles(Mat m, Face f)
    {
        var origin = f.P(197);
        var left = f.P(234);
        var right = f.P(454);
        var top = f.P(10);
        var bottom = f.P(152);
        var ax = new Point2f((right.X - left.X) / 2, (right.Y - left.Y) / 2);
        var ay = new Point2f((bottom.X - top.X) / 2, (bottom.Y - top.Y) / 2);
        var rnd = new Random(4711);
        var size = Math.Max(1.2, f.Width * 0.007);
        // Desenha em coordenadas com 4 bits de fração (pontos redondos e posição sub-pixel).
        const int shift = 4;
        for (var n = 0; n < 90; n++)
        {
            // u: de uma maçã do rosto à outra, passando pelo nariz; v: abaixo dos olhos, acima da boca.
            var u = rnd.NextDouble() * 1.1 - 0.55;
            var v = 0.12 + rnd.NextDouble() * 0.2 - Math.Abs(u) * 0.08;
            var x = origin.X + ax.X * u + ay.X * v;
            var y = origin.Y + ax.Y * u + ay.Y * v;
            var rad = size * (0.6 + 0.8 * rnd.NextDouble());
            Cv2.Circle(m, new Point((int)(x * 16), (int)(y * 16)), (int)(rad * 16), Scalar.All(70 + rnd.Next(120)), -1, LineTypes.AntiAlias, shift);
        }
        // Só dentro do contorno do rosto (com o rosto de perfil, parte dos pontos cairia no fundo).
        using var oval = new Mat(m.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(oval, [f.Poly(Oval)], Scalar.All(255), LineTypes.AntiAlias);
        Cv2.BitwiseAnd(m, oval, m);
    }

    // ---------------------------------------------------------------- utilidades

    private static Point Pt(Point2f p) => new((int)Math.Round(p.X), (int)Math.Round(p.Y));

    /// <summary>Elipse alongada entre os pontos a e b (frações t0…t1 do segmento), com a espessura dada.</summary>
    private static void Stroke(Mat m, Face f, int a, int b, double t0, double t1, double thickness)
    {
        var pa = f.P(a);
        var pb = f.P(b);
        Point2f At(double t) => new((float)(pa.X + (pb.X - pa.X) * t), (float)(pa.Y + (pb.Y - pa.Y) * t));
        var c = At((t0 + t1) / 2);
        var len = Point2f.Distance(At(t0), At(t1));
        var angle = Math.Atan2(pb.Y - pa.Y, pb.X - pa.X) * 180 / Math.PI;
        Cv2.Ellipse(m, new RotatedRect(c, new Size2f(len, thickness * 2), (float)angle), Scalar.All(255), -1, LineTypes.AntiAlias);
    }

    /// <summary>Máscara do retângulo do rosto (0…255) desenhada por <paramref name="draw"/> e suavizada.</summary>
    private static byte[] Mask(Rect r, Action<Mat> draw, double feather, double erodeHoles = 0)
    {
        using var m = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
        draw(m);
        if (erodeHoles > 1)
        {
            using var k = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size((int)erodeHoles | 1, (int)erodeHoles | 1));
            Cv2.Erode(m, m, k);
        }
        if (feather > 0.3) Cv2.GaussianBlur(m, m, new Size(0, 0), feather);
        return CvImage.Bytes(m);
    }

    private static byte[] Bytes3(Mat bgr)
    {
        var bytes = new byte[bgr.Width * bgr.Height * 3];
        using var cont = bgr.Clone();
        cont.GetArray(out Vec3b[] data);
        for (var i = 0; i < data.Length; i++)
        {
            bytes[i * 3] = data[i].Item0;
            bytes[i * 3 + 1] = data[i].Item1;
            bytes[i * 3 + 2] = data[i].Item2;
        }
        return bytes;
    }

    /// <summary>
    /// Aplica <paramref name="fx"/> canal a canal onde a máscara é &gt; 0 e mistura pelo valor da máscara.
    /// fx recebe o valor (0…255), o índice no buffer BGR do retângulo e o canal (0 = B, 1 = G, 2 = R).
    /// </summary>
    private static void Blend(byte[] px, int width, Rect r, byte[] mask, Func<float, int, int, int, float> fx)
    {
        var src = (byte[])px.Clone();
        Parallel.For(0, r.Height, y =>
        {
            for (var x = 0; x < r.Width; x++)
            {
                var mk = mask[y * r.Width + x];
                if (mk == 0) continue;
                var a = mk / 255f;
                var i = ((r.Y + y) * width + r.X + x) * 4;
                var k3 = (y * r.Width + x) * 3;
                for (var c = 0; c < 3; c++)
                {
                    var v = src[i + c];
                    var t = fx(v, k3 + c, c, i);
                    px[i + c] = (byte)Math.Clamp((int)(v + (t - v) * a + 0.5f), 0, 255);
                }
            }
        });
    }

    /// <summary>Canal (0 = B, 1 = G, 2 = R) de uma cor RGB 0…1.</summary>
    private static float Channel(int channel, (double R, double G, double B) c) =>
        channel switch { 0 => (float)c.B, 1 => (float)c.G, _ => (float)c.R };

    private static (double R, double G, double B) Normalize((double R, double G, double B) c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        return max <= 1e-6 ? (1, 1, 1) : (c.R / max, c.G / max, c.B / max);
    }

    /// <summary>Cor aplicada mantendo a luz do pixel (sombras e brilhos da pele continuam visíveis).</summary>
    private static float Colorize(byte[] px, int channel, int i, (double R, double G, double B) c)
    {
        var lum = (0.299f * px[i + 2] + 0.587f * px[i + 1] + 0.114f * px[i]) / 255f;
        var shine = Math.Max(0, lum - 0.75f) * 2.2f;
        return Math.Clamp(255 * (Channel(channel, c) * (0.3f + 1.4f * lum) + shine), 0, 255);
    }
}
