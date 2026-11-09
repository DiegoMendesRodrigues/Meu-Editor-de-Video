using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class AnimationPresetsTests
{
    private static readonly ProjectSettings Settings = new();

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static Clip NewClip(double duration = 4, Transform? transform = null, double opacity = 1) => new()
    {
        Id = Guid.NewGuid(),
        Start = MediaTime.Zero,
        Duration = S(duration),
        Transform = transform ?? Transform.Identity,
        Opacity = opacity,
    };

    private static Clip Apply(Clip clip, string id) => AnimationPresets.Apply(clip, id, Settings);

    [Fact]
    public void All_tem_11_predefinicoes_com_ids_unicos_e_nomes_preenchidos()
    {
        Assert.Equal(11, AnimationPresets.All.Count);
        Assert.Equal(11, AnimationPresets.All.Select(p => p.Id).Distinct().Count());
        Assert.All(AnimationPresets.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
        });
    }

    [Fact]
    public void Find_retorna_a_predefinicao_conhecida_ou_nulo()
    {
        Assert.Equal("slideLeft", AnimationPresets.Find("slideLeft")?.Id);
        Assert.Null(AnimationPresets.Find("inexistente"));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    [InlineData(0.3)]
    [InlineData(7.5)]
    public void Toda_predefinicao_gera_offsets_dentro_do_clipe_ordenados_unicos_e_alinhados_ao_quadro(double duration)
    {
        var clip = NewClip(duration, new Transform { X = 10, Y = 20, ScaleX = 1, ScaleY = 1, RotationDegrees = 15 }, 0.8);
        var rate = Settings.FrameRate;

        foreach (var preset in AnimationPresets.All)
        {
            var result = Apply(clip, preset.Id);
            foreach (var kv in result.Keyframes)
            {
                var keys = kv.Value;
                Assert.All(keys, k =>
                {
                    Assert.True(k.Offset >= MediaTime.Zero && k.Offset <= clip.Duration, $"{preset.Id}/{kv.Key}: offset {k.Offset} fora do clipe");
                    Assert.Equal(k.Offset, rate.FrameToTime(rate.TimeToFrame(k.Offset)));
                });
                for (var i = 1; i < keys.Count; i++)
                    Assert.True(keys[i - 1].Offset < keys[i].Offset, $"{preset.Id}/{kv.Key}: offsets fora de ordem ou repetidos");
            }
        }
    }

    [Fact]
    public void slideLeft_vai_de_baseX_menos_largura_ate_baseX()
    {
        var clip = NewClip(4, new Transform { X = 100 });

        var result = Apply(clip, "slideLeft");

        Assert.Equal(100 - Settings.Width, ClipAnimation.ValueAt(result, AnimatedProperty.X, MediaTime.Zero), 6);
        Assert.Equal(100, ClipAnimation.ValueAt(result, AnimatedProperty.X, clip.Duration), 6);
    }

    [Fact]
    public void Aplicar_zoomIn_e_depois_slideLeft_mantem_as_chaves_de_escala_e_opacidade()
    {
        var clip = NewClip(4);
        var zoomed = Apply(clip, "zoomIn");

        var both = Apply(zoomed, "slideLeft");

        Assert.True(zoomed.Keyframes[AnimatedProperty.Scale].SequenceEqual(both.Keyframes[AnimatedProperty.Scale]));
        Assert.True(zoomed.Keyframes[AnimatedProperty.Opacity].SequenceEqual(both.Keyframes[AnimatedProperty.Opacity]));
        Assert.True(both.Keyframes.ContainsKey(AnimatedProperty.X));
    }

    [Fact]
    public void none_remove_as_chaves_visuais_e_mantem_o_volume()
    {
        var clip = Apply(NewClip(4), "spin");
        clip = clip with
        {
            Keyframes = clip.Keyframes.SetItem(
                AnimatedProperty.Volume,
                ImmutableList.Create(new Keyframe(S(0), 0.5), new Keyframe(S(1), 1))),
        };

        var result = Apply(clip, "none");

        foreach (var property in AnimatedProperty.All)
            Assert.False(result.Keyframes.ContainsKey(property), property);
        Assert.Equal(2, result.Keyframes[AnimatedProperty.Volume].Count);
    }

    [Fact]
    public void kenBurns_termina_com_escala_115_por_cento_da_base()
    {
        var clip = NewClip(4, new Transform { ScaleX = 2, ScaleY = 2 });

        var result = Apply(clip, "kenBurns");

        Assert.Equal(2.3, ClipAnimation.ValueAt(result, AnimatedProperty.Scale, clip.Duration), 6);
    }

    [Fact]
    public void spin_termina_em_base_mais_360()
    {
        var clip = NewClip(4, new Transform { RotationDegrees = 30 });

        var result = Apply(clip, "spin");

        Assert.Equal(390, ClipAnimation.ValueAt(result, AnimatedProperty.Rotation, clip.Duration), 6);
    }

    [Fact]
    public void pulse_tem_pico_de_108_por_cento_em_meio_segundo()
    {
        var clip = NewClip(4, new Transform { ScaleX = 1, ScaleY = 1 });

        var result = Apply(clip, "pulse");

        Assert.Equal(1.08, ClipAnimation.ValueAt(result, AnimatedProperty.Scale, S(0.5)), 6);
    }

    [Fact]
    public void bounce_comeca_acima_do_chao_e_termina_em_baseY()
    {
        var clip = NewClip(4, new Transform { Y = 50 });

        var result = Apply(clip, "bounce");

        Assert.Equal(50 - 0.5 * Settings.Height, ClipAnimation.ValueAt(result, AnimatedProperty.Y, MediaTime.Zero), 6);
        Assert.Equal(50, ClipAnimation.ValueAt(result, AnimatedProperty.Y, clip.Duration), 6);
    }

    [Fact]
    public void Id_desconhecido_lanca_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Apply(NewClip(), "inexistente"));
    }

    [Fact]
    public void Clipe_com_menos_de_dois_quadros_fica_inalterado_exceto_none()
    {
        var clip = NewClip(4) with { Duration = Settings.FrameRate.FrameToTime(1) };

        foreach (var preset in AnimationPresets.All.Where(p => p.Id != "none"))
            Assert.Same(clip, Apply(clip, preset.Id));

        var withKeys = Apply(NewClip(4), "spin") with { Duration = Settings.FrameRate.FrameToTime(1) };
        Assert.False(Apply(withKeys, "none").Keyframes.ContainsKey(AnimatedProperty.Rotation));
    }
}
