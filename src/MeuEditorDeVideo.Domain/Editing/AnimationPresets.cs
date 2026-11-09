using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>Predefinição de animação 2D aplicável a um clipe (RF153).</summary>
public sealed record AnimationPreset(string Id, string Name, string Description);

/// <summary>
/// Animações prontas (RF153) convertidas em keyframes de <see cref="AnimatedProperty"/>. Os offsets são relativos ao início
/// do clipe, alinhados ao quadro do projeto e sempre dentro de [0, duração]. Aplicar uma predefinição substitui apenas os
/// keyframes das propriedades que ela usa; os keyframes das demais propriedades são mantidos.
/// </summary>
public static class AnimationPresets
{
    private const string NoneId = "none";

    /// <summary>Duração máxima das entradas (deslizar, zoom, surgir).</summary>
    private const double EntranceSeconds = 0.6;

    /// <summary>Duração máxima do quicar.</summary>
    private const double BounceSeconds = 1.2;

    /// <summary>Clipes mais curtos que isto (em quadros) não recebem animação, exceto "none".</summary>
    private const int MinFrames = 2;

    public static IReadOnlyList<AnimationPreset> All { get; } =
    [
        new(NoneId, "Sem animação", "Remove as animações de posição, escala, rotação e opacidade."),
        new("slideLeft", "Entrar pela esquerda", "Desliza do lado esquerdo até a posição original."),
        new("slideRight", "Entrar pela direita", "Desliza do lado direito até a posição original."),
        new("slideUp", "Subir de baixo", "Sobe de baixo até a posição original."),
        new("zoomIn", "Aparecer com zoom", "Cresce a partir de 30% do tamanho, surgindo do transparente."),
        new("kenBurns", "Zoom lento (Ken Burns)", "Aproximação lenta e contínua ao longo de todo o clipe, com leve deslocamento."),
        new("pulse", "Pulsar", "Pulsação de escala de até +8% a cada meio segundo, um ciclo por segundo."),
        new("spin", "Girar", "Uma volta completa (360°) ao longo de todo o clipe."),
        new("wobble", "Balançar", "Oscila a rotação em ±5° a cada 0,25 s e termina na posição original."),
        new("bounce", "Quicar", "Cai de cima e quica três vezes, com alturas decrescentes, em até 1,2 s."),
        new("fadeInOut", "Surgir e sumir", "Surge do transparente no início e some no final do clipe."),
    ];

    public static AnimationPreset? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Aplica a predefinição ao clipe, substituindo os keyframes das propriedades que ela usa.
    /// Clipes com menos de dois quadros ficam inalterados (exceto com "none").
    /// </summary>
    /// <exception cref="ArgumentException">Quando <paramref name="presetId"/> não existe.</exception>
    public static Clip Apply(Clip clip, string presetId, ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(settings);
        if (Find(presetId) is null)
            throw new ArgumentException($"Predefinição de animação desconhecida: '{presetId}'.", nameof(presetId));

        var timing = new Timing(clip, settings);
        if (presetId != NoneId && timing.IsTooShort) return clip;

        var keyframes = clip.Keyframes;
        foreach (var (property, keys) in Build(presetId, timing))
        {
            var normalized = Normalize(keys);
            keyframes = normalized.IsEmpty ? keyframes.Remove(property) : keyframes.SetItem(property, normalized);
        }
        return clip with { Keyframes = keyframes };
    }

    private static Dictionary<string, List<Keyframe>> Build(string id, Timing t)
    {
        var c = t.Clip;
        var x = ClipAnimation.BaseValue(c, AnimatedProperty.X);
        var y = ClipAnimation.BaseValue(c, AnimatedProperty.Y);
        var scale = ClipAnimation.BaseValue(c, AnimatedProperty.Scale);
        var rotation = ClipAnimation.BaseValue(c, AnimatedProperty.Rotation);
        var opacity = ClipAnimation.BaseValue(c, AnimatedProperty.Opacity);
        var w = t.Width;
        var h = t.Height;
        var end = t.End;
        var entrance = t.Entrance;
        var zero = MediaTime.Zero;

        switch (id)
        {
            case NoneId:
                return new()
                {
                    [AnimatedProperty.X] = [],
                    [AnimatedProperty.Y] = [],
                    [AnimatedProperty.Scale] = [],
                    [AnimatedProperty.Rotation] = [],
                    [AnimatedProperty.Opacity] = [],
                };

            case "slideLeft":
                return new()
                {
                    [AnimatedProperty.X] = [t.Key(zero, x - w, Easing.EaseOut), t.Key(entrance, x, Easing.EaseOut)],
                };

            case "slideRight":
                return new()
                {
                    [AnimatedProperty.X] = [t.Key(zero, x + w, Easing.EaseOut), t.Key(entrance, x, Easing.EaseOut)],
                };

            case "slideUp":
                return new()
                {
                    [AnimatedProperty.Y] = [t.Key(zero, y + h, Easing.EaseOut), t.Key(entrance, y, Easing.EaseOut)],
                };

            case "zoomIn":
                return new()
                {
                    [AnimatedProperty.Scale] = [t.Key(zero, 0.3 * scale, Easing.EaseOut), t.Key(entrance, scale, Easing.EaseOut)],
                    [AnimatedProperty.Opacity] = [t.Key(zero, 0, Easing.EaseOut), t.Key(entrance, opacity, Easing.EaseOut)],
                };

            case "kenBurns":
                return new()
                {
                    [AnimatedProperty.Scale] = [t.Key(zero, scale, Easing.Linear), t.Key(end, 1.15 * scale, Easing.Linear)],
                    [AnimatedProperty.X] = [t.Key(zero, x, Easing.Linear), t.Key(end, x + 0.03 * w, Easing.Linear)],
                };

            case "pulse":
                return new()
                {
                    [AnimatedProperty.Scale] = Oscillate(t, 0.5, k => k % 2 == 0 ? scale : 1.08 * scale, scale),
                };

            case "spin":
                return new()
                {
                    [AnimatedProperty.Rotation] = [t.Key(zero, rotation, Easing.Linear), t.Key(end, rotation + 360, Easing.Linear)],
                };

            case "wobble":
                return new()
                {
                    [AnimatedProperty.Rotation] = Oscillate(t, 0.25, k => rotation + (k % 2 == 0 ? 5 : -5), rotation),
                };

            case "bounce":
            {
                // Pontos: queda inicial de 0,5 H, depois quicadas de 0,2 H e 0,08 H. Cada trecho dura proporcionalmente
                // à raiz da altura do salto (quedas e subidas mais rápidas em alturas pequenas).
                double[] heights = [0.5, 0, 0.2, 0, 0.08, 0];
                Easing[] easings = [Easing.EaseIn, Easing.EaseOut, Easing.EaseIn, Easing.EaseOut, Easing.EaseIn, Easing.EaseOut];
                double[] segments = [Math.Sqrt(0.5), Math.Sqrt(0.2), Math.Sqrt(0.2), Math.Sqrt(0.08), Math.Sqrt(0.08)];
                var total = segments.Sum();
                var span = Math.Min(BounceSeconds, end.TotalSeconds);

                var keys = new List<Keyframe>(heights.Length);
                var elapsed = 0.0;
                for (var i = 0; i < heights.Length; i++)
                {
                    if (i > 0) elapsed += segments[i - 1];
                    keys.Add(t.Key(MediaTime.FromSecondsApprox(span * elapsed / total), y - heights[i] * h, easings[i]));
                }
                return new() { [AnimatedProperty.Y] = keys };
            }

            case "fadeInOut":
                return new()
                {
                    [AnimatedProperty.Opacity] =
                    [
                        t.Key(zero, 0, Easing.EaseInOut),
                        t.Key(entrance, opacity, Easing.EaseInOut),
                        t.Key(end - entrance, opacity, Easing.EaseInOut),
                        t.Key(end, 0, Easing.EaseInOut),
                    ],
                };

            default:
                throw new InvalidOperationException($"Predefinição sem construtor: '{id}'.");
        }
    }

    /// <summary>Keyframes a cada <paramref name="stepSeconds"/> alternando valores; termina em <paramref name="finalValue"/>.</summary>
    private static List<Keyframe> Oscillate(Timing t, double stepSeconds, Func<int, double> valueAt, double finalValue)
    {
        var keys = new List<Keyframe>();
        for (var k = 0; ; k++)
        {
            var time = MediaTime.FromSecondsApprox(stepSeconds * k);
            if (time >= t.End) break;
            keys.Add(t.Key(time, valueAt(k), Easing.EaseInOut));
        }
        keys.Add(t.Key(t.End, finalValue, Easing.EaseInOut));
        return keys;
    }

    /// <summary>Ordena pelo offset e remove offsets repetidos (vale o último), após o arredondamento ao quadro.</summary>
    private static ImmutableList<Keyframe> Normalize(IEnumerable<Keyframe> keys)
    {
        var result = new List<Keyframe>();
        foreach (var key in keys.OrderBy(k => k.Offset.Ticks))
        {
            if (result.Count > 0 && result[^1].Offset == key.Offset) result[^1] = key;
            else result.Add(key);
        }
        return ImmutableList.CreateRange(result);
    }

    /// <summary>Contexto de tempo e dimensões de uma aplicação: todos os offsets passam por <see cref="Snap"/>.</summary>
    private sealed class Timing
    {
        private readonly FrameRate _rate;

        public Timing(Clip clip, ProjectSettings settings)
        {
            Clip = clip;
            _rate = settings.FrameRate;
            Width = settings.Width;
            Height = settings.Height;
            End = _rate.FrameToTime(_rate.TimeToFrame(clip.Duration));
        }

        public Clip Clip { get; }
        public double Width { get; }
        public double Height { get; }

        /// <summary>Último quadro inteiro dentro do clipe: limite superior de todos os offsets.</summary>
        public MediaTime End { get; }

        public bool IsTooShort => Clip.Duration < _rate.FrameToTime(MinFrames);

        /// <summary>Duração das entradas: no máximo 0,6 s e metade do clipe.</summary>
        public MediaTime Entrance =>
            Snap(MediaTime.Min(MediaTime.FromSecondsApprox(EntranceSeconds), new MediaTime(End.Ticks / 2)));

        public MediaTime Snap(MediaTime time) =>
            MediaTime.Min(_rate.SnapToFrame(MediaTime.Max(MediaTime.Zero, MediaTime.Min(End, time))), End);

        public Keyframe Key(MediaTime time, double value, Easing easing) => new(Snap(time), value, easing);
    }
}
