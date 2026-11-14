using System.Collections.Immutable;

namespace MeuEditorDeVideo.Domain.Model;

/// <summary>Ponto da curva de velocidade: posição em fração da duração do clipe (0…1) e multiplicador relativo (&gt; 0).</summary>
public sealed record SpeedPoint(double Position, double Speed);

/// <summary>
/// Curvas de velocidade (speed ramping). Entre os pontos a velocidade varia linearmente; antes do primeiro e depois do
/// último fica constante. A curva é normalizada pela média, então o clipe consome o mesmo trecho de origem.
/// </summary>
public static class SpeedRamps
{
    /// <summary>Velocidade mínima relativa de um ponto (evita parar a imagem por completo).</summary>
    public const double MinSpeed = 0.1;

    public const double MaxSpeed = 10;

    /// <summary>Curvas prontas (nome e pontos), como nos editores populares.</summary>
    public static IReadOnlyList<(string Name, ImmutableList<SpeedPoint> Points)> Presets { get; } =
    [
        ("Montagem", [new(0, 1.6), new(0.3, 1.6), new(0.4, 0.3), new(0.6, 0.3), new(0.7, 1.6), new(1, 1.6)]),
        ("Herói", [new(0, 1.2), new(0.35, 1.2), new(0.45, 0.2), new(0.65, 0.2), new(0.75, 1.2), new(1, 1.2)]),
        ("Bala", [new(0, 2.5), new(0.4, 2.5), new(0.5, 0.15), new(0.6, 2.5), new(1, 2.5)]),
        ("Pulo", [new(0, 0.5), new(0.45, 0.5), new(0.5, 3), new(0.55, 0.5), new(1, 0.5)]),
        ("Acelerar", [new(0, 0.4), new(1, 2.5)]),
        ("Desacelerar", [new(0, 2.5), new(1, 0.4)]),
    ];

    /// <summary>Pontos ordenados e válidos (posição 0…1, velocidade entre <see cref="MinSpeed"/> e <see cref="MaxSpeed"/>).</summary>
    public static ImmutableList<SpeedPoint> Normalize(IEnumerable<SpeedPoint> points) =>
        points.Select(p => new SpeedPoint(Math.Clamp(p.Position, 0, 1), Math.Clamp(p.Speed, MinSpeed, MaxSpeed)))
              .OrderBy(p => p.Position).ToImmutableList();

    /// <summary>Velocidade bruta (sem normalizar) na fração <paramref name="f"/>.</summary>
    public static double Raw(IReadOnlyList<SpeedPoint> points, double f)
    {
        if (points.Count == 0) return 1;
        if (f <= points[0].Position) return points[0].Speed;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (f <= b.Position)
                return b.Position <= a.Position ? b.Speed : a.Speed + (b.Speed - a.Speed) * (f - a.Position) / (b.Position - a.Position);
        }
        return points[^1].Speed;
    }

    /// <summary>Integral da velocidade bruta de 0 a <paramref name="f"/> (trapézios exatos, a curva é linear por partes).</summary>
    private static double Integral(IReadOnlyList<SpeedPoint> points, double f)
    {
        f = Math.Clamp(f, 0, 1);
        var sum = 0.0;
        var x = 0.0;
        var v = Raw(points, 0);
        foreach (var p in points.Where(p => p.Position > 0 && p.Position < f).Append(new SpeedPoint(f, Raw(points, f))))
        {
            var pv = Raw(points, p.Position);
            sum += (p.Position - x) * (v + pv) / 2;
            x = p.Position;
            v = pv;
        }
        return sum;
    }

    /// <summary>Fração do trecho de origem já reproduzida na fração <paramref name="f"/> do clipe (0…1, crescente).</summary>
    public static double Progress(IReadOnlyList<SpeedPoint> points, double f)
    {
        if (points.Count < 2) return Math.Clamp(f, 0, 1);
        var total = Integral(points, 1);
        return total <= 0 ? Math.Clamp(f, 0, 1) : Math.Clamp(Integral(points, f) / total, 0, 1);
    }

    /// <summary>Velocidade relativa à do clipe na fração <paramref name="f"/> (1 = velocidade base).</summary>
    public static double RelativeAt(IReadOnlyList<SpeedPoint> points, double f)
    {
        if (points.Count < 2) return 1;
        var mean = Integral(points, 1);
        return mean <= 0 ? 1 : Raw(points, f) / mean;
    }
}
