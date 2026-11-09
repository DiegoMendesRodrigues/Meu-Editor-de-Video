using System.IO;
using System.Collections.Immutable;
using System.Text.Json;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.ProjectEngine;
using MeuEditorDeVideo.ProjectEngine.Format;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Visual;

public sealed class VisualSerializationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "visual-serial-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MedProjectStore _store;

    public VisualSerializationTests()
    {
        var paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        paths.EnsureCreated();
        _store = new MedProjectStore(paths, NullLogger<MedProjectStore>.Instance);
    }

    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static Project BuildProject(MediaAsset asset)
    {
        var blur = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Blur), "radius", 12);
        var lut = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Lut, @"C:\luts\filme.cube"), "intensity", 65);
        var key = EffectCatalog.WithColor(EffectCatalog.Create(EffectCatalog.ChromaKey), "keyColor", 0.2, 0.4, 0.9) with { Enabled = false };
        var curve = EffectCatalog.WithCurve(EffectCatalog.Create(EffectCatalog.Curves), "master", new (double X, double Y)[] { (0, 0.1), (1, 0.9) });
        var wheel = EffectCatalog.WithWheel(EffectCatalog.Create(EffectCatalog.Wheels), "gamma", 0.3, -0.2, 0.1);

        var animated = ImmutableDictionary<string, ImmutableList<Keyframe>>.Empty
            .Add(AnimatedProperty.X, ImmutableList.Create(new Keyframe(S(0), 0, Easing.Linear), new Keyframe(S(2), 150, Easing.Hold)))
            .Add(AnimatedProperty.Opacity, ImmutableList.Create(new Keyframe(S(1), 0.25, Easing.EaseOut)));

        var first = new Clip
        {
            Id = Guid.NewGuid(),
            MediaAssetId = asset.Id,
            Start = MediaTime.Zero,
            Duration = S(6),
            SourceIn = S(1),
            Speed = new Rational(1, 2),
            Opacity = 0.8,
            Volume = 0.5,
            LinkId = Guid.NewGuid(),
            Transform = new Transform { X = 12.5, Y = -40, ScaleX = 1.5, ScaleY = 1.5, RotationDegrees = 15, FlipHorizontal = true, CropLeft = 0.1 },
            Effects = [blur, lut, key, curve, wheel],
            BlendMode = BlendMode.Screen,
            Keyframes = animated,
            FreezeAt = S(1.5),
            FrameBlending = true,
            FadeIn = S(0.5),
            FadeOut = S(0.25),
        };
        var second = new Clip
        {
            Id = Guid.NewGuid(),
            MediaAssetId = asset.Id,
            Start = S(6),
            Duration = S(4),
            SourceIn = S(3),
        };
        var transition = new Transition
        {
            Id = Guid.NewGuid(),
            FromClipId = first.Id,
            ToClipId = second.Id,
            Kind = TransitionKind.WipeLeft,
            Duration = S(1),
        };

        var project = Project.CreateNew("Visual", new ProjectSettings { Width = 1280, Height = 720, FrameRate = FrameRate.Fps25 }, StorageMode.Linked, Now)
            with { Media = [asset] };
        var seq = project.MainSequence;
        var track = seq.Tracks[0] with { Clips = [first, second], Transitions = [transition] };
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(0, track) });
    }

    private static MediaAsset VideoAsset(string path) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = Path.GetFileName(path),
        OriginalPath = path,
        FileSize = 2048,
        LastWriteUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        Fingerprint = "fingerprint-visual",
        Duration = S(10),
        Width = 1920,
        Height = 1080,
        FrameRate = FrameRate.Fps30,
        Info = new MediaTechnicalInfo { AudioCodec = "aac" },
    };

    private static List<KeyValuePair<string, double>> Pairs(EffectInstance effect) =>
        effect.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Projeto_com_efeitos_keyframes_transicao_e_modos_sobrevive_a_salvar_e_carregar()
    {
        var mediaDir = Path.Combine(_root, "midias");
        Directory.CreateDirectory(mediaDir);
        var media = Path.Combine(mediaDir, "clip.mp4");
        await File.WriteAllBytesAsync(media, new byte[2048]);
        var project = BuildProject(VideoAsset(media));
        var target = Path.Combine(_root, "visual.med");

        await _store.SaveAsync(new SaveRequest(project, target, IncludeMedia: false, new Dictionary<Guid, string>()));
        var loaded = (await _store.LoadAsync(target)).Project;

        var expectedTrack = project.MainSequence.Tracks[0];
        var actualTrack = loaded.MainSequence.Tracks[0];
        Assert.Equal(expectedTrack.Transitions.Single(), actualTrack.Transitions.Single());

        foreach (var expected in expectedTrack.Clips)
        {
            var actual = actualTrack.Clips.Single(c => c.Id == expected.Id);
            Assert.Equal(expected.Start, actual.Start);
            Assert.Equal(expected.Duration, actual.Duration);
            Assert.Equal(expected.SourceIn, actual.SourceIn);
            Assert.Equal(expected.Speed, actual.Speed);
            Assert.Equal(expected.Opacity, actual.Opacity);
            Assert.Equal(expected.Volume, actual.Volume);
            Assert.Equal(expected.Transform, actual.Transform);
            Assert.Equal(expected.LinkId, actual.LinkId);
            Assert.Equal(expected.BlendMode, actual.BlendMode);
            Assert.Equal(expected.FreezeAt, actual.FreezeAt);
            Assert.Equal(expected.FrameBlending, actual.FrameBlending);
            Assert.Equal(expected.FadeIn, actual.FadeIn);
            Assert.Equal(expected.FadeOut, actual.FadeOut);

            Assert.Equal(expected.Effects.Count, actual.Effects.Count);
            for (var i = 0; i < expected.Effects.Count; i++)
            {
                var e = expected.Effects[i];
                var a = actual.Effects[i];
                Assert.Equal(e.Id, a.Id);
                Assert.Equal(e.Type, a.Type);
                Assert.Equal(e.Enabled, a.Enabled);
                Assert.Equal(e.Resource, a.Resource);
                Assert.Equal(Pairs(e), Pairs(a));
            }

            Assert.Equal(expected.Keyframes.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keyframes.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var key in expected.Keyframes.Keys)
                Assert.Equal(expected.Keyframes[key], actual.Keyframes[key]);
        }
    }

    [Fact]
    public void Tempos_sao_gravados_como_flicks_e_velocidade_como_fracao()
    {
        var project = BuildProject(VideoAsset(@"C:\m\clip.mp4"));
        var clip = project.MainSequence.Tracks[0].Clips[0];

        var json = JsonSerializer.Serialize(clip, MedJson.Options);

        // 1,5 s = 1 058 400 000 flicks (705 600 000 por segundo).
        Assert.Contains("\"freezeAt\": 1058400000", json);
        Assert.Contains("\"speed\": \"1/2\"", json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
