using System.IO;
using System.IO.Compression;
using MeuEditorDeVideo.Application.Packs;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Effects;
using MeuEditorDeVideo.MediaEngine.Recording;
using MeuEditorDeVideo.ProjectEngine.Format;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Domain;

/// <summary>Camada de ajuste, tela dividida, clipe composto, multicâmera, 360°, gravação de tela e pacotes de conteúdo.</summary>
public sealed class StructuralFeaturesTests
{
    private const int Video1 = 0;
    private const int Video2 = 1;
    private const int Audio1 = 2;

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset Asset(MediaKind kind, double seconds, int w = 1920, int h = 1080, bool audio = false) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Name = "m",
        OriginalPath = @"C:\m\" + Guid.NewGuid(),
        Duration = S(seconds),
        Width = w,
        Height = h,
        Info = audio ? new MediaTechnicalInfo { AudioCodec = "aac" } : null,
    };

    private static Clip NewClip(MediaAsset asset, double start, double duration, Guid? link = null) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = asset.Id,
        Start = S(start),
        Duration = S(duration),
        LinkId = link,
    };

    private static Project NewProject(params MediaAsset[] media) =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [.. media] };

    private static Project Place(Project project, int track, params Clip[] clips)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(track, seq.Tracks[track] with { Clips = [.. clips] }) });
    }

    /// <summary>Fonte com uma cor por mídia (pela ordem de cadastro): vermelho, verde, azul…</summary>
    private sealed class ColorVideo(Project project) : IVideoFrameSource
    {
        private static readonly (byte R, byte G, byte B)[] Colors = [(255, 0, 0), (0, 255, 0), (0, 0, 255)];

        public BgraImage? GetFrame(MediaAsset asset, string path, MediaTime sourceTime, int maxWidth, int maxHeight)
        {
            var (r, g, b) = Colors[project.Media.FindIndex(m => m.Id == asset.Id) % Colors.Length];
            var px = new byte[maxWidth * maxHeight * 4];
            for (var i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
            return new BgraImage(maxWidth, maxHeight, px);
        }

        public void Trim(IReadOnlySet<Guid> inUse) { }
        public void Dispose() { }
    }

    private static BgraImage Render(Project p, double t) =>
        new FrameRenderer(new CpuCompositor()).Render(new ColorVideo(p), p, p.MainSequence, p.Media.ToDictionary(m => m.Id, m => "x"), S(t), 160, 90);

    private static (int R, int G, int B) Pixel(BgraImage img, int x, int y)
    {
        var i = (y * img.Width + x) * 4;
        return (img.Pixels[i + 2], img.Pixels[i + 1], img.Pixels[i]);
    }

    // ---------------------------------------------------------------- camada de ajuste

    [Fact]
    public void Adjustment_layer_applies_its_effects_to_the_layers_below()
    {
        var a = Asset(MediaKind.Video, 10);
        var p = Place(NewProject(a), Video1, NewClip(a, 0, 10));
        var add = new AddAdjustmentLayerCommand(p.MainSequence.Id, S(0), S(5));
        p = add.Apply(p);
        var effectTrack = p.MainSequence.Tracks[0];
        Assert.Equal(TrackKind.Effect, effectTrack.Kind);
        var gray = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Grayscale), "intensity", 100);
        p = p.WithSequence(p.MainSequence with
        {
            Tracks = p.MainSequence.Tracks.SetItem(0, effectTrack with { Clips = [effectTrack.Clips[0] with { Effects = [gray] }] }),
        });

        var inside = Pixel(Render(p, 1), 80, 45);
        Assert.Equal(inside.R, inside.G);
        Assert.Equal(inside.G, inside.B);
        Assert.InRange(inside.R, 40, 70); // vermelho em cinza (0,2126 × 255)

        var after = Pixel(Render(p, 6), 80, 45); // fora da camada de ajuste: vermelho original
        Assert.Equal((255, 0, 0), after);
    }

    [Fact]
    public void Adjustment_layer_opacity_mixes_with_the_original()
    {
        var a = Asset(MediaKind.Video, 10);
        var p = Place(NewProject(a), Video1, NewClip(a, 0, 10));
        p = new AddAdjustmentLayerCommand(p.MainSequence.Id, S(0), S(5)).Apply(p);
        var t = p.MainSequence.Tracks[0];
        var inv = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Invert), "intensity", 100);
        p = p.WithSequence(p.MainSequence with { Tracks = p.MainSequence.Tracks.SetItem(0, t with { Clips = [t.Clips[0] with { Effects = [inv], Opacity = 0.5 }] }) });
        var px = Pixel(Render(p, 1), 80, 45);
        Assert.InRange(px.R, 120, 135);
        Assert.InRange(px.G, 120, 135);
    }

    [Fact]
    public void Disabled_clip_is_not_drawn_nor_heard()
    {
        var a = Asset(MediaKind.Video, 10, audio: true);
        var link = Guid.NewGuid();
        var v = NewClip(a, 0, 10, link);
        var au = NewClip(a, 0, 10, link);
        var p = Place(Place(NewProject(a), Video1, v), Audio1, au);
        p = new SetClipsEnabledCommand(p.MainSequence.Id, [v.Id, au.Id], false).Apply(p);
        Assert.Empty(TimelineEvaluator.Evaluate(p, p.MainSequence, S(1)).Layers);
        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, S(0), S(2)));
        p = new SetClipsEnabledCommand(p.MainSequence.Id, [v.Id], true).Apply(p);
        Assert.Single(TimelineEvaluator.Evaluate(p, p.MainSequence, S(1)).Layers);
    }

    // ---------------------------------------------------------------- tela dividida

    [Fact]
    public void Split_screen_cell_transform_covers_the_cell_and_crops_the_excess()
    {
        // 16:9 no quadro 16:9, metade esquerda: escala 1 (altura cobre), recorte de 25 % de cada lado.
        var t = SplitScreenCommand.CellTransform(1920, 1080, 1920, 1080, (0, 0, 0.5, 1));
        Assert.Equal(1, t.ScaleX, 6);
        Assert.Equal(0.25, t.CropLeft, 6);
        Assert.Equal(0.25, t.CropRight, 6);
        Assert.Equal(0, t.CropTop, 6);
        Assert.Equal(-480, t.X, 6);
        Assert.Equal(0, t.Y, 6);

        // Grade 2×2, canto inferior direito: escala 0,5, sem recorte.
        var q = SplitScreenCommand.CellTransform(1920, 1080, 1920, 1080, (0.5, 0.5, 0.5, 0.5));
        Assert.Equal(0.5, q.ScaleX, 6);
        Assert.Equal(0, q.CropLeft, 6);
        Assert.Equal(480, q.X, 6);
        Assert.Equal(270, q.Y, 6);
    }

    [Fact]
    public void Split_screen_shows_each_clip_in_its_cell()
    {
        var a = Asset(MediaKind.Video, 10);
        var b = Asset(MediaKind.Video, 10);
        var ca = NewClip(a, 0, 10);
        var cb = NewClip(b, 0, 10);
        var p = Place(Place(NewProject(a, b), Video1, ca), Video2, cb);
        p = new SplitScreenCommand(p.MainSequence.Id, [ca.Id, cb.Id], "2h").Apply(p);
        var img = Render(p, 1);
        Assert.Equal((255, 0, 0), Pixel(img, 20, 45));  // Vídeo 1 (de cima) à esquerda
        Assert.Equal((0, 255, 0), Pixel(img, 140, 45)); // Vídeo 2 à direita
    }

    [Fact]
    public void Split_screen_rejects_clips_on_the_same_track()
    {
        var a = Asset(MediaKind.Video, 10);
        var c1 = NewClip(a, 0, 4);
        var c2 = NewClip(a, 5, 4);
        var p = Place(NewProject(a), Video1, c1, c2);
        Assert.Throws<EditRejectedException>(() => new SplitScreenCommand(p.MainSequence.Id, [c1.Id, c2.Id], "2h").Apply(p));
    }

    // ---------------------------------------------------------------- clipe composto

    [Fact]
    public void Compound_clip_renders_and_plays_the_nested_sequence_and_can_be_unpacked()
    {
        var a = Asset(MediaKind.Video, 10, audio: true);
        var b = Asset(MediaKind.Video, 10);
        var link = Guid.NewGuid();
        var v = NewClip(a, 2, 4, link);
        var au = NewClip(a, 2, 4, link);
        var top = NewClip(b, 3, 1) with { Transform = Transform.Identity with { ScaleX = 0.5, ScaleY = 0.5 } };
        var p = Place(Place(Place(NewProject(a, b), Video1, top), Video2, v), Audio1, au);
        var create = new CreateCompoundClipCommand(p.MainSequence.Id, [v.Id, top.Id]);
        p = create.Apply(p);

        Assert.Equal(2, p.Sequences.Count);
        Assert.Empty(ProjectValidator.Validate(p));
        var main = p.MainSequence;
        var compound = main.Tracks.SelectMany(t => t.Clips).Single();
        Assert.True(compound.IsCompound);
        Assert.Equal(S(2), compound.Start);
        Assert.Equal(S(4), compound.Duration);

        // Imagem: o vídeo verde em escala 0,5 sobre o vermelho, como antes.
        var img = Render(p, 3.5);
        Assert.Equal((0, 255, 0), Pixel(img, 80, 45));
        Assert.Equal((255, 0, 0), Pixel(img, 5, 5));
        Assert.Equal((0, 0, 0), Pixel(Render(p, 1), 80, 45));

        // Som: o áudio vinculado entrou no composto e continua no mesmo instante.
        var seg = Assert.Single(TimelineEvaluator.AudioIn(p, main, S(0), S(10)));
        Assert.Equal(S(2), seg.TimelineStart);
        Assert.Equal(S(6), seg.TimelineEnd);
        Assert.Equal(S(2), seg.Clip.Start);
        Assert.Contains(a.Id, TimelineEvaluator.AssetsInUse(p, main));

        // Desfazer: os clipes voltam (com novos Ids) e a sequência aninhada some.
        p = new UnpackCompoundClipCommand(main.Id, compound.Id).Apply(p);
        Assert.Single(p.Sequences);
        var clips = p.MainSequence.Tracks.SelectMany(t => t.Clips).ToList();
        Assert.Equal(3, clips.Count);
        Assert.All(clips.Where(c => c.MediaAssetId == a.Id), c => Assert.Equal(S(2), c.Start));
        Assert.Equal(S(3), clips.Single(c => c.MediaAssetId == b.Id).Start);
        Assert.Empty(ProjectValidator.Validate(p));
        // As trilhas devolvidas não repetem o nome das trilhas que ficaram vazias.
        var names = p.MainSequence.Tracks.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Unpacking_a_trimmed_compound_clip_keeps_only_the_visible_part()
    {
        var a = Asset(MediaKind.Video, 20);
        var v = NewClip(a, 0, 10);
        var p = Place(NewProject(a), Video1, v);
        var create = new CreateCompoundClipCommand(p.MainSequence.Id, [v.Id]);
        p = create.Apply(p);
        var compound = p.MainSequence.Tracks.SelectMany(t => t.Clips).Single();
        // Mostra só de 3 a 7 s da sequência aninhada, a partir de 1 s da linha do tempo.
        var trimmed = compound with { Start = S(1), SourceIn = S(3), Duration = S(4) };
        var track = p.MainSequence.Tracks.First(t => t.Clips.Contains(compound));
        p = p.WithSequence(TimelineOps.ReplaceTrack(p.MainSequence, track with { Clips = [trimmed] }));
        p = new UnpackCompoundClipCommand(p.MainSequence.Id, trimmed.Id).Apply(p);
        var clip = p.MainSequence.Tracks.SelectMany(t => t.Clips).Single();
        Assert.Equal(S(1), clip.Start);
        Assert.Equal(S(4), clip.Duration);
        Assert.Equal(S(3), clip.SourceIn);
    }

    [Fact]
    public void Compound_clip_round_trips_through_json()
    {
        var a = Asset(MediaKind.Video, 10);
        var v = NewClip(a, 0, 5);
        var p = Place(NewProject(a), Video1, v);
        p = new CreateCompoundClipCommand(p.MainSequence.Id, [v.Id]).Apply(p);
        p = new AddAdjustmentLayerCommand(p.MainSequence.Id, S(0), S(2)).Apply(p);
        var json = new DomainJson();
        var back = json.Deserialize<Project>(json.Serialize(p))!;
        Assert.Equal(2, back.Sequences.Count);
        Assert.Contains(back.MainSequence.Tracks.SelectMany(t => t.Clips), c => c.IsCompound);
        Assert.Contains(back.MainSequence.Tracks.SelectMany(t => t.Clips), c => c.IsAdjustment);
        Assert.All(back.MainSequence.Tracks.SelectMany(t => t.Clips), c => Assert.True(c.Enabled));
    }

    [Fact]
    public void Old_clip_json_without_enabled_loads_enabled()
    {
        var json = new DomainJson();
        var clip = json.Deserialize<Clip>("""{"id":"6c7e2b0c-1d7a-4d62-9a3e-1b2c3d4e5f60","start":0,"duration":705600000}""")!;
        Assert.True(clip.Enabled);
        Assert.False(clip.IsAdjustment);
        Assert.Null(clip.NestedSequenceId);
    }

    // ---------------------------------------------------------------- multicâmera

    [Fact]
    public void Multicam_switch_cuts_every_camera_and_enables_only_the_chosen_one()
    {
        var a = Asset(MediaKind.Video, 20);
        var b = Asset(MediaKind.Video, 20);
        var ca = NewClip(a, 0, 20);
        var cb = NewClip(b, 0, 20);
        var p = Place(Place(NewProject(a, b), Video1, ca), Video2, cb);
        p = new MulticamSwitchCommand(p.MainSequence.Id, 1, S(5)).Apply(p);   // câmera 2 a partir de 5 s
        p = new MulticamSwitchCommand(p.MainSequence.Id, 0, S(12)).Apply(p);  // câmera 1 a partir de 12 s

        Assert.Equal((255, 0, 0), Pixel(Render(p, 2), 80, 45));
        Assert.Equal((0, 255, 0), Pixel(Render(p, 8), 80, 45));
        Assert.Equal((255, 0, 0), Pixel(Render(p, 15), 80, 45));
        Assert.Equal(3, p.MainSequence.Tracks[Video1].Clips.Count);
        Assert.Equal(3, p.MainSequence.Tracks[Video2].Clips.Count);
        Assert.Throws<EditRejectedException>(() => new MulticamSwitchCommand(p.MainSequence.Id, 2, S(15)).Apply(p));
    }

    // ---------------------------------------------------------------- 360°

    [Fact]
    public void Reframe_360_looks_at_the_chosen_direction()
    {
        // Equirretangular 400×200: metade esquerda (longitude < 0) azul, metade direita vermelha.
        int w = 400, h = 200;
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                if (x < w / 2) px[i] = 255; else px[i + 2] = 255;
                px[i + 3] = 255;
            }
        var (right, rw) = EffectProcessor.Reframe360Fx(px, w, h, 90, 0, 0, 60, 16 / 9.0);
        Assert.Equal(356, rw);
        Assert.Equal(255, right[(100 * rw + rw / 2) * 4 + 2]); // olhando para a direita: vermelho
        var (left, lw) = EffectProcessor.Reframe360Fx(px, w, h, -90, 0, 0, 60, 9 / 16.0);
        Assert.Equal(112, lw);
        Assert.Equal(255, left[(100 * lw + lw / 2) * 4]); // olhando para a esquerda: azul
    }

    [Fact]
    public void Reframe_360_effect_changes_the_layer_aspect()
    {
        var e = EffectCatalog.Create(EffectCatalog.Reframe360);
        Assert.False(EffectCatalog.IsNeutral(e));
        var src = new BgraImage(400, 200, new byte[400 * 200 * 4]);
        var (img, _) = EffectProcessor.Apply(src, [EffectCatalog.WithValue(e, "aspect", 2)], new EffectContext(1, 0));
        Assert.Equal(200, img.Width); // 1:1
        Assert.Equal(200, img.Height);
    }

    // ---------------------------------------------------------------- gravação de tela

    [Fact]
    public void Recorder_parses_directshow_devices()
    {
        const string output = """
            [in#0 @ 000001] "Integrated Camera" (video)
            [in#0 @ 000001]   Alternative name "@device_pnp_\\?\usb#vid"
            [in#0 @ 000001] "Microfone (Realtek(R) Audio)" (audio)
            [in#0 @ 000001]   Alternative name "@device_cm_{33D9A762}"
            """;
        var devices = WindowsScreenRecorder.ParseDevices(output);
        Assert.Equal(["Integrated Camera"], devices.Cameras);
        Assert.Equal(["Microfone (Realtek(R) Audio)"], devices.Microphones);
    }

    [Fact]
    public void Recorder_builds_screen_webcam_and_microphone_arguments()
    {
        var args = WindowsScreenRecorder.BuildArguments(new ScreenRecordingOptions(@"C:\v\g.mp4", true, "Cam", "Mic"), 1920);
        var text = string.Join(' ', args);
        Assert.Contains("-f gdigrab", text);
        Assert.Contains("video=Cam", text);
        Assert.Contains("audio=Mic", text);
        Assert.Contains("[1:v]scale=192:-2[cam]", text);
        Assert.DoesNotContain("-offset_x", text);
        Assert.Contains("overlay=main_w-overlay_w-24:main_h-overlay_h-24", text);
        Assert.Contains("-map 2:a", text);
        Assert.Equal(@"C:\v\g.mp4", args[^1]);

        // Um monitor só: recorte da área de trabalho virtual e webcam proporcional a ele.
        var second = new ScreenArea(2, -1280, 0, 1280, 1024, false);
        var one = string.Join(' ', WindowsScreenRecorder.BuildArguments(new ScreenRecordingOptions("o.mp4", true, "Cam", null, Area: second), second.Width));
        Assert.Contains("-offset_x -1280 -offset_y 0 -video_size 1280x1024 -i desktop", one);
        Assert.Contains("[1:v]scale=160:-2[cam]", one);

        // Uma janela: a área dela na tela (com sombra recortada pela tela e tamanho par), nunca title= (sai preto).
        Assert.Equal(new ScreenArea(0, 0, 0, 800, 600, false), WindowsScreenRecorder.WindowArea((-7, -3, 801, 601), (0, 0, 1920, 1080)));
        Assert.Equal(new ScreenArea(0, 101, 51, 300, 200, false), WindowsScreenRecorder.WindowArea((101, 51, 402, 252), (0, 0, 1920, 1080)));
        Assert.Null(WindowsScreenRecorder.WindowArea((2000, 0, 2400, 300), (0, 0, 1920, 1080)));
        var win = string.Join(' ', WindowsScreenRecorder.BuildArguments(new ScreenRecordingOptions("o.mp4", true, null, null,
            Area: new ScreenArea(0, 101, 51, 300, 200, false), Window: new CaptureWindow(42, "Bloco de Notas", "notepad")), 300));
        Assert.Contains("-offset_x 101 -offset_y 51 -video_size 300x200 -i desktop", win);
        Assert.DoesNotContain("title=", win);

        var camOnly = string.Join(' ', WindowsScreenRecorder.BuildArguments(new ScreenRecordingOptions("o.mp4", false, "Cam", null), 1920));
        Assert.DoesNotContain("gdigrab", camOnly);
        Assert.Contains("[0:v]scale=trunc", camOnly);
        Assert.Throws<InvalidOperationException>(() => WindowsScreenRecorder.BuildArguments(new ScreenRecordingOptions("o.mp4", false, null, "Mic"), 1920));
    }

    // ---------------------------------------------------------------- pacotes

    private sealed class TempPaths(string root) : IAppPaths
    {
        public string RoamingData => Path.Combine(root, "r");
        public string LocalData => Path.Combine(root, "l");
        public string Logs => Path.Combine(root, "logs");
        public string Cache => Path.Combine(root, "c");
        public string Autosave => Path.Combine(root, "a");
        public string AiModels => Path.Combine(root, "m");
        public string Backups => Path.Combine(root, "b");
        public string SettingsFile => Path.Combine(root, "s.json");
    }

    [Fact]
    public void Content_pack_installs_only_known_files_and_round_trips_titles()
    {
        var root = Path.Combine(Path.GetTempPath(), "med-pacotes-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(root);
            var zipPath = Path.Combine(root, "Festa Junina.medpack");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                void Add(string name, string content)
                {
                    using var w = new StreamWriter(zip.CreateEntry(name).Open());
                    w.Write(content);
                }
                Add("Festa/pacote.json", """{"nome":"Festa Junina"}""");
                Add("Festa/figurinhas/balão.png", "png");
                Add("Festa/figurinhas/script.exe", "x");
                Add("Festa/../../fora.png", "x");
                Add("Festa/modelos/Arraial.med", "zip");
            }
            var store = new ContentPackStore(new TempPaths(root), new DomainJson()) { BuiltInRoot = Path.Combine(root, "sem-incluidos") };
            var info = store.Install(zipPath);
            Assert.Equal("festa-junina", info.Id);
            Assert.Equal("Festa Junina", info.Name);
            Assert.Equal(1, info.Stickers);
            Assert.Equal(1, info.Templates);
            Assert.False(File.Exists(Path.Combine(root, "fora.png")));
            Assert.DoesNotContain(Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories), _ => true);
            Assert.Single(store.Stickers());

            var text = new Clip
            {
                Id = Guid.NewGuid(),
                Start = S(4),
                Duration = S(3),
                Text = "Olá",
                TextFormat = TextFormat.Default,
                LinkId = Guid.NewGuid(),
            };
            var saved = store.SaveTitle(text, "Abertura");
            var loaded = store.LoadTitle(saved.Path);
            Assert.Equal("Olá", loaded.Text);
            Assert.Equal(MediaTime.Zero, loaded.Start);
            Assert.Equal(S(3), loaded.Duration);
            Assert.NotEqual(text.Id, loaded.Id);
            Assert.Null(loaded.LinkId);
            Assert.Contains(store.Packs(), p => p.IsUserPack && p.Titles == 1);

            var exported = Path.Combine(root, "copia.medpack");
            store.Export(info.Id, exported);
            store.Remove(info.Id);
            Assert.Empty(store.Stickers());
            Assert.Equal(1, store.Install(exported).Stickers);

            using (var bad = ZipFile.Open(Path.Combine(root, "vazio.zip"), ZipArchiveMode.Create)) bad.CreateEntry("leia.txt");
            Assert.Throws<InvalidDataException>(() => store.Install(Path.Combine(root, "vazio.zip")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Overlay_goes_to_a_free_image_track_or_a_new_one()
    {
        var img = Asset(MediaKind.Image, 0);
        var p = NewProject(img);
        var cmd = new AddOverlayCommand(p.MainSequence.Id, img.Id, S(1), S(3), "Figurinhas") { Scale = 0.4 };
        p = cmd.Apply(p);
        var track = p.MainSequence.Tracks.Single(t => t.Clips.Any(c => c.Id == cmd.VideoClipId));
        Assert.Equal(TrackKind.Image, track.Kind);
        Assert.Equal("Figurinhas", track.Name);
        Assert.Equal(S(3), track.Clips[0].Duration);
        Assert.Equal(0.4, track.Clips[0].Transform.ScaleX);
        // A segunda no mesmo instante vai para outra trilha (a primeira está ocupada).
        var cmd2 = new AddOverlayCommand(p.MainSequence.Id, img.Id, S(1), S(3));
        p = cmd2.Apply(p);
        Assert.Equal(2, p.MainSequence.Tracks.Count(t => t.Kind == TrackKind.Image));
    }

    [Fact]
    public void Built_in_sticker_pack_ships_96_stickers_and_cannot_be_removed()
    {
        var resources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "resources", "stickers"));
        var root = Path.Combine(Path.GetTempPath(), "med-pacotes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ContentPackStore(new TempPaths(root), new DomainJson()) { BuiltInRoot = resources };
            var basic = Assert.Single(store.Packs());
            Assert.Equal("basico", basic.Id);
            Assert.Equal("Básico", basic.Name);
            Assert.True(basic.IsBuiltIn);
            Assert.Equal(96, basic.Stickers);
            Assert.Equal(96, store.Stickers().Count);
            Assert.Throws<InvalidOperationException>(() => store.Remove("basico"));

            // Exportar funciona e um pacote instalado com o mesmo id tem precedência sobre o incluído.
            var exported = Path.Combine(root, "basico.medpack");
            Directory.CreateDirectory(root);
            store.Export("basico", exported);
            var installed = store.Install(exported);
            Assert.False(installed.IsBuiltIn);
            Assert.False(Assert.Single(store.Packs()).IsBuiltIn);
            store.Remove("basico");
            Assert.True(Assert.Single(store.Packs()).IsBuiltIn);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

}
