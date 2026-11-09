using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class ClipAnimationTests
{
    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static Clip NewClip(double duration = 4, Transform? transform = null, double opacity = 1) => new()
    {
        Id = Guid.NewGuid(),
        Start = MediaTime.Zero,
        Duration = S(duration),
        Transform = transform ?? Transform.Identity,
        Opacity = opacity,
    };

    private static Clip WithKeys(Clip clip, string property, params Keyframe[] keys) =>
        clip with { Keyframes = clip.Keyframes.SetItem(property, ImmutableList.Create(keys)) };

    // ---------------------------------------------------------------- curvas de easing

    [Theory]
    [InlineData(Easing.Linear)]
    [InlineData(Easing.EaseIn)]
    [InlineData(Easing.EaseOut)]
    [InlineData(Easing.EaseInOut)]
    public void Ease_vai_de_0_a_1_nas_curvas_que_interpolam(Easing easing)
    {
        Assert.Equal(0, ClipAnimation.Ease(easing, 0), 9);
        Assert.Equal(1, ClipAnimation.Ease(easing, 1), 9);
    }

    [Fact]
    public void Ease_hold_permanece_em_zero_no_intervalo()
    {
        Assert.Equal(0, ClipAnimation.Ease(Easing.Hold, 0.5), 9);
        Assert.Equal(0, ClipAnimation.Ease(Easing.Hold, 1), 9);
    }

    [Fact]
    public void Ease_limita_o_progresso_entre_0_e_1()
    {
        Assert.Equal(1, ClipAnimation.Ease(Easing.Linear, 1.5), 9);
        Assert.Equal(0, ClipAnimation.Ease(Easing.Linear, -0.5), 9);
    }

    // ---------------------------------------------------------------- Evaluate

    [Fact]
    public void Evaluate_sem_keyframes_retorna_o_valor_base()
    {
        Assert.Equal(7, ClipAnimation.Evaluate(null, S(1), 7));
        Assert.Equal(7, ClipAnimation.Evaluate([], S(1), 7));
    }

    [Fact]
    public void Evaluate_antes_do_primeiro_e_depois_do_ultimo_keyframe_usa_os_extremos()
    {
        var keys = ImmutableList.Create(new Keyframe(S(1), 5, Easing.Linear), new Keyframe(S(2), 15, Easing.Linear));

        Assert.Equal(5, ClipAnimation.Evaluate(keys, S(0), 0));
        Assert.Equal(15, ClipAnimation.Evaluate(keys, S(3), 0));
    }

    [Fact]
    public void Evaluate_interpolacao_linear_no_ponto_medio()
    {
        var keys = ImmutableList.Create(new Keyframe(S(0), 0, Easing.Linear), new Keyframe(S(2), 10, Easing.Linear));

        Assert.Equal(5, ClipAnimation.Evaluate(keys, S(1), 0), 9);
    }

    [Fact]
    public void Evaluate_hold_mantem_o_valor_anterior_ate_o_proximo_keyframe()
    {
        var keys = ImmutableList.Create(new Keyframe(S(0), 10, Easing.Hold), new Keyframe(S(2), 20, Easing.Linear));

        Assert.Equal(10, ClipAnimation.Evaluate(keys, S(1.9), 0), 9);
        Assert.Equal(20, ClipAnimation.Evaluate(keys, S(2), 0), 9);
    }

    // ---------------------------------------------------------------- SetValue

    [Fact]
    public void SetValue_sem_keyframes_altera_o_valor_base()
    {
        var clip = NewClip();

        var moved = ClipAnimation.SetValue(clip, AnimatedProperty.X, S(1), 42);
        var faded = ClipAnimation.SetValue(clip, AnimatedProperty.Opacity, S(1), 0.5);

        Assert.Equal(42, moved.Transform.X);
        Assert.True(moved.Keyframes.IsEmpty);
        Assert.Equal(0.5, faded.Opacity);
    }

    [Fact]
    public void SetValue_com_keyframes_cria_e_depois_atualiza_o_keyframe_do_offset()
    {
        var clip = WithKeys(NewClip(), AnimatedProperty.X,
            new Keyframe(S(0), 0, Easing.Linear), new Keyframe(S(4), 100, Easing.Linear));

        var created = ClipAnimation.SetValue(clip, AnimatedProperty.X, S(2), 50);
        var updated = ClipAnimation.SetValue(created, AnimatedProperty.X, S(2), 60);

        var keys = ClipAnimation.KeysOf(created, AnimatedProperty.X)!;
        Assert.Equal(3, keys.Count);
        Assert.Equal(50, keys.Single(k => k.Offset == S(2)).Value);
        Assert.Equal(0, created.Transform.X);
        var updatedKeys = ClipAnimation.KeysOf(updated, AnimatedProperty.X)!;
        Assert.Equal(3, updatedKeys.Count);
        Assert.Equal(60, updatedKeys.Single(k => k.Offset == S(2)).Value);
    }

    [Fact]
    public void SetValue_limita_o_valor_ao_intervalo_da_propriedade()
    {
        var clip = NewClip();

        var opacity = ClipAnimation.SetValue(clip, AnimatedProperty.Opacity, S(0), 3);
        var scale = ClipAnimation.SetValue(clip, AnimatedProperty.Scale, S(0), 0);

        Assert.Equal(1, opacity.Opacity);
        Assert.Equal(0.01, scale.Transform.ScaleX);
    }

    // ---------------------------------------------------------------- ToggleKeyframe

    [Fact]
    public void ToggleKeyframe_adiciona_com_o_valor_atual_e_remove_em_seguida()
    {
        var clip = NewClip(transform: new Transform { X = 7 });

        var added = ClipAnimation.ToggleKeyframe(clip, AnimatedProperty.X, S(1));
        var keys = ClipAnimation.KeysOf(added, AnimatedProperty.X)!;
        Assert.Equal(S(1), Assert.Single(keys).Offset);
        Assert.Equal(7, Assert.Single(keys).Value);

        var removed = ClipAnimation.ToggleKeyframe(added, AnimatedProperty.X, S(1));
        Assert.False(ClipAnimation.IsAnimated(removed, AnimatedProperty.X));
        Assert.Equal(7, removed.Transform.X);
    }

    [Fact]
    public void ToggleKeyframe_em_propriedade_animada_captura_o_valor_interpolado()
    {
        var clip = WithKeys(NewClip(), AnimatedProperty.X,
            new Keyframe(S(0), 0, Easing.Linear), new Keyframe(S(4), 100, Easing.Linear));

        var toggled = ClipAnimation.ToggleKeyframe(clip, AnimatedProperty.X, S(2));

        var added = ClipAnimation.KeysOf(toggled, AnimatedProperty.X)!.Single(k => k.Offset == S(2));
        Assert.Equal(50, added.Value, 9);
    }

    [Fact]
    public void Remover_o_ultimo_keyframe_grava_o_valor_animado_no_base()
    {
        var clip = NewClip(transform: new Transform { X = 7 });
        var keyed = ClipAnimation.ToggleKeyframe(clip, AnimatedProperty.X, S(1));
        var changed = ClipAnimation.SetValue(keyed, AnimatedProperty.X, S(1), 9);

        var removed = ClipAnimation.ToggleKeyframe(changed, AnimatedProperty.X, S(1));

        Assert.False(removed.Keyframes.ContainsKey(AnimatedProperty.X));
        Assert.Equal(9, removed.Transform.X);
    }

    [Fact]
    public void ClearKeyframes_grava_o_valor_no_offset_no_base_e_remove_a_propriedade()
    {
        var clip = WithKeys(NewClip(), AnimatedProperty.X,
            new Keyframe(S(0), 0, Easing.Linear), new Keyframe(S(4), 100, Easing.Linear));

        var cleared = ClipAnimation.ClearKeyframes(clip, AnimatedProperty.X, S(2));

        Assert.False(cleared.Keyframes.ContainsKey(AnimatedProperty.X));
        Assert.Equal(50, cleared.Transform.X, 9);
    }

    [Fact]
    public void Propriedade_desconhecida_lanca_ArgumentException()
    {
        var clip = NewClip();

        Assert.Throws<ArgumentException>(() => ClipAnimation.ValueAt(clip, "cor", S(0)));
    }

    // ---------------------------------------------------------------- SetEasing

    [Fact]
    public void SetEasing_altera_o_keyframe_anterior_mais_proximo_do_offset()
    {
        var clip = WithKeys(NewClip(), AnimatedProperty.X,
            new Keyframe(S(0), 0, Easing.EaseInOut), new Keyframe(S(4), 100, Easing.EaseInOut));

        var result = ClipAnimation.SetEasing(clip, AnimatedProperty.X, S(3), Easing.Linear);

        var keys = ClipAnimation.KeysOf(result, AnimatedProperty.X)!;
        Assert.Equal(Easing.Linear, keys[0].Easing);
        Assert.Equal(Easing.EaseInOut, keys[1].Easing);
    }

    [Fact]
    public void SetEasing_sem_keyframes_devolve_o_mesmo_clipe()
    {
        var clip = NewClip();

        var result = ClipAnimation.SetEasing(clip, AnimatedProperty.X, S(1), Easing.Hold);

        Assert.Same(clip, result);
    }

    // ---------------------------------------------------------------- TransformAt / OpacityAt

    [Fact]
    public void TransformAt_com_escala_animada_mantem_a_proporcao_X_Y_da_base()
    {
        var clip = WithKeys(NewClip(transform: new Transform { ScaleX = 1, ScaleY = 2 }), AnimatedProperty.Scale,
            new Keyframe(S(0), 1, Easing.Linear), new Keyframe(S(4), 3, Easing.Linear));

        var transform = ClipAnimation.TransformAt(clip, S(2));

        Assert.Equal(2, transform.ScaleX, 9);
        Assert.Equal(4, transform.ScaleY, 9);
    }

    [Fact]
    public void TransformAt_sem_keyframes_devolve_a_transformacao_do_clipe()
    {
        var clip = NewClip(transform: new Transform { X = 10, RotationDegrees = 45 });

        Assert.Equal(clip.Transform, ClipAnimation.TransformAt(clip, S(2)));
    }

    [Fact]
    public void OpacityAt_com_entrada_e_saida_gradual_comeca_e_termina_em_zero()
    {
        var clip = NewClip(duration: 4) with { FadeIn = S(1), FadeOut = S(1) };

        Assert.Equal(0, ClipAnimation.OpacityAt(clip, S(0)), 9);
        Assert.Equal(0.5, ClipAnimation.OpacityAt(clip, S(0.5)), 9);
        Assert.Equal(1, ClipAnimation.OpacityAt(clip, S(2)), 9);
        Assert.Equal(0, ClipAnimation.OpacityAt(clip, S(4)), 9);
    }

    [Fact]
    public void OpacityAt_usa_a_opacidade_animada()
    {
        var clip = WithKeys(NewClip(duration: 4), AnimatedProperty.Opacity,
            new Keyframe(S(0), 1, Easing.Linear), new Keyframe(S(4), 0, Easing.Linear));

        Assert.Equal(0.5, ClipAnimation.OpacityAt(clip, S(2)), 9);
    }

    // ---------------------------------------------------------------- Adjacent

    [Fact]
    public void Adjacent_encontra_o_keyframe_anterior_e_o_proximo_de_qualquer_propriedade()
    {
        var clip = NewClip(duration: 4);
        clip = WithKeys(clip, AnimatedProperty.X, new Keyframe(S(1), 0), new Keyframe(S(3), 0));
        clip = WithKeys(clip, AnimatedProperty.Y, new Keyframe(S(2), 0));

        Assert.Equal(S(1), ClipAnimation.Adjacent(clip, S(0), forward: true));
        Assert.Equal(S(2), ClipAnimation.Adjacent(clip, S(1), forward: true));
        Assert.Null(ClipAnimation.Adjacent(clip, S(3), forward: true));
        Assert.Equal(S(2), ClipAnimation.Adjacent(clip, S(2.5), forward: false));
        Assert.Null(ClipAnimation.Adjacent(clip, S(1), forward: false));
    }
}
