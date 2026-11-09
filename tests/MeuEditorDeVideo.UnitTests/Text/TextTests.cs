using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Text;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.ProjectEngine;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Text;

/// <summary>F8: textos, títulos, créditos, tarjas e legendas — domínio, animação, composição e formato .med.</summary>
public sealed class TextTests
{
    private static readonly ProjectSettings Settings = new() { Width = 640, Height = 360, FrameRate = FrameRate.Fps25 };

    private static MediaTime S(double s) => MediaTime.FromSecondsApprox(s);

    private static Project NewProject() => Project.CreateNew("Textos", Settings, StorageMode.Linked, DateTimeOffset.UtcNow);

    private static Project With(Project p, params (TrackKind Kind, Clip[] Clips)[] tracks)
    {
        var seq = p.MainSequence with
        {
            Tracks = [.. tracks.Select((t, i) => new Track { Id = Guid.NewGuid(), Kind = t.Kind, Name = $"T{i}", Clips = [.. t.Clips] })],
        };
        return p.WithSequence(seq);
    }

    private static Clip TextClip(string text, TextFormat? format = null, double start = 0, double duration = 4, Transform? transform = null) => new()
    {
        Id = Guid.NewGuid(), Start = S(start), Duration = S(duration), Text = text,
        TextFormat = format ?? new TextFormat { Style = new TextStyle { FontSize = 60 } },
        Transform = transform ?? Transform.Identity,
    };

    private sealed class NoVideo : IVideoFrameSource
    {
        public BgraImage? GetFrame(MediaAsset asset, string path, MediaTime sourceTime, int maxWidth, int maxHeight) => null;
        public void Trim(IReadOnlySet<Guid> inUse) { }
        public void Dispose() { }
    }

    /// <summary>Fonte que devolve um quadro sólido (vermelho) para qualquer mídia.</summary>
    private sealed class SolidVideo : IVideoFrameSource
    {
        public BgraImage? GetFrame(MediaAsset asset, string path, MediaTime sourceTime, int maxWidth, int maxHeight)
        {
            var px = new byte[maxWidth * maxHeight * 4];
            for (var i = 0; i < px.Length; i += 4) { px[i + 2] = 255; px[i + 3] = 255; }
            return new BgraImage(maxWidth, maxHeight, px);
        }
        public void Trim(IReadOnlySet<Guid> inUse) { }
        public void Dispose() { }
    }

    private static BgraImage Render(Project p, double t, IVideoFrameSource? video = null, bool subtitles = true, Dictionary<Guid, string>? paths = null) =>
        new FrameRenderer(new CpuCompositor()).Render(video ?? new NoVideo(), p, p.MainSequence, paths ?? [], S(t), 640, 360, includeSubtitles: subtitles);

    /// <summary>Retângulo que contém os pixels claros (texto branco) do quadro.</summary>
    private static (int Left, int Top, int Right, int Bottom, int Count) Bright(BgraImage img, int threshold = 200)
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1, n = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
            {
                var i = (y * img.Width + x) * 4;
                if (img.Pixels[i] < threshold || img.Pixels[i + 1] < threshold || img.Pixels[i + 2] < threshold) continue;
                n++;
                l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y);
            }
        return (l, t, r, b, n);
    }

    // ------------------------------------------------------------------ rasterização

    [Fact]
    public void Texto_e_rasterizado_com_alfa_e_caixa_proporcional_a_fonte()
    {
        var small = TextRasterizer.Render("Olá", 3, new TextStyle { FontSize = 40 }, 1, 1920);
        var big = TextRasterizer.Render("Olá", 3, new TextStyle { FontSize = 80 }, 1, 1920);
        Assert.InRange(big.BoxHeight / small.BoxHeight, 1.8, 2.2);
        Assert.InRange(big.BoxWidth / small.BoxWidth, 1.8, 2.2);
        Assert.Contains(small.Image.Pixels.Where((_, i) => i % 4 == 3), a => a == 255);
        Assert.Contains(small.Image.Pixels.Where((_, i) => i % 4 == 3), a => a == 0);
    }

    [Fact]
    public void Texto_longo_quebra_linhas_dentro_da_largura_maxima()
    {
        var style = new TextStyle { FontSize = 40, MaxWidth = 0.5 };
        var r = TextRasterizer.Render(string.Join(' ', Enumerable.Repeat("palavra", 20)), 159, style, 1, 1000);
        Assert.True(r.BoxWidth <= 501, $"largura {r.BoxWidth}");
        Assert.True(r.BoxHeight > 40 * 3, $"altura {r.BoxHeight}");
    }

    [Fact]
    public void Contorno_sombra_e_fundo_aumentam_a_imagem_e_pintam_as_cores()
    {
        var plain = TextRasterizer.Render("A", 1, new TextStyle { FontSize = 100 }, 1, 1000);
        var styled = TextRasterizer.Render("A", 1, new TextStyle
        {
            FontSize = 100, OutlineWidth = 6, OutlineColor = "#FF00FF00", Shadow = true, ShadowOffsetX = 10, ShadowOffsetY = 10, ShadowBlur = 4,
        }, 1, 1000);
        Assert.True(styled.Image.Width > plain.Image.Width);
        // Contorno verde presente.
        var px = styled.Image.Pixels;
        Assert.Contains(Enumerable.Range(0, px.Length / 4), i => px[i * 4 + 1] > 200 && px[i * 4 + 2] < 60 && px[i * 4] < 60 && px[i * 4 + 3] > 200);

        var boxed = TextRasterizer.Render("A", 1, new TextStyle { FontSize = 100, BackgroundColor = "#FF0000FF", BackgroundPadding = 20 }, 1, 1000);
        Assert.InRange(boxed.BoxWidth - plain.BoxWidth, 39, 41);
        // Canto da caixa (dentro do arredondamento) é azul opaco.
        var m = (int)((boxed.Image.Width - boxed.BoxWidth) / 2);
        var c = ((m + 10) * boxed.Image.Width + m + 10) * 4;
        Assert.Equal(255, boxed.Image.Pixels[c]);
        Assert.Equal(255, boxed.Image.Pixels[c + 3]);
    }

    [Fact]
    public void Fonte_desconhecida_cai_na_padrao_sem_erro()
    {
        var r = TextRasterizer.Render("Teste", 5, new TextStyle { FontFamily = "Fonte Que Nao Existe 123" }, 1, 1000);
        Assert.True(r.BoxWidth > 10);
        Assert.Contains("Segoe UI", FontCatalog.Families());
    }

    // ------------------------------------------------------------------ composição

    [Fact]
    public void Texto_aparece_centralizado_no_quadro()
    {
        var p = With(NewProject(), (TrackKind.Text, [TextClip("MEU TEXTO")]));
        var b = Bright(Render(p, 1));
        Assert.True(b.Count > 200);
        Assert.InRange((b.Left + b.Right) / 2.0, 310, 330);
        Assert.InRange((b.Top + b.Bottom) / 2.0, 170, 192);
    }

    [Fact]
    public void Alinhamento_a_esquerda_usa_x_como_borda_esquerda_e_ancora_inferior_usa_y_como_base()
    {
        var format = new TextFormat { Anchor = TextAnchor.Bottom, Style = new TextStyle { FontSize = 40, Align = TextAlign.Left } };
        var p = With(NewProject(), (TrackKind.Text, [TextClip("Tarja", format, transform: Transform.Identity with { X = -280, Y = 150 })]));
        var b = Bright(Render(p, 1));
        Assert.InRange(b.Left, 40, 50);   // 320 − 280 = 40
        Assert.InRange(b.Bottom, 310, 330); // 180 + 150 = 330 (base do bloco)
    }

    [Fact]
    public void Textos_e_legendas_ficam_na_frente_do_video_mesmo_abaixo_na_lista()
    {
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = MediaKind.Video, Name = "v", OriginalPath = "v.mp4", Duration = S(10), Width = 640, Height = 360,
            FrameRate = FrameRate.Fps25,
        };
        var video = new Clip { Id = Guid.NewGuid(), MediaAssetId = asset.Id, Start = MediaTime.Zero, Duration = S(10) };
        var p = With(NewProject() with { Media = [asset] }, (TrackKind.Video, [video]), (TrackKind.Text, [TextClip("FRENTE")]));
        var order = TimelineEvaluator.Evaluate(p, p.MainSequence, S(1)).Layers;
        Assert.Equal(2, order.Count);
        Assert.NotNull(order[1].Text);
        var frame = Render(p, 1, new SolidVideo(), paths: new() { [asset.Id] = "v.mp4" });
        Assert.True(Bright(frame).Count > 200);
    }

    [Fact]
    public void Legendas_podem_ser_excluidas_da_exportacao()
    {
        var sub = SubtitleOps.ToClips([new SubtitleEntry(S(0), S(3), "Legenda queimada")], Settings);
        var p = With(NewProject(), (TrackKind.Subtitle, [.. sub]));
        Assert.True(Bright(Render(p, 1)).Count > 100);
        Assert.Equal(0, Bright(Render(p, 1, subtitles: false)).Count);
        // Legenda fica na parte de baixo do quadro.
        Assert.True(Bright(Render(p, 1)).Top > 250);
    }

    [Fact]
    public void Creditos_rolam_de_baixo_para_cima()
    {
        var format = new TextFormat { Kind = TextKind.Credits, Style = new TextStyle { FontSize = 30 } };
        var p = With(NewProject(), (TrackKind.Text, [TextClip("A\nB\nC", format, duration: 10)]));
        Assert.Equal(0, Bright(Render(p, 0)).Count);           // ainda abaixo do quadro
        var mid = Bright(Render(p, 5));
        Assert.InRange((mid.Top + mid.Bottom) / 2.0, 150, 210); // no meio
        var later = Bright(Render(p, 6));
        Assert.True(later.Top < mid.Top);                       // subindo
    }

    [Fact]
    public void Maquina_de_escrever_revela_o_texto_aos_poucos_sem_mudar_a_posicao_inicial()
    {
        var format = new TextFormat
        {
            Style = new TextStyle { FontSize = 50, Align = TextAlign.Left }, AnimationIn = TextAnimationKind.Typewriter,
            AnimationInDuration = S(2),
        };
        var p = With(NewProject(), (TrackKind.Text, [TextClip("ABCDEFGH", format, transform: Transform.Identity with { X = -200 })]));
        var half = Bright(Render(p, 1));
        var full = Bright(Render(p, 3));
        Assert.True(half.Count < full.Count * 0.7);
        Assert.InRange(Math.Abs(half.Left - full.Left), 0, 2);
        Assert.Equal(0, Bright(Render(p, 0)).Count);
    }

    // ------------------------------------------------------------------ animações

    [Fact]
    public void Animacoes_de_entrada_e_saida()
    {
        var clip = TextClip("x", new TextFormat
        {
            AnimationIn = TextAnimationKind.SlideUp, AnimationOut = TextAnimationKind.Zoom,
            AnimationInDuration = S(1), AnimationOutDuration = S(1),
        });
        var start = TextAnimator.At(clip, MediaTime.Zero, 1000, 1000);
        Assert.Equal(0, start.Opacity, 3);
        Assert.Equal(1000 * TextAnimator.SlideFraction, start.OffsetY, 3); // começa abaixo
        Assert.Equal(TextAnimationState.Identity, TextAnimator.At(clip, S(2), 1000, 1000));
        var end = TextAnimator.At(clip, S(3.5), 1000, 1000);
        Assert.InRange(end.Scale, 0.5, 0.95);
        Assert.InRange(end.Opacity, 0.4, 0.6);

        var pop = TextAnimator.At(TextClip("x", new TextFormat { AnimationIn = TextAnimationKind.Pop, AnimationInDuration = S(1) }), S(0.6), 100, 100);
        Assert.True(pop.Scale > 1); // passa do tamanho final
    }

    [Fact]
    public void Animacoes_que_nao_cabem_no_clipe_sao_reduzidas()
    {
        var clip = TextClip("x", new TextFormat
        {
            AnimationIn = TextAnimationKind.Fade, AnimationOut = TextAnimationKind.Fade, AnimationInDuration = S(2), AnimationOutDuration = S(2),
        }, duration: 2);
        Assert.Equal(1, TextAnimator.At(clip, S(1), 100, 100).Opacity, 2);
        Assert.Equal(0.25, TextAnimator.At(clip, S(0.25), 100, 100).Opacity, 2); // entrada reduzida a 1 s
    }

    [Fact]
    public void Caracteres_visiveis_nao_cortam_emoji()
    {
        Assert.Equal(1, TextAnimator.VisibleCharacters("a😀b", 0.5)); // não deixa meio emoji
        Assert.Equal(4, TextAnimator.VisibleCharacters("a😀b", 1));
    }

    // ------------------------------------------------------------------ comandos

    [Fact]
    public void Modelos_criam_clipes_de_texto_proporcionais_ao_projeto()
    {
        foreach (var kind in Enum.GetValues<TextKind>())
        {
            var clip = TextPresets.Create(kind, Settings, S(1.01));
            Assert.True(clip.IsText);
            Assert.Equal(kind, clip.TextFormat!.Kind);
            Assert.Equal(S(1), clip.Start); // alinhado ao quadro (25 fps)
        }
        Assert.Equal(Math.Round(140 / 3.0), TextPresets.Create(TextKind.Title, Settings, MediaTime.Zero).TextFormat!.Style.FontSize);
        Assert.Equal(TrackKind.Subtitle, TextPresets.TrackKindFor(TextKind.Subtitle));
        Assert.True(TextPresets.Create(TextKind.LowerThird, Settings, MediaTime.Zero).Transform.X < 0);
    }

    [Fact]
    public void Adicionar_texto_usa_trilha_livre_ou_cria_uma_no_topo()
    {
        var p = NewProject();
        var seq = p.MainSequence;
        var first = TextPresets.Create(TextKind.Text, Settings, MediaTime.Zero);
        p = new AddTextClipCommand(seq.Id, first, TrackKind.Text).Apply(p);
        var textTracks = p.MainSequence.Tracks.Where(t => t.Kind == TrackKind.Text).ToList();
        Assert.Single(textTracks);
        Assert.Single(textTracks[0].Clips);

        // Mesmo intervalo: nova trilha no topo (na frente).
        var cmd = new AddTextClipCommand(seq.Id, TextPresets.Create(TextKind.Title, Settings, S(1)), TrackKind.Text);
        p = cmd.Apply(p);
        Assert.Equal(cmd.NewTrackId, p.MainSequence.Tracks[0].Id);
        Assert.Equal("Texto 2", p.MainSequence.Tracks[0].Name);

        // Legenda sem trilha de legendas: cria "Legendas 1".
        p = new AddTextClipCommand(seq.Id, TextPresets.Create(TextKind.Subtitle, Settings, S(20)), TrackKind.Subtitle).Apply(p);
        Assert.Equal("Legendas 1", p.MainSequence.Tracks[0].Name);

        Assert.Throws<EditRejectedException>(() => new AddTextClipCommand(seq.Id, first, TrackKind.Video).Apply(p));
    }

    [Fact]
    public void Importar_legendas_corrige_sobreposicao_e_alinha_a_quadros()
    {
        var clips = SubtitleOps.ToClips(
        [
            new SubtitleEntry(S(1.013), S(3), "Um"),
            new SubtitleEntry(S(2.5), S(4), "Dois"),
            new SubtitleEntry(S(5), S(5), "Vazia"),
            new SubtitleEntry(S(6), S(7), "   "),
        ], Settings);
        Assert.Equal(2, clips.Count);
        Assert.Equal(S(1), clips[0].Start);
        Assert.Equal(Settings.FrameRate.SnapToFrame(S(2.5)), clips[0].End); // encurtada até a seguinte
        Assert.Equal(TextKind.Subtitle, clips[1].TextFormat!.Kind);

        var p = NewProject();
        var cmd = new ImportSubtitlesCommand(p.MainSequence.Id, clips);
        p = cmd.Apply(p);
        var track = p.MainSequence.Tracks[0];
        Assert.Equal(TrackKind.Subtitle, track.Kind);
        Assert.Equal(2, track.Clips.Count);

        var more = SubtitleOps.ToClips([new SubtitleEntry(S(10), S(11), "Três")], Settings);
        p = new ImportSubtitlesCommand(p.MainSequence.Id, more, track.Id, Replace: true).Apply(p);
        Assert.Equal("Três", Assert.Single(p.MainSequence.Tracks[0].Clips).Text);
        Assert.Throws<EditRejectedException>(() => new ImportSubtitlesCommand(p.MainSequence.Id, []).Apply(p));
    }

    [Fact]
    public void Editar_tempo_da_legenda_nao_invade_a_vizinha()
    {
        var clips = SubtitleOps.ToClips([new SubtitleEntry(S(1), S(2), "A"), new SubtitleEntry(S(3), S(4), "B")], Settings);
        var p0 = NewProject();
        var p = new ImportSubtitlesCommand(p0.MainSequence.Id, clips).Apply(p0);
        var seqId = p.MainSequence.Id;
        p = new SetClipRangeCommand(seqId, clips[0].Id, S(0.6), S(3)).Apply(p);
        var a = p.MainSequence.Tracks[0].Clips[0];
        Assert.Equal(S(0.6), a.Start);
        Assert.Equal(S(3), a.End);
        Assert.Throws<EditRejectedException>(() => new SetClipRangeCommand(seqId, clips[0].Id, S(0.6), S(3.5)).Apply(p));
        Assert.Throws<EditRejectedException>(() => new SetClipRangeCommand(seqId, clips[0].Id, S(2), S(2)).Apply(p));
    }

    [Fact]
    public void Legendas_da_sequencia_ignoram_trilhas_ocultas_e_saem_ordenadas()
    {
        var a = SubtitleOps.ToClips([new SubtitleEntry(S(5), S(6), "depois")], Settings);
        var b = SubtitleOps.ToClips([new SubtitleEntry(S(1), S(2), "antes")], Settings);
        var c = SubtitleOps.ToClips([new SubtitleEntry(S(3), S(4), "oculta")], Settings);
        var p = With(NewProject(), (TrackKind.Subtitle, [.. a]), (TrackKind.Subtitle, [.. b]), (TrackKind.Subtitle, [.. c]));
        var seq = p.MainSequence;
        seq = seq with { Tracks = seq.Tracks.SetItem(2, seq.Tracks[2] with { IsHidden = true }) };
        Assert.Equal(["antes", "depois"], SubtitleOps.FromSequence(seq).Select(e => e.Text));
    }

    [Fact]
    public void Exportacao_grava_legendas_por_parte_com_tempos_relativos()
    {
        var dir = Path.Combine(Path.GetTempPath(), "text-sub-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var clips = SubtitleOps.ToClips([new SubtitleEntry(S(1), S(2), "Primeira"), new SubtitleEntry(S(9), S(12), "Atravessa o corte")], Settings);
            var seq = With(NewProject(), (TrackKind.Subtitle, [.. clips])).MainSequence;
            var settings = new MeuEditorDeVideo.Core.Export.ExportSettings { SubtitleFile = MeuEditorDeVideo.Core.Export.SubtitleFileExport.Srt };
            var files = RenderEngine.WriteSubtitleFiles(settings, seq,
                [(Path.Combine(dir, "v_parte1.mp4"), S(0), S(10)), (Path.Combine(dir, "v_parte2.mp4"), S(10), S(20))]);
            Assert.Equal(2, files.Count);
            var first = MeuEditorDeVideo.Core.Subtitles.SubtitleFile.Load(files[0]);
            Assert.Equal(["Primeira", "Atravessa o corte"], first.Select(c => c.Text));
            Assert.Equal(S(10) - S(9), first[1].End - first[1].Start);
            var second = Assert.Single(MeuEditorDeVideo.Core.Subtitles.SubtitleFile.Load(files[1]));
            Assert.Equal(MediaTime.Zero, second.Start);
            Assert.Equal(S(2), second.End);

            var none = RenderEngine.WriteSubtitleFiles(settings with { SubtitleFile = MeuEditorDeVideo.Core.Export.SubtitleFileExport.None }, seq,
                [(Path.Combine(dir, "x.mp4"), S(0), S(10))]);
            Assert.Empty(none);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // ------------------------------------------------------------------ formato .med

    [Fact]
    public async Task Texto_formatado_sobrevive_a_salvar_e_carregar()
    {
        var root = Path.Combine(Path.GetTempPath(), "text-serial-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"));
            paths.EnsureCreated();
            var store = new MedProjectStore(paths, NullLogger<MedProjectStore>.Instance);
            var clip = TextPresets.Create(TextKind.LowerThird, Settings, S(2)) with { Text = "Ana Souza\nRepórter" };
            clip = clip with
            {
                TextFormat = clip.TextFormat! with
                {
                    AnimationOut = TextAnimationKind.Typewriter,
                    Style = clip.TextFormat.Style with { FontFamily = "Georgia", Italic = true, Underline = true, OutlineWidth = 3, ShadowColor = "#80FF0000" },
                },
            };
            var p = With(NewProject(), (TrackKind.Text, [clip]));
            var file = Path.Combine(root, "t.med");
            await store.SaveAsync(new SaveRequest(p, file, IncludeMedia: false, new Dictionary<Guid, string>()));
            var loaded = (await store.LoadAsync(file)).Project.MainSequence.Tracks[0].Clips[0];
            Assert.Equal(clip.Text, loaded.Text);
            Assert.Equal(clip.TextFormat, loaded.TextFormat);
            Assert.Equal(clip.Transform, loaded.Transform);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
