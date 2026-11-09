using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>Máscaras das transições por cortina e geométricas (RF081–RF083), em coordenadas normalizadas do quadro.</summary>
public static class TransitionMasks
{
    private const double Edge = 0.015;
    private const int BlindCount = 8;

    public static bool IsHidden(TransitionMask mask) => mask.Progress <= 0;

    /// <summary>Cobertura 0–1 do clipe de entrada no ponto (u, v) ∈ [0,1]², com borda suave.</summary>
    public static double Coverage(TransitionMask mask, double u, double v, double aspect)
    {
        var p = Math.Clamp(mask.Progress, 0, 1);
        if (p >= 1) return 1;
        switch (mask.Kind)
        {
            case TransitionKind.WipeLeft: return Reveal(u - (1 - p) * (1 + Edge));
            case TransitionKind.WipeRight: return Reveal(p * (1 + Edge) - u);
            case TransitionKind.WipeUp: return Reveal(v - (1 - p) * (1 + Edge));
            case TransitionKind.WipeDown: return Reveal(p * (1 + Edge) - v);
            case TransitionKind.Circle:
            {
                // Distância ao centro corrigida pela proporção; 1 = canto do quadro.
                var dx = (u - 0.5) * aspect;
                var dy = v - 0.5;
                var d = Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(aspect * aspect * 0.25 + 0.25);
                return Reveal(p * (1 + Edge) - d);
            }
            case TransitionKind.Diamond:
            {
                var d = Math.Abs(u - 0.5) + Math.Abs(v - 0.5);
                return Reveal(p * (1 + Edge) - d);
            }
            case TransitionKind.Clock:
            {
                // Varredura no sentido horário a partir das 12 horas.
                var angle = Math.Atan2((u - 0.5) * aspect, -(v - 0.5));
                var frac = (angle < 0 ? angle + 2 * Math.PI : angle) / (2 * Math.PI);
                return frac <= p ? 1 : 0;
            }
            case TransitionKind.Blinds:
            {
                var frac = v * BlindCount - Math.Floor(v * BlindCount);
                return Reveal(p * (1 + Edge * BlindCount) - frac, edge: Edge * BlindCount);
            }
            default:
                return 1;
        }
    }

    private static double Reveal(double distanceInside, double edge = Edge)
    {
        var t = Math.Clamp(distanceInside / edge, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
