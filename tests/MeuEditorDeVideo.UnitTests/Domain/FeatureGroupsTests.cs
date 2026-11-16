using System.Collections.Immutable;
using System.Text.Json;
using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Effects;
using MeuEditorDeVideo.RenderingEngine;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.UnitTests.Domain;

/// <summary>Curva de velocidade, desfoque de movimento, batidas, voz, cor, lente, ruído, visualizador e análise de mídia.</summary>
public sealed class FeatureGroupsTests
{
    private const int Video1 = 0;
    private const int Audio1 = 2;

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset Asset(MediaKind kind, double seconds, int w = 1920, int h = 1080, bool audio = false) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Name = "m",
        OriginalPath = @"C:\m\m",
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

    // ---------------------------------------------------------------- curva de velocidade

    [Fact]
    public void Speed_ramp_keeps_the_source_range_and_is_monotonic()
    {
        var ramp = SpeedRamps.Presets.First(p => p.Name == "Montagem").Points;
        var clip = NewClip(Asset(MediaKind.Video, 20), 0, 10) with { SourceIn = S(2), SpeedRamp = ramp };
        Assert.Equal(S(2), clip.SourceTimeAt(S(0)));
        Assert.InRange(clip.SourceTimeAt(S(10)).Ticks, clip.SourceOut.Ticks - 2, clip.SourceOut.Ticks);
        var last = MediaTime.Zero;
        for (var t = 0.0; t <= 10; t += 0.1)
        {
            var s = clip.SourceTimeAt(S(t));
            Assert.True(s >= last);
            last = s;
        }
        // No trecho lento (40–60 %) anda bem menos que no rápido (0–30 %).
        var slow = (clip.SourceTimeAt(S(6)) - clip.SourceTimeAt(S(4))).Ticks;
        var fast = (clip.SourceTimeAt(S(2)) - clip.SourceTimeAt(S(0))).Ticks;
        Assert.True(fast > slow * 3);
    }

    [Fact]
    public void Speed_ramp_progress_is_linear_for_a_flat_curve()
    {
        var flat = ImmutableList.Create(new SpeedPoint(0, 2), new SpeedPoint(1, 2));
        Assert.Equal(0.25, SpeedRamps.Progress(flat, 0.25), 9);
        var accel = SpeedRamps.Presets.First(p => p.Name == "Acelerar").Points;
        Assert.True(SpeedRamps.Progress(accel, 0.5) < 0.5);
        Assert.Equal(1, SpeedRamps.RelativeAt(flat, 0.3), 9);
    }

    [Fact]
    public void Speed_ramp_command_applies_to_linked_clips_and_mutes_the_audio()
    {
        var asset = Asset(MediaKind.Video, 20, audio: true);
        var link = Guid.NewGuid();
        var v = NewClip(asset, 0, 10, link);
        var a = NewClip(asset, 0, 10, link);
        var p = Place(Place(NewProject(asset), Video1, v), Audio1, a);
        p = new SetSpeedRampCommand(p.MainSequence.Id, v.Id, SpeedRamps.Presets[0].Points, "Montagem").Apply(p);
        Assert.All(p.MainSequence.Tracks.SelectMany(t => t.Clips), c => Assert.Equal(6, c.SpeedRamp.Count));
        Assert.Empty(TimelineEvaluator.AudioIn(p, p.MainSequence, S(0), S(5)));

        p = new SetSpeedRampCommand(p.MainSequence.Id, v.Id, []).Apply(p);
        Assert.All(p.MainSequence.Tracks.SelectMany(t => t.Clips), c => Assert.Empty(c.SpeedRamp));
    }

    [Fact]
    public void Splitting_a_ramped_clip_returns_to_constant_speed()
    {
        var clip = NewClip(Asset(MediaKind.Video, 20), 0, 10) with { SpeedRamp = SpeedRamps.Presets[0].Points };
        var (left, right) = TimelineOps.Split(clip, S(4), Guid.NewGuid(), null)!.Value;
        Assert.Empty(left.SpeedRamp);
        Assert.Empty(right.SpeedRamp);
        Assert.Equal(S(4), right.SourceIn);
    }

    // ---------------------------------------------------------------- desfoque de movimento

    [Fact]
    public void Motion_trail_exists_only_with_blur_and_animation()
    {
        var asset = Asset(MediaKind.Video, 10);
        var p = NewProject(asset);
        var clip = NewClip(asset, 0, 4);
        Assert.Empty(TimelineEvaluator.Trail(p, clip with { MotionBlur = 1 }, S(1)));

        var moving = ClipAnimation.ToggleKeyframe(clip, AnimatedProperty.X, S(0));
        moving = ClipAnimation.SetValue(ClipAnimation.ToggleKeyframe(moving, AnimatedProperty.X, S(2)), AnimatedProperty.X, S(2), 600);
        Assert.Empty(TimelineEvaluator.Trail(p, moving, S(1)));
        var trail = TimelineEvaluator.Trail(p, moving with { MotionBlur = 1 }, S(1));
        Assert.Equal(TimelineEvaluator.MotionBlurSamples - 1, trail.Count);
        Assert.True(trail[0].X < trail[^1].X); // mais antiga primeiro
    }

    // ---------------------------------------------------------------- máscara de cor

    [Fact]
    public void Masked_mix_keeps_the_original_outside_the_mask()
    {
        var a = new BgraImage(2, 1, [10, 10, 10, 255, 10, 10, 10, 255]);
        var b = new BgraImage(2, 1, [200, 200, 200, 255, 200, 200, 200, 255]);
        var mixed = FrameRenderer.MaskedMix(a, b, [255, 0]);
        Assert.Equal(200, mixed.Pixels[0]);
        Assert.Equal(10, mixed.Pixels[4]);
    }

    [Fact]
    public void Effects_only_masks_do_not_cut_the_layer()
    {
        var mask = new Mask { Id = Guid.NewGuid(), Points = [new MaskPoint(0, 0), new MaskPoint(0.5, 1)], EffectsOnly = true };
        var json = JsonSerializer.Serialize(mask);
        Assert.True(JsonSerializer.Deserialize<Mask>(json)!.EffectsOnly);
    }

    // ---------------------------------------------------------------- cor, lente e ruído

    [Fact]
    public void Color_match_moves_the_statistics_to_the_reference()
    {
        static BgraImage Gradient(int lo, int hi)
        {
            var px = new byte[256 * 4];
            for (var i = 0; i < 256; i++)
            {
                var v = (byte)(lo + (hi - lo) * i / 255);
                px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = v;
                px[i * 4 + 3] = 255;
            }
            return new BgraImage(256, 1, px);
        }
        var dark = Gradient(20, 120);
        var bright = Gradient(100, 240);
        var match = EffectProcessor.MatchColor(EffectProcessor.ColorStats(dark), EffectProcessor.ColorStats(bright));
        var effect = EffectCatalog.Create(EffectCatalog.ColorMatch);
        string[] ch = ["r", "g", "b"];
        for (var i = 0; i < 3; i++)
            effect = EffectCatalog.WithValue(EffectCatalog.WithValue(effect, ch[i] + ".gain", match[i].Gain), ch[i] + ".offset", match[i].Offset);
        Assert.False(EffectCatalog.IsNeutral(effect));
        var result = EffectProcessor.Apply(dark, [effect], new EffectContext(1, 0)).Image;
        var (mean, _) = EffectProcessor.ColorStats(result);
        var (target, _) = EffectProcessor.ColorStats(bright);
        Assert.Equal(target[0], mean[0], 1);
    }

    [Theory]
    [InlineData(EffectCatalog.ColorMatch)]
    [InlineData(EffectCatalog.Lens)]
    [InlineData(EffectCatalog.AudioVisualizer)]
    [InlineData(EffectCatalog.VoiceChanger)]
    public void New_effects_are_neutral_by_default(string type) => Assert.True(EffectCatalog.IsNeutral(EffectCatalog.Create(type)));

    [Fact]
    public void Lens_correction_keeps_the_center_and_moves_the_corners()
    {
        var px = new byte[64 * 64 * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)(i / 4 % 64 * 4); px[i + 3] = 255; }
        var img = new BgraImage(64, 64, px);
        var lens = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Lens), "distortion", -80);
        var result = EffectProcessor.Apply(img, [lens], new EffectContext(1, 0)).Image;
        var center = (32 * 64 + 32) * 4;
        Assert.InRange(result.Pixels[center], px[center] - 8, px[center] + 8);
        Assert.NotEqual(px[(2 * 64 + 2) * 4], result.Pixels[(2 * 64 + 2) * 4]);
    }

    [Fact]
    public void Video_denoise_smooths_noise_but_keeps_strong_edges()
    {
        var rnd = new Random(3);
        var px = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var o = (y * 64 + x) * 4;
                var v = (x < 32 ? 40 : 220) + rnd.Next(-12, 13);
                px[o] = px[o + 1] = px[o + 2] = (byte)v;
                px[o + 3] = 255;
            }
        var img = new BgraImage(64, 64, px);
        var fx = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.VideoDenoise), "strength", 100);
        var result = EffectProcessor.Apply(img, [fx], new EffectContext(1, 0)).Image;
        double Var(byte[] p, int x0)
        {
            var vals = Enumerable.Range(8, 48).SelectMany(y => Enumerable.Range(x0, 16).Select(x => (double)p[(y * 64 + x) * 4])).ToList();
            var m = vals.Average();
            return vals.Average(v => (v - m) * (v - m));
        }
        Assert.True(Var(result.Pixels, 4) < Var(px, 4) / 2);
        Assert.True(result.Pixels[(32 * 64 + 40) * 4] > 180); // o lado claro continua claro junto à borda
    }

    // ---------------------------------------------------------------- visualizador

    [Fact]
    public void Visualizer_levels_follow_the_waveform()
    {
        var max = new float[100];
        var min = new float[100];
        for (var i = 50; i < 100; i++) { max[i] = 0.8f; min[i] = -0.8f; }
        var data = new WaveformData(50, min, max, S(2));
        Assert.All(FrameRenderer.AudioVisualizerLevels(data, S(0.3), 16), l => Assert.Equal(0, l));
        Assert.All(FrameRenderer.AudioVisualizerLevels(data, S(1.5), 16), l => Assert.True(l > 0.8));

        var img = new BgraImage(32, 32, new byte[32 * 32 * 4]);
        var viz = EffectCatalog.Create(EffectCatalog.AudioVisualizer, Guid.NewGuid().ToString());
        var drawn = EffectProcessor.DrawVisualizer(img, Enumerable.Repeat(1f, 8).ToList(), viz);
        Assert.True(drawn.Pixels[(31 * 32 + 2) * 4 + 3] > 0);
    }

    // ---------------------------------------------------------------- áudio: batidas e voz

    [Fact]
    public void Beat_detector_finds_regular_clicks()
    {
        const int rate = 48000;
        var seconds = 4;
        var s = new float[rate * seconds * 2];
        var rnd = new Random(1);
        for (var i = 0; i < s.Length; i++) s[i] = (float)(rnd.NextDouble() - 0.5) * 0.01f;
        var beats = new[] { 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5 };
        foreach (var b in beats)
        {
            var start = (int)(b * rate);
            for (var k = 0; k < 2400; k++)
            {
                var v = (float)(0.9 * Math.Exp(-k / 400.0) * Math.Sin(2 * Math.PI * 80 * k / rate));
                s[(start + k) * 2] += v;
                s[(start + k) * 2 + 1] += v;
            }
        }
        var found = BeatDetector.Detect(s, 0.5).Select(f => f / (double)rate).ToList();
        Assert.Equal(beats.Length, found.Count);
        for (var i = 0; i < beats.Length; i++) Assert.InRange(found[i], beats[i] - 0.03, beats[i] + 0.03);
    }

    [Fact]
    public void Voice_changer_shifts_the_pitch_an_octave_up()
    {
        const int rate = 48000;
        var frames = rate;
        var s = new float[frames * 2];
        for (var i = 0; i < frames; i++) s[i * 2] = s[i * 2 + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 220 * i / rate));
        var voice = new VoiceChangerProcessor();
        voice.Configure(EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.VoiceChanger), "pitch", 12));
        voice.Process(s);
        // Conta cruzamentos por zero na segunda metade (depois da janela inicial).
        var crossings = 0;
        for (var i = frames / 2 + 1; i < frames; i++)
            if (s[(i - 1) * 2] < 0 && s[i * 2] >= 0) crossings++;
        Assert.InRange(crossings, 200, 245); // ~440 Hz em meio segundo
    }

    [Fact]
    public void Voice_presets_override_the_manual_controls()
    {
        var e = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.VoiceChanger), "preset", 5);
        Assert.Equal((0, 0.8), EffectCatalog.VoiceSettings(e));
        Assert.False(EffectCatalog.IsNeutral(e));
    }

    // ---------------------------------------------------------------- comandos

    [Fact]
    public void Add_markers_skips_existing_frames()
    {
        var p = NewProject();
        var seq = p.MainSequence.Id;
        p = new AddMarkersCommand(seq, [S(1), S(2), S(1)], "Batida").Apply(p);
        Assert.Equal(2, p.MainSequence.Markers.Count);
        Assert.Throws<EditRejectedException>(() => new AddMarkersCommand(seq, [S(2)]).Apply(p));
    }

    [Fact]
    public void Split_at_times_cuts_the_clip_at_every_beat()
    {
        var asset = Asset(MediaKind.Video, 20);
        var clip = NewClip(asset, 0, 10);
        var p = Place(NewProject(asset), Video1, clip);
        p = new SplitAtTimesCommand(p.MainSequence.Id, [S(2), S(5), S(8), S(12)], [clip.Id]).Apply(p);
        var clips = p.MainSequence.Tracks[Video1].Clips.OrderBy(c => c.Start).ToList();
        Assert.Equal(4, clips.Count);
        Assert.Equal(new[] { S(0), S(2), S(5), S(8) }, clips.Select(c => c.Start));
        Assert.Equal(clip.Id, clips[0].Id);
    }

    [Fact]
    public void Narration_goes_to_a_free_audio_track_or_a_new_one()
    {
        var music = Asset(MediaKind.Audio, 30, 0, 0);
        var voice = Asset(MediaKind.Audio, 3, 0, 0);
        var p = Place(NewProject(music, voice), Audio1, NewClip(music, 0, 30));
        var tracks = p.MainSequence.Tracks.Count;
        p = new AddNarrationCommand(p.MainSequence.Id, voice.Id, S(5)).Apply(p);
        Assert.Equal(tracks + 1, p.MainSequence.Tracks.Count);
        var narration = p.MainSequence.Tracks[0];
        Assert.Equal(TrackKind.Audio, narration.Kind);
        Assert.Equal(S(5), narration.Clips.Single().Start);

        // A trilha nova está livre depois de 8 s: a próxima narração entra nela.
        p = new AddNarrationCommand(p.MainSequence.Id, voice.Id, S(10)).Apply(p);
        Assert.Equal(tracks + 1, p.MainSequence.Tracks.Count);
    }

    [Fact]
    public void Media_tags_describe_format_and_are_saved()
    {
        var vertical = Asset(MediaKind.Video, 8, 1080, 1920, audio: true);
        var tags = MediaTags.Basic(vertical);
        Assert.Contains("vertical", tags);
        Assert.Contains("Full HD", tags);
        Assert.Contains("curto", tags);
        Assert.Contains("com som", tags);

        var p = NewProject(vertical);
        p = new SetMediaTagsCommand(new Dictionary<Guid, ImmutableList<string>> { [vertical.Id] = ["fala", "pessoa"] }).Apply(p);
        Assert.Equal(["fala", "pessoa"], p.Media[0].Tags);
    }
}
