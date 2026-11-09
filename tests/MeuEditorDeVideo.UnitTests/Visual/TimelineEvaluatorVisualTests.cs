using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class TimelineEvaluatorVisualTests
{
    private const int Video1 = 0;

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset VideoAsset(double seconds = 10) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = "v.mp4",
        OriginalPath = @"C:\m\v.mp4",
        Duration = S(seconds),
        Width = 1920,
        Height = 1080,
        FrameRate = FrameRate.Fps30,
    };

    private static Clip NewClip(MediaAsset media, double start, double duration, double sourceIn = 0) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = media.Id,
        Start = S(start),
        Duration = S(duration),
        SourceIn = S(sourceIn),
    };

    private static Project Setup(MediaAsset media, params Clip[] clips)
    {
        var project = Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [media] };
        return WithTrack(project, t => t with { Clips = [.. clips] });
    }

    private static Project WithTrack(Project project, Func<Track, Track> change)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(Video1, change(seq.Tracks[Video1])) });
    }

    private static Project WithTransition(Project project, params Transition[] transitions) =>
        WithTrack(project, t => t with { Transitions = [.. transitions] });

    /// <summary>Dois clipes encostados em 4 s; a transição de 2 s fica centrada no corte: janela de 3 s a 5 s.</summary>
    private static (Project Project, Clip A, Clip B) Cut(MediaAsset media, TransitionKind kind, double? sourceInA = null)
    {
        var a = NewClip(media, 0, 4, sourceIn: sourceInA ?? 0);
        var b = NewClip(media, 4, 4);
        var transition = new Transition { Id = Guid.NewGuid(), FromClipId = a.Id, ToClipId = b.Id, Kind = kind, Duration = S(2) };
        var project = WithTransition(Setup(media, a, b), transition);
        return (project, a, b);
    }

    private static VisualLayer LayerOf(CompositionGraph graph, Guid clipId) =>
        graph.Layers.Single(l => l.ClipId == clipId);

    // ---------------------------------------------------------------- transições

    [Fact]
    public void Dissolve_no_corte_mostra_os_dois_clipes_com_o_de_entrada_meio_transparente()
    {
        var media = VideoAsset();
        var (project, a, b) = Cut(media, TransitionKind.Dissolve);

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(4));

        Assert.Equal(2, graph.Layers.Count);
        Assert.Equal(a.Id, graph.Layers[0].ClipId);
        Assert.Equal(1, graph.Layers[0].Opacity, 9);
        Assert.Equal(b.Id, graph.Layers[1].ClipId);
        Assert.Equal(0.5, graph.Layers[1].Opacity, 6);
    }

    [Fact]
    public void Dissolve_antes_da_janela_mostra_apenas_o_clipe_de_saida()
    {
        var media = VideoAsset();
        var (project, a, _) = Cut(media, TransitionKind.Dissolve);

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(2.5));

        var layer = Assert.Single(graph.Layers);
        Assert.Equal(a.Id, layer.ClipId);
        Assert.Equal(1, layer.Opacity, 9);
    }

    [Fact]
    public void Dissolve_no_quarto_da_janela_deixa_o_entrante_com_opacidade_um_quarto()
    {
        var media = VideoAsset();
        var (project, _, b) = Cut(media, TransitionKind.Dissolve);

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(3.5));

        Assert.Equal(0.25, LayerOf(graph, b.Id).Opacity, 6);
    }

    [Fact]
    public void SlideLeft_no_quarto_da_janela_desloca_o_entrante_pela_curva_de_easing()
    {
        var media = VideoAsset();
        var (project, _, b) = Cut(media, TransitionKind.SlideLeft);
        var width = project.Settings.Width;

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(3.5));

        // Progresso 0,25; easing InOut(0,25) = 0,0625 ⇒ deslocamento restante = largura × (1 − 0,0625).
        Assert.Equal(width * 0.9375, LayerOf(graph, b.Id).Transform.X, 6);
    }

    [Fact]
    public void WipeRight_no_quarto_da_janela_da_ao_entrante_a_mascara_com_tipo_e_progresso()
    {
        var media = VideoAsset();
        var (project, a, b) = Cut(media, TransitionKind.WipeRight);

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(3.5));

        Assert.Equal(new TransitionMask(TransitionKind.WipeRight, 0.25), LayerOf(graph, b.Id).Mask);
        Assert.Null(LayerOf(graph, a.Id).Mask);
    }

    [Fact]
    public void Clipe_de_saida_alem_do_fim_usa_quadros_seguintes_da_origem()
    {
        // Saída entra em 2 s e dura 4 s (SourceOut = 6 s). No instante 4,5 s, a transição ainda mostra 6,5 s de origem.
        var media = VideoAsset();
        var (project, a, _) = Cut(media, TransitionKind.Dissolve, sourceInA: 2);

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(4.5));

        Assert.True(LayerOf(graph, a.Id).SourceTime > a.SourceOut);
        Assert.Equal(S(6.5), LayerOf(graph, a.Id).SourceTime);
    }

    [Fact]
    public void Clipe_de_saida_alem_do_fim_da_midia_e_limitado_a_duracao_da_midia()
    {
        var media = VideoAsset(seconds: 10);
        var (project, a, _) = Cut(media, TransitionKind.Dissolve, sourceInA: 8);

        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(4.5));

        // Origem pedida: 12,5 s; a mídia tem 10 s, então fica no último quadro.
        Assert.Equal(media.Duration - new MediaTime(1), LayerOf(graph, a.Id).SourceTime);
    }

    // ---------------------------------------------------------------- congelamento, câmera lenta e animação

    [Fact]
    public void FreezeAt_mantem_o_instante_de_origem_constante_durante_o_clipe()
    {
        var media = VideoAsset();
        var clip = NewClip(media, 0, 4, sourceIn: 5) with { FreezeAt = S(2) };
        var project = Setup(media, clip);

        var early = TimelineEvaluator.Evaluate(project, project.MainSequence, S(0.5));
        var late = TimelineEvaluator.Evaluate(project, project.MainSequence, S(3.5));

        Assert.Equal(S(2), Assert.Single(early.Layers).SourceTime);
        Assert.Equal(S(2), Assert.Single(late.Layers).SourceTime);
        Assert.Null(Assert.Single(late.Layers).BlendSourceTime);
    }

    [Fact]
    public void FrameBlending_em_camera_lenta_mistura_o_proximo_quadro_com_peso_no_meio_do_quadro()
    {
        // Velocidade 1/2 num vídeo de 30 fps: t = 1,0333… s ⇒ origem = 0,5 s + meio quadro.
        var media = VideoAsset();
        var clip = NewClip(media, 0, 4) with { Speed = new Rational(1, 2), FrameBlending = true };
        var project = Setup(media, clip);
        var fr = FrameRate.Fps30;
        var t = new MediaTime(729_120_000);

        var layer = Assert.Single(TimelineEvaluator.Evaluate(project, project.MainSequence, t).Layers);

        Assert.Equal(fr.FrameToTime(15), layer.SourceTime);
        Assert.Equal(fr.FrameToTime(16), layer.BlendSourceTime);
        Assert.InRange(layer.BlendWeight, 0.01, 0.99);
    }

    [Fact]
    public void FrameBlending_em_velocidade_normal_nao_mistura_quadros()
    {
        var media = VideoAsset();
        var clip = NewClip(media, 0, 4) with { FrameBlending = true };
        var project = Setup(media, clip);

        var layer = Assert.Single(TimelineEvaluator.Evaluate(project, project.MainSequence, S(1.0333)).Layers);

        Assert.Null(layer.BlendSourceTime);
        Assert.Equal(0, layer.BlendWeight);
    }

    [Fact]
    public void Keyframe_de_posicao_X_aparece_na_transformacao_da_camada()
    {
        var media = VideoAsset();
        var clip = NewClip(media, 0, 4) with
        {
            Keyframes = ImmutableDictionary<string, ImmutableList<Keyframe>>.Empty.Add(
                AnimatedProperty.X,
                ImmutableList.Create(new Keyframe(S(0), 0, Easing.Linear), new Keyframe(S(4), 100, Easing.Linear))),
        };
        var project = Setup(media, clip);

        var layer = Assert.Single(TimelineEvaluator.Evaluate(project, project.MainSequence, S(2)).Layers);

        Assert.Equal(50, layer.Transform.X, 6);
    }

    [Fact]
    public void FadeIn_reduz_a_opacidade_da_camada_no_inicio()
    {
        var media = VideoAsset();
        var clip = NewClip(media, 0, 4) with { FadeIn = S(1) };
        var project = Setup(media, clip);

        var layer = Assert.Single(TimelineEvaluator.Evaluate(project, project.MainSequence, S(0.5)).Layers);

        Assert.Equal(0.5, layer.Opacity, 6);
    }

    [Fact]
    public void Efeitos_desativados_ou_neutros_ficam_fora_da_camada()
    {
        var media = VideoAsset();
        var invert = EffectCatalog.Create(EffectCatalog.Invert);
        var disabled = EffectCatalog.Create(EffectCatalog.Grayscale) with { Enabled = false };
        var neutralBlur = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Blur), "radius", 0);
        var clip = NewClip(media, 0, 4) with { Effects = [invert, disabled, neutralBlur] };
        var project = Setup(media, clip);

        var layer = Assert.Single(TimelineEvaluator.Evaluate(project, project.MainSequence, S(1)).Layers);

        var effect = Assert.Single(layer.Effects);
        Assert.Equal(invert.Id, effect.Id);
    }

    [Fact]
    public void Modo_de_mistura_do_clipe_chega_a_camada()
    {
        var media = VideoAsset();
        var clip = NewClip(media, 0, 4) with { BlendMode = BlendMode.Multiply };
        var project = Setup(media, clip);

        var layer = Assert.Single(TimelineEvaluator.Evaluate(project, project.MainSequence, S(1)).Layers);

        Assert.Equal(BlendMode.Multiply, layer.BlendMode);
    }
}
