using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Correção de estabilização num instante: deslocamento (fração da largura da imagem), giro (rad) e ampliação.</summary>
public readonly record struct StabilizationCorrection(double Dx, double Dy, double Da, double Zoom)
{
    public static readonly StabilizationCorrection None = new(0, 0, 0, 1);
}

/// <summary>
/// Cálculos da composição avançada (F9), sem dependência de visão computacional: forma das máscaras animadas e
/// rastreadas, interpolação do rastreamento, vínculo de elementos ao movimento, correção de estabilização e a
/// geometria que leva um ponto da imagem de origem ao quadro do projeto (igual ao compositor).
/// </summary>
public static class CompositionMath
{
    /// <summary>Ampliação automática máxima da estabilização.</summary>
    public const double MaxAutoZoom = 1.35;

    // ---------- Rastreamento ----------

    /// <summary>Amostra do rastreamento no instante de origem (interpolada; fora do intervalo, a ponta mais próxima).</summary>
    public static TrackSample? SampleAt(MotionTracker tracker, MediaTime sourceTime)
    {
        var s = tracker.Samples;
        if (s.Count == 0) return null;
        if (sourceTime <= s[0].SourceTime) return s[0] with { SourceTime = sourceTime };
        if (sourceTime >= s[^1].SourceTime) return s[^1] with { SourceTime = sourceTime };
        int lo = 0, hi = s.Count - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (s[mid].SourceTime <= sourceTime) lo = mid;
            else hi = mid;
        }
        var a = s[lo];
        var b = s[hi];
        var span = (double)(b.SourceTime - a.SourceTime).Ticks;
        var f = span <= 0 ? 0 : (sourceTime - a.SourceTime).Ticks / span;
        return new TrackSample(sourceTime, Lerp(a.X, b.X, f), Lerp(a.Y, b.Y, f), Lerp(a.Width, b.Width, f), Lerp(a.Height, b.Height, f));
    }

    /// <summary>Junta amostras novas às existentes (substitui as do mesmo instante), mantendo a ordem de tempo.</summary>
    public static ImmutableList<TrackSample> MergeSamples(IEnumerable<TrackSample> existing, IEnumerable<TrackSample> added)
    {
        var map = new SortedDictionary<long, TrackSample>();
        foreach (var s in existing) map[s.SourceTime.Ticks] = s;
        foreach (var s in added) map[s.SourceTime.Ticks] = s;
        return [.. map.Values];
    }

    // ---------- Máscaras ----------

    /// <summary>
    /// Forma da máscara no offset do clipe (interpola as formas-chave) já deslocada pelo rastreamento, se houver.
    /// Retorna a máscara com <see cref="Mask.Points"/> resolvidos e sem formas-chave.
    /// </summary>
    public static Mask Resolve(Mask mask, Clip clip, MediaTime offset, MediaTime sourceTime)
    {
        var points = ShapeAt(mask, offset);
        if (mask.TrackerId is { } tid && clip.Trackers.Find(t => t.Id == tid) is { } tracker
            && SampleAt(tracker, sourceTime) is { } now && SampleAt(tracker, mask.TrackerReference) is { } reference)
        {
            var sx = reference.Width > 1e-6 ? now.Width / reference.Width : 1;
            var sy = reference.Height > 1e-6 ? now.Height / reference.Height : 1;
            // Segue a posição e o tamanho do objeto (escala em torno do centro rastreado).
            points = [.. points.Select(p => new MaskPoint(now.X + (p.X - reference.X) * sx, now.Y + (p.Y - reference.Y) * sy))];
        }
        return mask with { Points = points, Keys = [] };
    }

    /// <summary>Pontos da máscara no offset: interpola as formas-chave (mesma quantidade de pontos) ou usa a forma fixa.</summary>
    public static ImmutableList<MaskPoint> ShapeAt(Mask mask, MediaTime offset)
    {
        var keys = mask.Keys;
        if (keys.Count == 0) return mask.Points;
        if (offset <= keys[0].Offset) return keys[0].Points;
        if (offset >= keys[^1].Offset) return keys[^1].Points;
        for (var i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (offset >= b.Offset) continue;
            if (a.Points.Count != b.Points.Count || a.Easing == Easing.Hold) return a.Points;
            var span = (double)(b.Offset - a.Offset).Ticks;
            var f = ClipAnimation.Ease(a.Easing, span <= 0 ? 1 : (offset - a.Offset).Ticks / span);
            return [.. a.Points.Zip(b.Points, (p, q) => new MaskPoint(Lerp(p.X, q.X, f), Lerp(p.Y, q.Y, f)))];
        }
        return keys[^1].Points;
    }

    /// <summary>Define a forma no offset: com formas-chave, cria/atualiza a chave; sem, altera a forma fixa.</summary>
    public static Mask SetShape(Mask mask, MediaTime offset, ImmutableList<MaskPoint> points)
    {
        if (mask.Keys.Count == 0) return mask with { Points = points };
        var keys = mask.Keys.RemoveAll(k => k.Offset == offset);
        var previous = keys.FindLast(k => k.Offset < offset);
        keys = [.. keys.Add(new MaskShapeKey(offset, points, previous?.Easing ?? Easing.EaseInOut)).OrderBy(k => k.Offset.Ticks)];
        return mask with { Keys = keys, Points = points };
    }

    /// <summary>Liga/desliga a forma-chave no offset (a primeira captura a forma atual).</summary>
    public static Mask ToggleShapeKey(Mask mask, MediaTime offset)
    {
        var existing = mask.Keys.FindIndex(k => k.Offset == offset);
        if (existing >= 0)
        {
            var removed = mask.Keys[existing];
            var rest = mask.Keys.RemoveAt(existing);
            return rest.Count == 0 ? mask with { Keys = rest, Points = removed.Points } : mask with { Keys = rest };
        }
        var current = ShapeAt(mask, offset);
        return mask with { Keys = [.. mask.Keys.Add(new MaskShapeKey(offset, current)).OrderBy(k => k.Offset.Ticks)] };
    }

    /// <summary>Forma padrão de uma máscara nova (centralizada, metade da imagem).</summary>
    public static ImmutableList<MaskPoint> DefaultShape(MaskKind kind) => kind switch
    {
        MaskKind.Path => [new(0.5, 0.2), new(0.78, 0.45), new(0.66, 0.8), new(0.34, 0.8), new(0.22, 0.45)],
        _ => [new(0.25, 0.25), new(0.75, 0.75)],
    };

    /// <summary>Contorno da máscara como polígono (pontos normalizados), pronto para rasterizar.</summary>
    public static IReadOnlyList<MaskPoint> Outline(Mask mask, int ellipseSegments = 96, int smoothSubdivisions = 12)
    {
        var p = mask.Points;
        switch (mask.Kind)
        {
            case MaskKind.Rectangle when p.Count >= 2:
            {
                double l = Math.Min(p[0].X, p[1].X), r = Math.Max(p[0].X, p[1].X), t = Math.Min(p[0].Y, p[1].Y), b = Math.Max(p[0].Y, p[1].Y);
                return [new(l, t), new(r, t), new(r, b), new(l, b)];
            }
            case MaskKind.Ellipse when p.Count >= 2:
            {
                double cx = (p[0].X + p[1].X) / 2, cy = (p[0].Y + p[1].Y) / 2, rx = Math.Abs(p[1].X - p[0].X) / 2, ry = Math.Abs(p[1].Y - p[0].Y) / 2;
                var list = new MaskPoint[ellipseSegments];
                for (var i = 0; i < ellipseSegments; i++)
                {
                    var a = 2 * Math.PI * i / ellipseSegments;
                    list[i] = new MaskPoint(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a));
                }
                return list;
            }
            case MaskKind.Path when p.Count >= 3:
                return mask.Smooth ? CatmullRom(p, smoothSubdivisions) : p;
            default:
                return [];
        }
    }

    /// <summary>Curva fechada de Catmull-Rom (centrípeta simplificada) que passa por todos os pontos.</summary>
    private static List<MaskPoint> CatmullRom(IReadOnlyList<MaskPoint> p, int steps)
    {
        var n = p.Count;
        var result = new List<MaskPoint>(n * steps);
        for (var i = 0; i < n; i++)
        {
            var p0 = p[(i - 1 + n) % n];
            var p1 = p[i];
            var p2 = p[(i + 1) % n];
            var p3 = p[(i + 2) % n];
            for (var s = 0; s < steps; s++)
            {
                var t = (double)s / steps;
                double t2 = t * t, t3 = t2 * t;
                double X(double a, double b, double c, double d) =>
                    0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
                result.Add(new MaskPoint(X(p0.X, p1.X, p2.X, p3.X), X(p0.Y, p1.Y, p2.Y, p3.Y)));
            }
        }
        return result;
    }

    // ---------- Geometria da camada ----------

    /// <summary>Escala "contain" que ajusta a imagem de origem ao quadro do projeto.</summary>
    public static double FitScale(int imageWidth, int imageHeight, int projectWidth, int projectHeight) =>
        imageWidth <= 0 || imageHeight <= 0 ? 1 : Math.Min((double)projectWidth / imageWidth, (double)projectHeight / imageHeight);

    /// <summary>
    /// Ponto normalizado da imagem de origem → pixels do projeto relativos ao centro do quadro (mesma geometria 2D do
    /// compositor: ajuste ao quadro, escala, espelhamento, giro e posição).
    /// </summary>
    public static (double X, double Y) ToProject(Transform t, double u, double v, int imageWidth, int imageHeight, int projectWidth, int projectHeight) =>
        ToProject(t, u, v, imageWidth, imageHeight, FitScale(imageWidth, imageHeight, projectWidth, projectHeight));

    /// <summary>Como o anterior, com o tamanho da imagem e o ajuste explícitos (imagens geradas já em pixels do projeto: fit = 1).</summary>
    public static (double X, double Y) ToProject(Transform t, double u, double v, double imageWidth, double imageHeight, double fit)
    {
        var x = (u - 0.5) * imageWidth * fit * t.ScaleX * (t.FlipHorizontal ? -1 : 1);
        var y = (v - 0.5) * imageHeight * fit * t.ScaleY * (t.FlipVertical ? -1 : 1);
        var a = t.RotationDegrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(a), Math.Sin(a));
        return (x * cos - y * sin + t.X, x * sin + y * cos + t.Y);
    }

    /// <summary>Inverso de <see cref="ToProject(Transform, double, double, int, int, int, int)"/>: pixels do projeto (relativos ao centro) → ponto normalizado da imagem.</summary>
    public static (double U, double V) FromProject(Transform t, double px, double py, int imageWidth, int imageHeight, int projectWidth, int projectHeight) =>
        FromProject(t, px, py, imageWidth, imageHeight, FitScale(imageWidth, imageHeight, projectWidth, projectHeight));

    public static (double U, double V) FromProject(Transform t, double px, double py, double imageWidth, double imageHeight, double fit)
    {
        var a = t.RotationDegrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(a), Math.Sin(a));
        var dx = px - t.X;
        var dy = py - t.Y;
        var x = dx * cos + dy * sin;
        var y = -dx * sin + dy * cos;
        var sx = imageWidth * fit * t.ScaleX * (t.FlipHorizontal ? -1 : 1);
        var sy = imageHeight * fit * t.ScaleY * (t.FlipVertical ? -1 : 1);
        return (Math.Abs(sx) < 1e-9 ? 0.5 : x / sx + 0.5, Math.Abs(sy) < 1e-9 ? 0.5 : y / sy + 0.5);
    }

    /// <summary>
    /// Deslocamento (pixels do projeto) e fator de escala de um elemento vinculado ao rastreamento de outro clipe, no
    /// instante <paramref name="t"/> da timeline, relativo ao instante de referência do vínculo. Nulo se o vínculo é inválido.
    /// </summary>
    public static (double Dx, double Dy, double Scale)? LinkOffset(Project project, Sequence sequence, TrackerLink link, MediaTime t)
    {
        Clip? source = null;
        foreach (var track in sequence.Tracks)
            if (track.Clips.Find(c => c.Id == link.ClipId) is { } c) { source = c; break; }
        if (source?.MediaAssetId is not { } id || project.FindMedia(id) is not { } asset) return null;
        if (source.Trackers.Find(k => k.Id == link.TrackerId) is not { Samples.Count: > 0 } tracker) return null;

        (double X, double Y, double W)? At(MediaTime time)
        {
            var clamped = MediaTime.Max(source.Start, MediaTime.Min(source.End, time));
            var src = source.FreezeAt ?? source.SourceIn + source.Speed.SourceDurationOf(clamped - source.Start);
            if (SampleAt(tracker, src) is not { } s) return null;
            var tr = ClipAnimation.TransformAt(source, clamped - source.Start);
            var (x, y) = ToProject(tr, s.X, s.Y, asset.Width, asset.Height, project.Settings.Width, project.Settings.Height);
            return (x, y, s.Width);
        }

        if (At(t) is not { } now || At(link.ReferenceTime) is not { } reference) return null;
        var scale = link.FollowScale && reference.W > 1e-6 ? now.W / reference.W : 1;
        return (now.X - reference.X, now.Y - reference.Y, scale);
    }

    // ---------- Estabilização ----------

    private sealed class StabCache(MediaTime[] times, double[] cx, double[] cy, double[] ca, double zoom)
    {
        public MediaTime[] Times { get; } = times;
        public double[] Cx { get; } = cx;
        public double[] Cy { get; } = cy;
        public double[] Ca { get; } = ca;
        public double Zoom { get; } = zoom;
    }

    private static readonly ConditionalWeakTable<Stabilization, StabCache> StabCaches = new();

    /// <summary>
    /// Correção de estabilização no instante de origem: trajetória acumulada da câmera menos a trajetória suavizada
    /// (média móvel de <see cref="Stabilization.Smoothness"/> segundos para cada lado), e a ampliação que esconde as bordas.
    /// </summary>
    /// <param name="aspect">Largura/altura da imagem (converte o deslocamento vertical para a ampliação automática).</param>
    public static StabilizationCorrection Correction(Stabilization? stab, MediaTime sourceTime, double aspect)
    {
        if (stab is not { Enabled: true } || stab.Steps.Count < 2) return StabilizationCorrection.None;
        var cache = StabCaches.GetValue(stab, s => BuildCache(s, aspect));
        var times = cache.Times;
        int lo = 0, hi = times.Length - 1;
        if (sourceTime <= times[0]) hi = 0;
        else if (sourceTime >= times[^1]) lo = hi;
        else
        {
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (times[mid] <= sourceTime) lo = mid;
                else hi = mid;
            }
        }
        var i = sourceTime >= times[^1] ? times.Length - 1 : lo;
        return new StabilizationCorrection(cache.Cx[i], cache.Cy[i], cache.Ca[i], cache.Zoom);
    }

    private static StabCache BuildCache(Stabilization s, double aspect)
    {
        var steps = s.Steps;
        var n = steps.Count;
        var times = new MediaTime[n];
        double[] tx = new double[n], ty = new double[n], ta = new double[n];
        double ax = 0, ay = 0, aa = 0;
        for (var i = 0; i < n; i++)
        {
            ax += steps[i].Dx; ay += steps[i].Dy; aa += steps[i].Da;
            tx[i] = ax; ty[i] = ay; ta[i] = aa;
            times[i] = steps[i].SourceTime;
        }
        // Raio em amostras a partir do intervalo médio entre quadros.
        var avg = n > 1 ? (double)(times[^1] - times[0]).Ticks / (n - 1) : 1;
        var radius = Math.Max(1, (int)Math.Round(Math.Max(0, s.Smoothness) * MediaTime.TicksPerSecond / Math.Max(1, avg)));

        // Prefixos para médias móveis O(1).
        double[] px = new double[n + 1], py = new double[n + 1], pa = new double[n + 1];
        for (var i = 0; i < n; i++) { px[i + 1] = px[i] + tx[i]; py[i + 1] = py[i] + ty[i]; pa[i + 1] = pa[i] + ta[i]; }
        double[] cx = new double[n], cy = new double[n], ca = new double[n];
        double mx = 0, my = 0, ma = 0;
        for (var i = 0; i < n; i++)
        {
            // Janela simétrica (encolhe perto das pontas): um movimento contínuo da câmera (panorâmica) não vira correção.
            var r = Math.Min(radius, Math.Min(i, n - 1 - i));
            var a = i - r;
            var b = i + r;
            var count = b - a + 1;
            cx[i] = (px[b + 1] - px[a]) / count - tx[i];
            cy[i] = (py[b + 1] - py[a]) / count - ty[i];
            ca[i] = (pa[b + 1] - pa[a]) / count - ta[i];
            mx = Math.Max(mx, Math.Abs(cx[i])); my = Math.Max(my, Math.Abs(cy[i])); ma = Math.Max(ma, Math.Abs(ca[i]));
        }
        var zoom = s.Zoom > 0
            ? 1 + s.Zoom
            : Math.Min(MaxAutoZoom, 1 + 2 * Math.Max(mx, my * Math.Max(0.1, aspect)) + 2 * Math.Sin(ma));
        return new StabCache(times, cx, cy, ca, zoom);
    }

    /// <summary>
    /// Aplica a correção à transformação da camada: o deslocamento da imagem (em pixels da imagem ajustada) é girado e
    /// escalado pela transformação do clipe, o giro soma e a escala recebe a ampliação.
    /// </summary>
    public static Transform Apply(Transform t, StabilizationCorrection c, double fittedImageWidth)
    {
        if (c == StabilizationCorrection.None) return t;
        var a = t.RotationDegrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(a), Math.Sin(a));
        var sx = t.ScaleX * c.Zoom * (t.FlipHorizontal ? -1 : 1);
        var sy = t.ScaleY * c.Zoom * (t.FlipVertical ? -1 : 1);
        var dx = c.Dx * fittedImageWidth * sx;
        var dy = c.Dy * fittedImageWidth * sy;
        return t with
        {
            X = t.X + dx * cos - dy * sin,
            Y = t.Y + dx * sin + dy * cos,
            ScaleX = t.ScaleX * c.Zoom,
            ScaleY = t.ScaleY * c.Zoom,
            RotationDegrees = t.RotationDegrees + c.Da * 180 / Math.PI * (t.FlipHorizontal ^ t.FlipVertical ? -1 : 1),
        };
    }

    private static double Lerp(double a, double b, double f) => a + (b - a) * f;
}
