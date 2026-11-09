using System.Collections.Immutable;
using System.Text.Json;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Composition;
using MeuEditorDeVideo.ProjectEngine.Format;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Visual;

/// <summary>Composição avançada (F9): máscaras, rastreamento, vínculo, estabilização, 2,5D e elementos gráficos.</summary>
public sealed class CompositionTests
{
    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset Video() => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = "v.mp4",
        OriginalPath = @"C:\m\v.mp4",
        Duration = S(10),
        Width = 1920,
        Height = 1080,
        FrameRate = FrameRate.Fps30,
    };

    private static Clip MediaClip(MediaAsset a, double start = 0, double duration = 10) =>
        new() { Id = Guid.NewGuid(), MediaAssetId = a.Id, Start = S(start), Duration = S(duration) };

    private static Project Setup(MediaAsset media, params (int Track, Clip Clip)[] clips)
    {
        var project = Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [media] };
        var seq = project.MainSequence;
        foreach (var (track, clip) in clips)
            seq = seq with { Tracks = seq.Tracks.SetItem(track, seq.Tracks[track] with { Clips = seq.Tracks[track].Clips.Add(clip) }) };
        return project.WithSequence(seq);
    }

    private static TrackSample Sample(double t, double x, double y, double w = 0.1, double h = 0.1) => new(S(t), x, y, w, h);

    // ---------------------------------------------------------------- máscaras (domínio)

    [Fact]
    public void Mascara_animada_interpola_as_formas_chave()
    {
        var mask = new Mask
        {
            Id = Guid.NewGuid(),
            Keys =
            [
                new MaskShapeKey(S(0), [new(0, 0), new(0.2, 0.2)], Easing.Linear),
                new MaskShapeKey(S(2), [new(0.5, 0.5), new(0.7, 0.7)], Easing.Linear),
            ],
        };
        var mid = CompositionMath.ShapeAt(mask, S(1));
        Assert.Equal(0.25, mid[0].X, 6);
        Assert.Equal(0.45, mid[1].Y, 6);
        Assert.Equal(0.5, CompositionMath.ShapeAt(mask, S(5))[0].X, 6);
    }

    [Fact]
    public void Forma_chave_liga_e_desliga_preservando_a_forma()
    {
        var mask = new Mask { Id = Guid.NewGuid(), Points = CompositionMath.DefaultShape(MaskKind.Rectangle) };
        var keyed = CompositionMath.ToggleShapeKey(mask, S(1));
        Assert.Single(keyed.Keys);
        var moved = CompositionMath.SetShape(keyed, S(3), [new(0.1, 0.1), new(0.3, 0.3)]);
        Assert.Equal(2, moved.Keys.Count);
        Assert.True(moved.Keys[0].Offset < moved.Keys[1].Offset);
        var back = CompositionMath.ToggleShapeKey(CompositionMath.ToggleShapeKey(moved, S(3)), S(1));
        Assert.Empty(back.Keys);
        Assert.Equal(mask.Points, back.Points);
    }

    [Fact]
    public void Mascara_rastreada_segue_o_objeto()
    {
        var trackerId = Guid.NewGuid();
        var tracker = new MotionTracker
        {
            Id = trackerId,
            Region = Sample(0, 0.5, 0.5),
            Samples = [Sample(0, 0.5, 0.5), Sample(1, 0.7, 0.4)],
        };
        var mask = new Mask { Id = Guid.NewGuid(), Points = [new(0.45, 0.45), new(0.55, 0.55)], TrackerId = trackerId, TrackerReference = S(0) };
        var clip = new Clip { Id = Guid.NewGuid(), Start = S(0), Duration = S(2), Trackers = [tracker], Masks = [mask] };
        var resolved = CompositionMath.Resolve(mask, clip, S(0.5), S(0.5));
        Assert.Equal(0.55, resolved.Points[0].X, 6); // centro andou 0,1 em X
        Assert.Equal(0.40, resolved.Points[0].Y, 6); // e −0,05 em Y
        Assert.Empty(resolved.Keys);
    }

    [Fact]
    public void Contornos_de_retangulo_elipse_e_livre_suave()
    {
        var rect = new Mask { Id = Guid.NewGuid(), Kind = MaskKind.Rectangle, Points = [new(0.8, 0.9), new(0.2, 0.1)] };
        var o = CompositionMath.Outline(rect);
        Assert.Equal(4, o.Count);
        Assert.Equal(new MaskPoint(0.2, 0.1), o[0]);

        var ellipse = rect with { Kind = MaskKind.Ellipse };
        Assert.True(CompositionMath.Outline(ellipse).Count >= 32);

        var path = new Mask { Id = Guid.NewGuid(), Kind = MaskKind.Path, Smooth = true, Points = CompositionMath.DefaultShape(MaskKind.Path) };
        var smooth = CompositionMath.Outline(path);
        Assert.True(smooth.Count > path.Points.Count);
        foreach (var p in path.Points) Assert.Contains(smooth, q => Math.Abs(q.X - p.X) < 1e-9 && Math.Abs(q.Y - p.Y) < 1e-9);
    }

    [Fact]
    public void Remover_rastreamento_solta_as_mascaras_e_mover_reordena()
    {
        var clip = new Clip { Id = Guid.NewGuid(), Start = S(0), Duration = S(2) };
        var tid = Guid.NewGuid();
        clip = CompositionOps.AddTracker(clip, Sample(0, 0.5, 0.5), tid);
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        clip = CompositionOps.AddMask(CompositionOps.AddMask(clip, MaskKind.Rectangle, m1), MaskKind.Ellipse, m2);
        clip = CompositionOps.FollowTracker(clip, m1, tid, S(0));
        Assert.Equal(tid, clip.Masks[0].TrackerId);

        clip = CompositionOps.RemoveTracker(clip, tid);
        Assert.Empty(clip.Trackers);
        Assert.Null(clip.Masks[0].TrackerId);

        clip = CompositionOps.MoveMask(clip, m2, -1);
        Assert.Equal(m2, clip.Masks[0].Id);
        Assert.Equal("Máscara 2", clip.Masks[0].Name);
    }

    // ---------------------------------------------------------------- máscaras (rasterização)

    private static byte Alpha(byte[] cov, int w, int x, int y) => cov[y * w + x];

    [Fact]
    public void Mascara_retangular_recorta_a_imagem()
    {
        var mask = new Mask { Id = Guid.NewGuid(), Points = [new(0.25, 0.25), new(0.75, 0.75)] };
        var cov = MaskRenderer.Coverage(100, 100, [mask])!;
        Assert.Equal(255, Alpha(cov, 100, 50, 50));
        Assert.Equal(0, Alpha(cov, 100, 5, 5));
        Assert.Equal(0, Alpha(cov, 100, 90, 50));

        var inverted = MaskRenderer.Coverage(100, 100, [mask with { Invert = true }])!;
        Assert.Equal(0, Alpha(inverted, 100, 50, 50));
        Assert.Equal(255, Alpha(inverted, 100, 5, 5));
    }

    [Fact]
    public void Mascaras_combinam_por_soma_subtracao_e_intersecao()
    {
        var left = new Mask { Id = Guid.NewGuid(), Points = [new(0, 0), new(0.6, 1)] };
        var right = new Mask { Id = Guid.NewGuid(), Points = [new(0.4, 0), new(1, 1)] };
        var add = MaskRenderer.Coverage(100, 10, [left, right])!;
        Assert.Equal(255, Alpha(add, 100, 10, 5));
        Assert.Equal(255, Alpha(add, 100, 90, 5));

        var sub = MaskRenderer.Coverage(100, 10, [left, right with { Mode = MaskMode.Subtract }])!;
        Assert.Equal(255, Alpha(sub, 100, 10, 5));
        Assert.Equal(0, Alpha(sub, 100, 50, 5));

        var inter = MaskRenderer.Coverage(100, 10, [left, right with { Mode = MaskMode.Intersect }])!;
        Assert.Equal(0, Alpha(inter, 100, 10, 5));
        Assert.Equal(255, Alpha(inter, 100, 50, 5));
    }

    [Fact]
    public void Suavizar_a_borda_cria_transicao_e_aplicar_preserva_a_entrada()
    {
        var mask = new Mask { Id = Guid.NewGuid(), Points = [new(0.25, 0.25), new(0.75, 0.75)], Feather = 0.05 };
        var cov = MaskRenderer.Coverage(100, 100, [mask])!;
        var row = Enumerable.Range(0, 100).Select(x => Alpha(cov, 100, x, 50)).ToList();
        Assert.Contains(row, a => a is > 30 and < 220);

        var px = Enumerable.Repeat((byte)255, 100 * 100 * 4).ToArray();
        var img = new BgraImage(100, 100, px);
        var masked = MaskRenderer.Apply(img, [mask with { Feather = 0 }]);
        Assert.Equal(255, img.Pixels[3]);
        Assert.Equal(0, masked.Pixels[3]);
        Assert.Equal(255, masked.Pixels[(50 * 100 + 50) * 4 + 3]);
    }

    // ---------------------------------------------------------------- estabilização

    [Fact]
    public void Estabilizacao_compensa_tremores_e_amplia()
    {
        // Câmera tremendo ±0,01 da largura a cada quadro, sem deriva.
        var steps = Enumerable.Range(0, 90).Select(i => new MotionStep(FrameRate.Fps30.FrameToTime(i), i == 0 ? 0 : (i % 2 == 0 ? -0.02 : 0.02), 0, 0)).ToImmutableList();
        var stab = new Stabilization { Steps = steps, Smoothness = 0.5 };
        double Position(int i) => steps.Take(i + 1).Sum(s => s.Dx);
        var spreadBefore = Enumerable.Range(20, 40).Select(Position).Max() - Enumerable.Range(20, 40).Select(Position).Min();
        var corrected = Enumerable.Range(20, 40).Select(i => Position(i) + CompositionMath.Correction(stab, steps[i].SourceTime, 16.0 / 9).Dx).ToList();
        Assert.True(corrected.Max() - corrected.Min() < spreadBefore / 4);

        var zoom = CompositionMath.Correction(stab, steps[10].SourceTime, 16.0 / 9).Zoom;
        Assert.InRange(zoom, 1.001, CompositionMath.MaxAutoZoom);
        Assert.Equal(StabilizationCorrection.None, CompositionMath.Correction(stab with { Enabled = false }, steps[10].SourceTime, 1.7));
        Assert.Equal(1.2, CompositionMath.Correction(stab with { Zoom = 0.2 }, steps[10].SourceTime, 1.7).Zoom, 6);
    }

    [Fact]
    public void Panoramica_continua_nao_e_tratada_como_tremor()
    {
        var steps = Enumerable.Range(0, 60).Select(i => new MotionStep(FrameRate.Fps30.FrameToTime(i), i == 0 ? 0 : 0.01, 0, 0)).ToImmutableList();
        var stab = new Stabilization { Steps = steps, Smoothness = 1 };
        foreach (var s in steps)
            Assert.Equal(0, CompositionMath.Correction(stab, s.SourceTime, 16.0 / 9).Dx, 9);
        Assert.Equal(1, CompositionMath.Correction(stab, steps[0].SourceTime, 16.0 / 9).Zoom, 6);
    }

    [Fact]
    public void Correcao_desloca_e_gira_a_camada_pela_transformacao_do_clipe()
    {
        var c = new StabilizationCorrection(0.01, 0, Math.PI / 180, 1.1);
        var t = CompositionMath.Apply(Transform.Identity, c, 1920);
        Assert.Equal(0.01 * 1920 * 1.1, t.X, 6);
        Assert.Equal(1.1, t.ScaleX, 6);
        Assert.Equal(1, t.RotationDegrees, 6);

        var rotated = CompositionMath.Apply(Transform.Identity with { RotationDegrees = 90 }, c with { Da = 0 }, 1920);
        Assert.Equal(0, rotated.X, 6);
        Assert.Equal(0.01 * 1920 * 1.1, rotated.Y, 6);
    }

    [Fact]
    public void Avaliador_aplica_estabilizacao_mascaras_e_vinculo()
    {
        var media = Video();
        var tid = Guid.NewGuid();
        var source = MediaClip(media) with
        {
            Trackers = [new MotionTracker { Id = tid, Region = Sample(0, 0.5, 0.5), Samples = [Sample(0, 0.5, 0.5), Sample(2, 0.6, 0.5)] }],
            Masks = [new Mask { Id = Guid.NewGuid(), Points = [new(0.2, 0.2), new(0.4, 0.4)] }],
            Stabilization = new Stabilization { Steps = [new MotionStep(S(0), 0, 0, 0), new MotionStep(S(0.1), 0.01, 0, 0)], Zoom = 0.1 },
        };
        var follower = new Clip
        {
            Id = Guid.NewGuid(),
            Start = S(0),
            Duration = S(5),
            Shape = new ShapeStyle(),
            MotionLink = new TrackerLink { ClipId = source.Id, TrackerId = tid, ReferenceTime = S(0) },
        };
        var project = Setup(media, (0, source), (1, follower));
        var graph = TimelineEvaluator.Evaluate(project, project.MainSequence, S(1));

        var srcLayer = graph.Layers.Single(l => l.ClipId == source.Id);
        Assert.Single(srcLayer.Masks);
        Assert.Equal(1.1, srcLayer.Transform.ScaleX, 6);

        var shapeLayer = graph.Layers.Single(l => l.ClipId == follower.Id);
        Assert.NotNull(shapeLayer.Shape);
        // Em 1 s o objeto andou 0,05 da largura da imagem (1920 px ajustados ao quadro de 1920).
        Assert.Equal(0.05 * 1920, shapeLayer.Transform.X, 3);
    }

    [Fact]
    public void Mapeamento_imagem_projeto_e_reversivel()
    {
        var t = Transform.Identity with { X = 100, Y = -50, ScaleX = 1.5, ScaleY = 1.5, RotationDegrees = 30, FlipHorizontal = true };
        var (x, y) = CompositionMath.ToProject(t, 0.3, 0.8, 1280, 720, 1920, 1080);
        var (u, v) = CompositionMath.FromProject(t, x, y, 1280, 720, 1920, 1080);
        Assert.Equal(0.3, u, 9);
        Assert.Equal(0.8, v, 9);
        Assert.Equal((100.0, -50.0), CompositionMath.ToProject(t, 0.5, 0.5, 1280, 720, 1920, 1080));
    }

    // ---------------------------------------------------------------- 2,5D no compositor

    private static readonly CpuCompositor Compositor = new();

    private static LayerImage White(int w, int h, Transform t) =>
        new(new VisualLayer(Guid.NewGuid(), Guid.NewGuid(), null, MediaTime.Zero, t, 1, []), new BgraImage(w, h, Enumerable.Repeat((byte)255, w * h * 4).ToArray()), false);

    private static int LitColumns(BgraImage frame, int y)
    {
        var count = 0;
        for (var x = 0; x < frame.Width; x++)
            if (frame.Pixels[(y * frame.Width + x) * 4] > 128) count++;
        return count;
    }

    [Fact]
    public void Projecao_sem_3D_e_afim()
    {
        var h = CpuCompositor.Projection(100, 50, 60, 40, 2, 2, 0, 0, 0, 0, 200);
        Assert.Equal([0, 0, 1], h[6..]);
        Assert.Equal(60 - 100, h[2], 9); // canto (0,0) → cx − largura/2·escala
        var inv = CpuCompositor.Invert(h)!;
        Assert.Equal(0.5, inv[0], 9);
    }

    [Fact]
    public void Giro_3D_estreita_a_camada_e_de_lado_some()
    {
        var flat = Compositor.Compose(200, 100, 200, [White(100, 50, Transform.Identity)]);
        var turned = Compositor.Compose(200, 100, 200, [White(100, 50, Transform.Identity with { RotationY = 60 })]);
        var edge = Compositor.Compose(200, 100, 200, [White(100, 50, Transform.Identity with { RotationY = 90 })]);
        var flatW = LitColumns(flat, 50);
        var turnedW = LitColumns(turned, 50);
        Assert.InRange(flatW, 195, 200);
        Assert.InRange(turnedW, 80, 120); // ≈ cos 60° (com perspectiva)
        Assert.True(LitColumns(edge, 50) <= 2);
    }

    [Fact]
    public void Profundidade_afasta_e_reduz_a_camada()
    {
        var far = Compositor.Compose(200, 100, 200, [White(100, 50, Transform.Identity with { Depth = 400 })]);
        // f = 2 × 200 = 400 → metade do tamanho.
        Assert.InRange(LitColumns(far, 50), 95, 105);
        Assert.Equal(0, far.Pixels[(5 * 200 + 5) * 4]);
    }

    // ---------------------------------------------------------------- elementos gráficos

    [Fact]
    public void Formas_sao_desenhadas_na_escala_de_saida()
    {
        var style = new ShapeStyle { Kind = ShapeKind.Rectangle, Width = 100, Height = 50, FillColor = "#FFFF0000" };
        var img = ShapeRasterizer.Render(style, 2);
        Assert.InRange(img.Width, 200, 210);
        Assert.InRange(img.Height, 100, 110);
        var c = ((img.Height / 2) * img.Width + img.Width / 2) * 4;
        Assert.Equal((0, 0, 255, 255), (img.Pixels[c], img.Pixels[c + 1], img.Pixels[c + 2], img.Pixels[c + 3]));
        Assert.Equal(0, img.Pixels[3]);

        foreach (var preset in ShapePresets.All)
        {
            var r = ShapeRasterizer.Render(preset.Style, 0.5);
            Assert.True(r.Pixels.Where((_, i) => i % 4 == 3).Any(a => a > 200), preset.Name);
        }
    }

    [Fact]
    public void Elemento_grafico_entra_numa_trilha_de_graficos()
    {
        var project = Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now);
        var clip = ShapePresets.Create(ShapePresets.All[0].Style, S(1), S(5));
        var command = new AddTextClipCommand(project.MainSequence.Id, clip, TrackKind.Image);
        var after = command.Apply(project);
        var track = after.MainSequence.Tracks[0];
        Assert.Equal(TrackKind.Image, track.Kind);
        Assert.Equal("Gráficos 1", track.Name);
        Assert.Equal("Adicionar elemento gráfico", command.Description);
        Assert.Throws<EditRejectedException>(() => new AddTextClipCommand(project.MainSequence.Id, clip, TrackKind.Text).Apply(project));
    }

    // ---------------------------------------------------------------- análise de movimento (OpenCV)

    /// <summary>Quadro 160×120 com ruído fixo (textura) deslocado por (dx, dy) e um quadrado claro na posição informada.</summary>
    private static BgraImage Frame(int dx, int dy, int squareX = -1, int squareY = -1)
    {
        const int w = 160, h = 120;
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var sx = x - dx;
                var sy = y - dy;
                var v = (byte)((((sx * 73856093) ^ (sy * 19349663)) & 0xFF) / 2 + 40);
                if (squareX >= 0 && x >= squareX && x < squareX + 20 && y >= squareY && y < squareY + 20)
                    v = (byte)(220 + ((x - squareX) * 7 + (y - squareY) * 3) % 30);
                var i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = v;
                px[i + 3] = 255;
            }
        return new BgraImage(w, h, px);
    }

    [Fact]
    public void Rastreia_um_objeto_em_movimento()
    {
        var times = Enumerable.Range(0, 10).Select(i => FrameRate.Fps30.FrameToTime(i)).ToList();
        BgraImage? At(MediaTime t) => Frame(0, 0, 40 + FrameRate.Fps30.TimeToFrame(t) * 4 is var x ? (int)x : 0, 50);
        var start = new TrackSample(times[0], 50 / 160.0, 60 / 120.0, 20 / 160.0, 20 / 120.0);
        var samples = MotionAnalyzer.Track(At, times, start);
        Assert.Equal(10, samples.Count);
        var last = samples[^1];
        Assert.Equal((50 + 36) / 160.0, last.X, 2); // andou 9 × 4 px
        Assert.Equal(60 / 120.0, last.Y, 2);
    }

    [Fact]
    public void Mede_o_movimento_da_camera()
    {
        var times = Enumerable.Range(0, 5).Select(i => FrameRate.Fps30.FrameToTime(i)).ToList();
        var steps = MotionAnalyzer.AnalyzeCameraMotion(t => { var i = (int)FrameRate.Fps30.TimeToFrame(t); return Frame(i * 3, i * 2); }, times);
        Assert.Equal(5, steps.Count);
        Assert.Equal(0, steps[0].Dx);
        foreach (var s in steps.Skip(1))
        {
            Assert.Equal(3 / 160.0, s.Dx, 0.001);
            Assert.Equal(2 / 160.0, s.Dy, 0.001);
            Assert.Equal(0, s.Da, 0.01);
        }
    }

    // ---------------------------------------------------------------- persistência

    [Fact]
    public void Composicao_sobrevive_ao_salvar_e_abrir()
    {
        var clip = new Clip
        {
            Id = Guid.NewGuid(),
            Start = S(0),
            Duration = S(2),
            Shape = new ShapeStyle { Kind = ShapeKind.Star, Sides = 7 },
            Transform = Transform.Identity with { RotationX = 10, RotationY = -20, Depth = 300 },
            Masks = [new Mask { Id = Guid.NewGuid(), Kind = MaskKind.Path, Points = [new(0.1, 0.2), new(0.3, 0.4), new(0.5, 0.1)],
                Keys = [new MaskShapeKey(S(1), [new(0.1, 0.2), new(0.3, 0.4), new(0.5, 0.1)])], Mode = MaskMode.Subtract }],
            Trackers = [new MotionTracker { Id = Guid.NewGuid(), Region = Sample(0, 0.5, 0.5), Samples = [Sample(0, 0.5, 0.5)] }],
            MotionLink = new TrackerLink { ClipId = Guid.NewGuid(), TrackerId = Guid.NewGuid(), ReferenceTime = S(1), FollowScale = true },
            Stabilization = new Stabilization { Smoothness = 2, Steps = [new MotionStep(S(0), 0.1, 0.2, 0.3)] },
        };
        var json = JsonSerializer.Serialize(clip, MedJson.Options);
        var back = JsonSerializer.Deserialize<Clip>(json, MedJson.Options)!;
        Assert.Equal(ShapeKind.Star, back.Shape!.Kind);
        Assert.Equal(7, back.Shape.Sides);
        Assert.Equal(300, back.Transform.Depth);
        Assert.Equal(clip.Masks[0].Points, back.Masks[0].Points);
        Assert.Equal(MaskMode.Subtract, back.Masks[0].Mode);
        Assert.Equal(S(1), back.Masks[0].Keys[0].Offset);
        Assert.Equal(clip.Trackers[0].Samples[0], back.Trackers[0].Samples[0]);
        Assert.True(back.MotionLink!.FollowScale);
        Assert.Equal(0.3, back.Stabilization!.Steps[0].Da);
    }
}
