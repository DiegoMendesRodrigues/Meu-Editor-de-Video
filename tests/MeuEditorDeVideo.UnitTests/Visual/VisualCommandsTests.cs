using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class VisualCommandsTests
{
    // Índices das trilhas criadas por Project.CreateNew: Vídeo 1, Vídeo 2, Áudio 1, Texto 1.
    private const int Video1 = 0;
    private const int Audio1 = 2;

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset VideoAsset(string name = "v.mp4") => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = name,
        OriginalPath = @"C:\m\" + name,
        Duration = S(10),
        Width = 1920,
        Height = 1080,
        FrameRate = FrameRate.Fps30,
        Info = new MediaTechnicalInfo { AudioCodec = "aac" },
    };

    private static MediaAsset ImageAsset() => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Image,
        Name = "foto.png",
        OriginalPath = @"C:\m\foto.png",
        Width = 1920,
        Height = 1080,
    };

    private static Clip NewClip(Guid mediaId, double start, double duration, double sourceIn = 0, Guid? link = null) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = mediaId,
        Start = S(start),
        Duration = S(duration),
        SourceIn = S(sourceIn),
        LinkId = link,
    };

    private static Project NewProject(params MediaAsset[] media) =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [.. media] };

    private static Project WithTrack(Project project, int index, Func<Track, Track> change)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(index, change(seq.Tracks[index])) });
    }

    private static Project PlaceClips(Project project, int index, params Clip[] clips) =>
        WithTrack(project, index, t => t with { Clips = [.. clips] });

    private static Track TrackOf(Project project, int index) => project.MainSequence.Tracks[index];

    private static List<Clip> ClipsOf(Project project, int index) => TrackOf(project, index).Clips.OrderBy(c => c.Start.Ticks).ToList();

    // ---------------------------------------------------------------- UpdateClipCommand

    [Fact]
    public void UpdateClip_aplica_a_alteracao_no_clipe()
    {
        var video = VideoAsset();
        var clip = NewClip(video.Id, 0, 4);
        var p = PlaceClips(NewProject(video), Video1, clip);
        var cmd = new UpdateClipCommand(p.MainSequence.Id, clip.Id, c => c with { Opacity = 0.4 }, "Opacidade");

        var result = cmd.Apply(p);

        Assert.Equal(0.4, Assert.Single(ClipsOf(result, Video1)).Opacity);
    }

    [Fact]
    public void UpdateClip_nao_altera_inicio_nem_duracao_do_clipe()
    {
        var video = VideoAsset();
        var clip = NewClip(video.Id, 1, 4);
        var p = PlaceClips(NewProject(video), Video1, clip);
        var cmd = new UpdateClipCommand(p.MainSequence.Id, clip.Id, c => c with { Start = S(9), Duration = S(1), Opacity = 0.5 }, "Mover");

        var updated = Assert.Single(ClipsOf(cmd.Apply(p), Video1));

        Assert.Equal(S(1), updated.Start);
        Assert.Equal(S(4), updated.Duration);
        Assert.Equal(0.5, updated.Opacity);
    }

    [Fact]
    public void UpdateClip_em_trilha_bloqueada_lanca_EditRejectedException()
    {
        var video = VideoAsset();
        var clip = NewClip(video.Id, 0, 4);
        var p = WithTrack(PlaceClips(NewProject(video), Video1, clip), Video1, t => t with { IsLocked = true });
        var cmd = new UpdateClipCommand(p.MainSequence.Id, clip.Id, c => c with { Opacity = 0.2 }, "Opacidade");

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void UpdateClip_sem_mudanca_devolve_o_mesmo_projeto()
    {
        var video = VideoAsset();
        var clip = NewClip(video.Id, 0, 4);
        var p = PlaceClips(NewProject(video), Video1, clip);
        var cmd = new UpdateClipCommand(p.MainSequence.Id, clip.Id, c => c, "Nada");

        Assert.Same(p, cmd.Apply(p));
    }

    [Fact]
    public void UpdateClip_expoe_a_chave_de_mesclagem_para_ajustes_continuos()
    {
        var merged = new UpdateClipCommand(Guid.NewGuid(), Guid.NewGuid(), c => c, "Ajuste", MergeKey: "opacidade:1");
        var single = new UpdateClipCommand(Guid.NewGuid(), Guid.NewGuid(), c => c, "Ajuste");

        Assert.Equal("opacidade:1", merged.MergeKey);
        Assert.Null(single.MergeKey);
    }

    // ---------------------------------------------------------------- FreezeFrameCommand

    [Fact]
    public void FreezeFrame_divide_o_video_insere_o_quadro_fixo_e_desloca_o_restante_das_trilhas()
    {
        // Vídeo 4 s com áudio ligado; um clipe de vídeo e um de áudio depois do corte.
        var video = VideoAsset();
        var link = Guid.NewGuid();
        var v = NewClip(video.Id, 0, 4, sourceIn: 1, link: link);
        var a = NewClip(video.Id, 0, 4, sourceIn: 1, link: link);
        var vAfter = NewClip(video.Id, 4, 2);
        var aAfter = NewClip(video.Id, 4, 2);
        var p = PlaceClips(PlaceClips(NewProject(video), Video1, v, vAfter), Audio1, a, aAfter);
        var seq = p.MainSequence;
        var cmd = new FreezeFrameCommand(seq.Id, v.Id, S(1), S(2));

        var result = cmd.Apply(p);

        var video1 = ClipsOf(result, Video1);
        Assert.Equal(4, video1.Count);
        Assert.Equal(v.Id, video1[0].Id);
        Assert.Equal(S(1), video1[0].Duration);
        Assert.Equal(S(1), video1[1].Start);
        Assert.Equal(S(3), video1[2].Start);
        Assert.Equal(S(3), video1[2].Duration);
        Assert.Equal(S(2), video1[2].SourceIn);
        Assert.Equal(S(6), video1[3].Start);
        Assert.Equal(vAfter.Id, video1[3].Id);

        var audio = ClipsOf(result, Audio1);
        Assert.Equal(3, audio.Count);
        Assert.Equal(S(3), audio[1].Start);
        Assert.Equal(S(2), audio[1].SourceIn);
        Assert.Equal(S(6), audio[2].Start);
    }

    [Fact]
    public void FreezeFrame_insere_clipe_congelado_com_o_instante_de_origem_do_corte()
    {
        var video = VideoAsset();
        var clip = NewClip(video.Id, 0, 4, sourceIn: 1);
        var p = PlaceClips(NewProject(video), Video1, clip);
        var cmd = new FreezeFrameCommand(p.MainSequence.Id, clip.Id, S(1), S(2));

        var result = cmd.Apply(p);

        var frozen = ClipsOf(result, Video1).Single(c => c.Id == cmd.FreezeClipId);
        Assert.Equal(S(1), frozen.Start);
        Assert.Equal(S(2), frozen.Duration);
        // Origem = SourceIn (1 s) + tempo decorrido no clipe (1 s) = 2 s.
        Assert.Equal(S(2), frozen.FreezeAt);
        Assert.Null(frozen.LinkId);
    }

    [Fact]
    public void FreezeFrame_em_imagem_lanca_EditRejectedException()
    {
        var image = ImageAsset();
        var clip = NewClip(image.Id, 0, 2);
        var p = PlaceClips(NewProject(image), Video1, clip);
        var cmd = new FreezeFrameCommand(p.MainSequence.Id, clip.Id, S(1), S(1));

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void FreezeFrame_em_clipe_de_audio_lanca_EditRejectedException()
    {
        var video = VideoAsset();
        var audioClip = NewClip(video.Id, 0, 4);
        var p = PlaceClips(NewProject(video), Audio1, audioClip);
        var cmd = new FreezeFrameCommand(p.MainSequence.Id, audioClip.Id, S(1), S(1));

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    // ---------------------------------------------------------------- RepeatClipCommand

    [Fact]
    public void RepeatClip_cria_copias_logo_apos_o_clipe_e_desloca_o_restante()
    {
        var video = VideoAsset();
        var link = Guid.NewGuid();
        var v = NewClip(video.Id, 0, 2, link: link);
        var a = NewClip(video.Id, 0, 2, link: link);
        var after = NewClip(video.Id, 2, 1);
        var p = PlaceClips(PlaceClips(NewProject(video), Video1, v, after), Audio1, a);
        var cmd = new RepeatClipCommand(p.MainSequence.Id, v.Id, 2);

        var result = cmd.Apply(p);

        var video1 = ClipsOf(result, Video1);
        Assert.Equal(new[] { S(0), S(2), S(4), S(6) }, video1.Select(c => c.Start));
        Assert.Equal(after.Id, video1[3].Id);
        Assert.Equal(new[] { S(0), S(2), S(4) }, ClipsOf(result, Audio1).Select(c => c.Start));
    }

    [Fact]
    public void RepeatClip_copias_recebem_novos_ids_e_vinculo_compartilhado_por_repeticao()
    {
        var video = VideoAsset();
        var link = Guid.NewGuid();
        var v = NewClip(video.Id, 0, 2, link: link);
        var a = NewClip(video.Id, 0, 2, link: link);
        var p = PlaceClips(PlaceClips(NewProject(video), Video1, v), Audio1, a);

        var result = new RepeatClipCommand(p.MainSequence.Id, v.Id, 1).Apply(p);

        var copyV = ClipsOf(result, Video1)[1];
        var copyA = ClipsOf(result, Audio1)[1];
        Assert.NotEqual(v.Id, copyV.Id);
        Assert.NotNull(copyV.LinkId);
        Assert.NotEqual(link, copyV.LinkId);
        Assert.Equal(copyV.LinkId, copyA.LinkId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void RepeatClip_com_numero_fora_do_intervalo_lanca_EditRejectedException(int count)
    {
        var video = VideoAsset();
        var v = NewClip(video.Id, 0, 2);
        var p = PlaceClips(NewProject(video), Video1, v);
        var cmd = new RepeatClipCommand(p.MainSequence.Id, v.Id, count);

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    // ---------------------------------------------------------------- SetTransitionCommand

    private static (Project Project, Clip A, Clip B) TwoAdjacentClips()
    {
        var video = VideoAsset();
        var a = NewClip(video.Id, 0, 2);
        var b = NewClip(video.Id, 2, 2);
        return (PlaceClips(NewProject(video), Video1, a, b), a, b);
    }

    [Fact]
    public void SetTransition_exige_clipes_encostados_na_mesma_trilha()
    {
        var video = VideoAsset();
        var a = NewClip(video.Id, 0, 2);
        var b = NewClip(video.Id, 2.5, 2);
        var p = PlaceClips(NewProject(video), Video1, a, b);
        var cmd = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, S(1));

        Assert.Throws<EditRejectedException>(() => cmd.Apply(p));
    }

    [Fact]
    public void SetTransition_limita_a_duracao_ao_clipe_mais_curto()
    {
        var (p, a, b) = TwoAdjacentClips();
        var cmd = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, S(10));

        var transition = Assert.Single(TrackOf(cmd.Apply(p), Video1).Transitions);

        Assert.Equal(S(2), transition.Duration);
        Assert.Equal(TransitionKind.Dissolve, transition.Kind);
        Assert.Equal(a.Id, transition.FromClipId);
        Assert.Equal(b.Id, transition.ToClipId);
    }

    [Fact]
    public void SetTransition_duracao_minima_e_dois_quadros()
    {
        var (p, a, b) = TwoAdjacentClips();
        var cmd = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, MediaTime.Zero);

        var transition = Assert.Single(TrackOf(cmd.Apply(p), Video1).Transitions);

        Assert.Equal(new MediaTime(FrameRate.Fps30.FrameDuration.Ticks * 2), transition.Duration);
    }

    [Fact]
    public void SetTransition_trocar_o_tipo_mantem_o_mesmo_Id()
    {
        var (p, a, b) = TwoAdjacentClips();
        var first = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, S(1)).Apply(p);
        var originalId = Assert.Single(TrackOf(first, Video1).Transitions).Id;

        var replaced = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.SlideLeft, S(1)).Apply(first);

        var transition = Assert.Single(TrackOf(replaced, Video1).Transitions);
        Assert.Equal(originalId, transition.Id);
        Assert.Equal(TransitionKind.SlideLeft, transition.Kind);
    }

    [Fact]
    public void SetTransition_com_tipo_nulo_remove_a_transicao()
    {
        var (p, a, b) = TwoAdjacentClips();
        var applied = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, S(1)).Apply(p);

        var removed = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, null, MediaTime.Zero).Apply(applied);

        Assert.Empty(TrackOf(removed, Video1).Transitions);
    }

    [Fact]
    public void Dividir_o_clipe_de_origem_religa_a_transicao_ao_novo_pedaco_direito()
    {
        var (p, a, b) = TwoAdjacentClips();
        var withTransition = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, S(1)).Apply(p);

        var split = new SplitClipsCommand(withTransition.MainSequence.Id, S(1)).Apply(withTransition);

        var clips = ClipsOf(split, Video1);
        var rightPiece = clips.Single(c => c.Start == S(1));
        var transition = Assert.Single(TrackOf(split, Video1).Transitions);
        Assert.Equal(rightPiece.Id, transition.FromClipId);
        Assert.Equal(b.Id, transition.ToClipId);
        Assert.Equal(S(1), transition.Duration);
    }

    [Fact]
    public void Remover_o_clipe_de_destino_descarta_a_transicao()
    {
        var (p, a, b) = TwoAdjacentClips();
        var withTransition = new SetTransitionCommand(p.MainSequence.Id, a.Id, b.Id, TransitionKind.Dissolve, S(1)).Apply(p);

        var removed = new RemoveClipsCommand(withTransition.MainSequence.Id, [b.Id]).Apply(withTransition);

        Assert.Single(ClipsOf(removed, Video1));
        Assert.Empty(TrackOf(removed, Video1).Transitions);
    }
}
