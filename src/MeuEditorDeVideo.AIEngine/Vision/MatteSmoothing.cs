using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Suavização temporal das máscaras da segmentação: cada quadro é combinado com as máscaras dos quadros vizinhos
/// (até ±2), com peso gaussiano pela distância no tempo e, em cada pixel, pela semelhança com o quadro central.
/// Diferenças pequenas (oscilação da borda de um quadro para o outro) são suavizadas; diferenças grandes (a pessoa se
/// moveu) não entram na média, o que evita rastros. O resultado depende só das máscaras de origem, então é o mesmo em
/// qualquer ordem de acesso (pré-visualização, saltos na timeline e exportação).
/// </summary>
public static class MatteSmoothing
{
    /// <summary>Quantos quadros de cada lado entram na média para a intensidade 0…1.</summary>
    public static int Radius(double strength) => strength <= 0.001 ? 0 : strength < 0.4 ? 1 : 2;

    /// <summary>Mistura <paramref name="center"/> com as máscaras vizinhas (deslocamento em quadros e máscara).</summary>
    public static Matte Blend(Matte center, IReadOnlyList<(int Offset, Matte Matte)> neighbors, double strength)
    {
        strength = Math.Clamp(strength, 0, 1);
        var usable = neighbors.Where(n => n.Offset != 0 && n.Matte.Width == center.Width && n.Matte.Height == center.Height
            && Math.Abs(n.Offset) <= Radius(strength)).ToArray();
        if (usable.Length == 0) return center;

        // Peso no tempo: gaussiana com sigma crescente; tolerância em níveis de cinza para a semelhança.
        var sigma = 0.6 + 1.4 * strength;
        var tolerance = 24 + 200 * strength;
        var timeWeights = usable.Select(n => Math.Exp(-n.Offset * n.Offset / (2 * sigma * sigma))).ToArray();

        var c = center.Alpha;
        var result = new byte[c.Length];
        for (var i = 0; i < c.Length; i++)
        {
            double v = c[i], sum = v, weight = 1;
            for (var k = 0; k < usable.Length; k++)
            {
                double other = usable[k].Matte.Alpha[i];
                var similarity = 1 - Math.Abs(other - v) / tolerance;
                if (similarity <= 0) continue;
                var w = timeWeights[k] * similarity;
                sum += w * other;
                weight += w;
            }
            result[i] = (byte)Math.Clamp(Math.Round(sum / weight), 0, 255);
        }
        return new Matte(center.Width, center.Height, result);
    }
}
