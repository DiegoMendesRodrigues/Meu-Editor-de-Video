using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Avaliação e edição de keyframes (RF076, RF144–RF148). Offsets são relativos ao início do clipe na timeline.
/// Uma propriedade sem keyframes usa o valor base do clipe (Transform/Opacity); com keyframes, o valor é interpolado
/// e alterações passam a criar/atualizar keyframes no instante atual ("auto-keyframe").
/// </summary>
public static class ClipAnimation
{
    /// <summary>Volume máximo do clipe (+12 dB).</summary>
    public const double MaxVolume = 4;

    /// <summary>
    /// Ganho do áudio no offset: volume (com keyframes) × entrada/saída gradual (RF121, RF122).
    /// A curva das rampas é quadrática (percebida como mais natural que a linear).
    /// </summary>
    public static double GainAt(Clip clip, MediaTime offset)
    {
        var g = Math.Clamp(ValueAt(clip, AnimatedProperty.Volume, offset), 0, MaxVolume);
        if (clip.FadeIn > MediaTime.Zero && offset < clip.FadeIn)
        {
            var t = Math.Clamp((double)offset.Ticks / clip.FadeIn.Ticks, 0, 1);
            g *= t * t;
        }
        var remaining = clip.Duration - offset;
        if (clip.FadeOut > MediaTime.Zero && remaining < clip.FadeOut)
        {
            var t = Math.Clamp((double)remaining.Ticks / clip.FadeOut.Ticks, 0, 1);
            g *= t * t;
        }
        return g;
    }

    /// <summary>
    /// Multiplica o volume do clipe (valor base e todos os keyframes de volume) por <paramref name="factor"/>,
    /// preservando a forma do envelope — usado na normalização (RF126). Limitado a <see cref="MaxVolume"/>.
    /// </summary>
    public static Clip ScaleVolume(Clip clip, double factor)
    {
        if (factor < 0 || double.IsNaN(factor) || double.IsInfinity(factor)) throw new ArgumentOutOfRangeException(nameof(factor));
        var scaled = clip with { Volume = Math.Clamp(clip.Volume * factor, 0, MaxVolume) };
        if (KeysOf(clip, AnimatedProperty.Volume) is { } keys)
            scaled = WithKeys(scaled, AnimatedProperty.Volume,
                [.. keys.Select(k => k with { Value = Math.Clamp(k.Value * factor, 0, MaxVolume) })]);
        return scaled;
    }

    public static double Ease(Easing easing, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return easing switch
        {
            Easing.Linear => t,
            Easing.EaseIn => t * t * t,
            Easing.EaseOut => 1 - Math.Pow(1 - t, 3),
            Easing.EaseInOut => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            Easing.Hold => 0,
            _ => t,
        };
    }

    /// <summary>Valor interpolado da lista de keyframes (ordenada) no offset; vazia = <paramref name="baseValue"/>.</summary>
    public static double Evaluate(IReadOnlyList<Keyframe>? keys, MediaTime offset, double baseValue)
    {
        if (keys is null || keys.Count == 0) return baseValue;
        if (offset <= keys[0].Offset) return keys[0].Value;
        if (offset >= keys[^1].Offset) return keys[^1].Value;
        for (var i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (offset < b.Offset)
            {
                var span = (double)(b.Offset - a.Offset).Ticks;
                var t = span <= 0 ? 1 : (offset - a.Offset).Ticks / span;
                return a.Value + (b.Value - a.Value) * Ease(a.Easing, t);
            }
        }
        return keys[^1].Value;
    }

    public static double BaseValue(Clip clip, string property) => property switch
    {
        AnimatedProperty.X => clip.Transform.X,
        AnimatedProperty.Y => clip.Transform.Y,
        AnimatedProperty.Scale => clip.Transform.ScaleX,
        AnimatedProperty.Rotation => clip.Transform.RotationDegrees,
        AnimatedProperty.Opacity => clip.Opacity,
        AnimatedProperty.Volume => clip.Volume,
        _ => throw new ArgumentException($"Propriedade não animável: {property}", nameof(property)),
    };

    public static ImmutableList<Keyframe>? KeysOf(Clip clip, string property) =>
        clip.Keyframes.TryGetValue(property, out var list) && list.Count > 0 ? list : null;

    public static bool IsAnimated(Clip clip, string property) => KeysOf(clip, property) is not null;

    public static double ValueAt(Clip clip, string property, MediaTime offset) =>
        Evaluate(KeysOf(clip, property), offset, BaseValue(clip, property));

    /// <summary>Transformação efetiva no offset (escala uniforme animada mantém a proporção X/Y da base).</summary>
    public static Transform TransformAt(Clip clip, MediaTime offset)
    {
        if (clip.Keyframes.IsEmpty) return clip.Transform;
        var t = clip.Transform;
        var scale = ValueAt(clip, AnimatedProperty.Scale, offset);
        var ratio = t.ScaleX == 0 ? 1 : t.ScaleY / t.ScaleX;
        return t with
        {
            X = ValueAt(clip, AnimatedProperty.X, offset),
            Y = ValueAt(clip, AnimatedProperty.Y, offset),
            ScaleX = scale,
            ScaleY = IsAnimated(clip, AnimatedProperty.Scale) ? scale * ratio : t.ScaleY,
            RotationDegrees = ValueAt(clip, AnimatedProperty.Rotation, offset),
        };
    }

    /// <summary>Opacidade efetiva no offset, incluindo entrada/saída gradual.</summary>
    public static double OpacityAt(Clip clip, MediaTime offset)
    {
        var o = Math.Clamp(ValueAt(clip, AnimatedProperty.Opacity, offset), 0, 1);
        if (clip.FadeIn > MediaTime.Zero && offset < clip.FadeIn)
            o *= Math.Clamp((double)offset.Ticks / clip.FadeIn.Ticks, 0, 1);
        var remaining = clip.Duration - offset;
        if (clip.FadeOut > MediaTime.Zero && remaining < clip.FadeOut)
            o *= Math.Clamp((double)remaining.Ticks / clip.FadeOut.Ticks, 0, 1);
        return o;
    }

    /// <summary>Define o valor: com keyframes, cria/atualiza o keyframe no offset; sem, altera o valor base.</summary>
    public static Clip SetValue(Clip clip, string property, MediaTime offset, double value)
    {
        value = Clamp(property, value);
        if (KeysOf(clip, property) is { } keys)
            return WithKeys(clip, property, Upsert(keys, ClampOffset(clip, offset), value, null));
        return SetBase(clip, property, value);
    }

    /// <summary>Liga/desliga o keyframe no offset. O primeiro keyframe captura o valor atual.</summary>
    public static Clip ToggleKeyframe(Clip clip, string property, MediaTime offset)
    {
        offset = ClampOffset(clip, offset);
        var keys = KeysOf(clip, property) ?? [];
        var existing = keys.FindIndex(k => k.Offset == offset);
        if (existing >= 0)
        {
            var remaining = keys.RemoveAt(existing);
            if (remaining.Count == 0)
            {
                // Último keyframe removido: o valor animado vira o valor base.
                return SetBase(clip, property, keys[existing].Value) with { Keyframes = clip.Keyframes.Remove(property) };
            }
            return WithKeys(clip, property, remaining);
        }
        return WithKeys(clip, property, Upsert(keys, offset, ValueAt(clip, property, offset), null));
    }

    public static bool HasKeyframeAt(Clip clip, string property, MediaTime offset) =>
        KeysOf(clip, property)?.Exists(k => k.Offset == offset) == true;

    /// <summary>Define a curva de interpolação do keyframe no offset (ou do anterior mais próximo).</summary>
    public static Clip SetEasing(Clip clip, string property, MediaTime offset, Easing easing)
    {
        if (KeysOf(clip, property) is not { } keys) return clip;
        var index = keys.FindLastIndex(k => k.Offset <= offset);
        if (index < 0) index = 0;
        return WithKeys(clip, property, keys.SetItem(index, keys[index] with { Easing = easing }));
    }

    public static Clip ClearKeyframes(Clip clip, string property, MediaTime offset)
    {
        if (!IsAnimated(clip, property)) return clip;
        var value = ValueAt(clip, property, offset);
        return SetBase(clip, property, value) with { Keyframes = clip.Keyframes.Remove(property) };
    }

    /// <summary>Anterior/próximo keyframe (de qualquer propriedade) em relação ao offset, para navegação.</summary>
    public static MediaTime? Adjacent(Clip clip, MediaTime offset, bool forward)
    {
        var all = clip.Keyframes.Values.SelectMany(k => k).Select(k => k.Offset);
        return forward
            ? all.Where(o => o > offset).Select(o => (MediaTime?)o).DefaultIfEmpty(null).Min()
            : all.Where(o => o < offset).Select(o => (MediaTime?)o).DefaultIfEmpty(null).Max();
    }

    public static double Clamp(string property, double value) => property switch
    {
        AnimatedProperty.Opacity => Math.Clamp(value, 0, 1),
        AnimatedProperty.Volume => Math.Clamp(value, 0, MaxVolume),
        AnimatedProperty.Scale => Math.Clamp(value, 0.01, 20),
        AnimatedProperty.Rotation => Math.Clamp(value, -3600, 3600),
        _ => Math.Clamp(value, -20000, 20000),
    };

    private static MediaTime ClampOffset(Clip clip, MediaTime offset) =>
        MediaTime.Max(MediaTime.Zero, MediaTime.Min(clip.Duration, offset));

    private static Clip SetBase(Clip clip, string property, double value)
    {
        var t = clip.Transform;
        return property switch
        {
            AnimatedProperty.X => clip with { Transform = t with { X = value } },
            AnimatedProperty.Y => clip with { Transform = t with { Y = value } },
            AnimatedProperty.Scale => clip with { Transform = t with { ScaleX = value, ScaleY = t.ScaleX == 0 ? value : value * t.ScaleY / t.ScaleX } },
            AnimatedProperty.Rotation => clip with { Transform = t with { RotationDegrees = value } },
            AnimatedProperty.Opacity => clip with { Opacity = value },
            AnimatedProperty.Volume => clip with { Volume = value },
            _ => throw new ArgumentException($"Propriedade não animável: {property}", nameof(property)),
        };
    }

    private static ImmutableList<Keyframe> Upsert(ImmutableList<Keyframe> keys, MediaTime offset, double value, Easing? easing)
    {
        var index = keys.FindIndex(k => k.Offset == offset);
        if (index >= 0) return keys.SetItem(index, keys[index] with { Value = value, Easing = easing ?? keys[index].Easing });
        var previous = keys.FindLast(k => k.Offset < offset);
        var key = new Keyframe(offset, value, easing ?? previous?.Easing ?? Easing.EaseInOut);
        return [.. keys.Add(key).OrderBy(k => k.Offset.Ticks)];
    }

    private static Clip WithKeys(Clip clip, string property, ImmutableList<Keyframe> keys) =>
        clip with { Keyframes = clip.Keyframes.SetItem(property, keys) };
}
