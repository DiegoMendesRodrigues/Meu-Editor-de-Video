// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Ponto a manter no centro do quadro, em frações da imagem de origem (0…1), num offset do clipe.</summary>
public readonly record struct FocusSample(MediaTime Offset, double X, double Y);

/// <summary>
/// Reenquadrar automaticamente: muda o formato do projeto (ex.: 16:9 → 9:16) e faz cada clipe de vídeo ou imagem
/// cobrir o novo quadro, deslocado para manter a pessoa (ou o centro) visível. O resultado são keyframes de posição
/// comuns, que o usuário pode ajustar depois.
/// </summary>
public static class Reframe
{
    /// <summary>
    /// Novo tamanho do projeto no formato <paramref name="aspectWidth"/>:<paramref name="aspectHeight"/>, mantendo o lado
    /// menor do tamanho atual (1920×1080 em 9:16 → 1080×1920). Dimensões pares.
    /// </summary>
    public static (int Width, int Height) Size(ProjectSettings settings, int aspectWidth, int aspectHeight)
    {
        if (aspectWidth <= 0 || aspectHeight <= 0) throw new ArgumentOutOfRangeException(nameof(aspectWidth));
        var shortSide = Math.Min(settings.Width, settings.Height);
        int w, h;
        if (aspectWidth <= aspectHeight)
        {
            w = shortSide;
            h = (int)Math.Round((double)shortSide * aspectHeight / aspectWidth);
        }
        else
        {
            h = shortSide;
            w = (int)Math.Round((double)shortSide * aspectWidth / aspectHeight);
        }
        return (Even(w), Even(h));

        static int Even(int v) => Math.Max(16, v - v % 2);
    }

    /// <summary>
    /// Faz o clipe cobrir o quadro <paramref name="projectWidth"/>×<paramref name="projectHeight"/> e o posiciona para
    /// manter o foco no centro. Sem amostras, centraliza. Substitui a escala e a posição (e os keyframes delas).
    /// </summary>
    /// <param name="threshold">Desvio mínimo, em fração da largura do quadro, para criar um novo keyframe.</param>
    public static Clip Apply(Clip clip, int imageWidth, int imageHeight, int projectWidth, int projectHeight,
        IReadOnlyList<FocusSample>? samples = null, double threshold = 0.02)
    {
        if (imageWidth <= 0 || imageHeight <= 0) return clip;
        var contain = Math.Min((double)projectWidth / imageWidth, (double)projectHeight / imageHeight);
        var cover = Math.Max((double)projectWidth / imageWidth, (double)projectHeight / imageHeight);
        var scale = cover / contain;
        double shownW = imageWidth * cover, shownH = imageHeight * cover;
        double maxX = Math.Max(0, (shownW - projectWidth) / 2), maxY = Math.Max(0, (shownH - projectHeight) / 2);
        var flipX = clip.Transform.FlipHorizontal ? -1 : 1;
        var flipY = clip.Transform.FlipVertical ? -1 : 1;

        double PosX(double fx) => Math.Clamp(-(fx - 0.5) * shownW * flipX, -maxX, maxX);
        double PosY(double fy) => Math.Clamp(-(fy - 0.5) * shownH * flipY, -maxY, maxY);

        var keys = clip.Keyframes.Remove(AnimatedProperty.X).Remove(AnimatedProperty.Y).Remove(AnimatedProperty.Scale);
        var points = samples?.Where(s => s.Offset >= MediaTime.Zero && s.Offset <= clip.Duration).OrderBy(s => s.Offset).ToList() ?? [];
        double x0 = 0, y0 = 0;
        if (points.Count > 0)
        {
            x0 = PosX(points[0].X);
            y0 = PosY(points[0].Y);
            var xs = Reduce(points.Select(p => (p.Offset, PosX(p.X))).ToList(), threshold * projectWidth);
            var ys = Reduce(points.Select(p => (p.Offset, PosY(p.Y))).ToList(), threshold * projectWidth);
            if (maxX > 0.5 && xs.Count > 1) keys = keys.SetItem(AnimatedProperty.X, xs);
            if (maxY > 0.5 && ys.Count > 1) keys = keys.SetItem(AnimatedProperty.Y, ys);
        }

        return clip with
        {
            Transform = clip.Transform with { ScaleX = scale, ScaleY = scale, X = x0, Y = y0 },
            Keyframes = keys,
        };
    }

    /// <summary>Mantém só os pontos que se afastam mais que <paramref name="tolerance"/> do último mantido (e o último).</summary>
    private static ImmutableList<Keyframe> Reduce(List<(MediaTime Offset, double Value)> points, double tolerance)
    {
        var result = ImmutableList.CreateBuilder<Keyframe>();
        for (var i = 0; i < points.Count; i++)
        {
            var (offset, value) = points[i];
            var last = i == points.Count - 1;
            if (result.Count == 0 || Math.Abs(value - result[^1].Value) > tolerance || (last && value != result[^1].Value))
                result.Add(new Keyframe(offset, Math.Round(value, 1), Easing.EaseInOut));
        }
        return result.ToImmutable();
    }

    /// <summary>Média móvel centrada (raio em amostras), para o enquadramento não tremer.</summary>
    public static IReadOnlyList<FocusSample> Smooth(IReadOnlyList<FocusSample> samples, int radius)
    {
        if (radius <= 0 || samples.Count < 3) return samples;
        var result = new List<FocusSample>(samples.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            int a = Math.Max(0, i - radius), b = Math.Min(samples.Count - 1, i + radius);
            double sx = 0, sy = 0;
            for (var j = a; j <= b; j++) { sx += samples[j].X; sy += samples[j].Y; }
            var n = b - a + 1;
            result.Add(samples[i] with { X = sx / n, Y = sy / n });
        }
        return result;
    }
}
