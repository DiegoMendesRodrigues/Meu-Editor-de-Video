using System.Collections.Immutable;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class AudioMixerLevelTests
{
    private const int Audio1 = 2;
    private const int Ch = AudioFormat.Channels;
    private const int Rate = AudioFormat.SampleRate;

    /// <summary>Fonte determinística: a amostra é função do índice absoluto do quadro na mídia (mesma em qualquer leitura).</summary>
    private sealed class FunctionSource(Func<long, float> f) : IAudioSampleSource
    {
        public int Read(MediaAsset asset, string path, MediaTime sourceTime, Span<float> destination)
        {
            var first = AudioFormat.ToFrames(sourceTime);
            var frames = destination.Length / Ch;
            for (var i = 0; i < frames; i++)
            {
                var v = f(first + i);
                destination[i * Ch] = v;
                destination[i * Ch + 1] = v;
            }
            return frames;
        }

        public void Trim(IReadOnlySet<Guid> inUse) { }

        public void Dispose() { }
    }

    private static float Sine440(long n) => (float)(0.3 * Math.Sin(2 * Math.PI * 440.0 * n / Rate));

    private static MediaTime T(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset MediaWithAudio() => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = "v.mp4",
        OriginalPath = @"C:\m\v.mp4",
        Duration = T(10),
        Info = new MediaTechnicalInfo { AudioCodec = "aac" },
    };

    private static Clip MakeClip(MediaAsset asset, double start, double duration) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = asset.Id,
        Start = T(start),
        Duration = T(duration),
        Speed = Rational.One,
    };

    private static Track AudioTrack(string name) => new() { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = name };

    private static Project Setup(MediaAsset asset) =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [asset] };

    private static Project PlaceOn(Project project, int trackIndex, params Clip[] clips)
    {
        var seq = project.MainSequence;
        var track = seq.Tracks[trackIndex] with { Clips = [.. clips] };
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(trackIndex, track) });
    }

    private static Project AppendTrack(Project project, Track track)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.Add(track) });
    }

    private static Project EditTrack(Project project, int trackIndex, Func<Track, Track> change)
    {
        var seq = project.MainSequence;
        return project.WithSequence(seq with { Tracks = seq.Tracks.SetItem(trackIndex, change(seq.Tracks[trackIndex])) });
    }

    private static IReadOnlyDictionary<Guid, string> PathsFor(MediaAsset asset) =>
        new Dictionary<Guid, string> { [asset.Id] = asset.OriginalPath };

    private static float[] Mix(AudioMixer mixer, Project project, IReadOnlyDictionary<Guid, string> paths,
        long fromFrame, int frames, double masterVolume = 1)
    {
        var buffer = new float[frames * Ch];
        mixer.Mix(project, project.MainSequence, paths, fromFrame, buffer, masterVolume);
        return buffer;
    }

    private static void AssertFlat(float[] buffer, int fromFrame, int toFrame, float expected)
    {
        for (var i = fromFrame * Ch; i < toFrame * Ch; i++)
            Assert.InRange(buffer[i], expected - 1e-6f, expected + 1e-6f);
    }

    // ---------------------------------------------------------------- Trilhas: volume, panorâmica, solo

    [Fact]
    public void Track_volume_scales_the_track_output()
    {
        var media = MediaWithAudio();
        var p = EditTrack(PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1)), Audio1, t => t with { Volume = 0.5 });

        var buffer = Mix(new AudioMixer(new FunctionSource(_ => 0.5f)), p, PathsFor(media), 0, 4800);

        AssertFlat(buffer, 0, 4800, 0.25f);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.25)]   // tudo à direita
    [InlineData(-1.0, 0.25, 0.0)]  // tudo à esquerda
    public void Full_pan_silences_the_opposite_channel(double pan, double left, double right)
    {
        var media = MediaWithAudio();
        var p = EditTrack(PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1)), Audio1, t => t with { Pan = pan });

        var buffer = Mix(new AudioMixer(new FunctionSource(_ => 0.25f)), p, PathsFor(media), 0, 4800);

        for (var f = 0; f < 4800; f++)
        {
            Assert.InRange(buffer[f * Ch], left - 1e-6, left + 1e-6);
            Assert.InRange(buffer[f * Ch + 1], right - 1e-6, right + 1e-6);
        }
    }

    [Fact]
    public void Solo_track_silences_the_other_audio_tracks()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        p = AppendTrack(p, AudioTrack("Áudio 2"));
        p = PlaceOn(p, p.MainSequence.Tracks.Count - 1, MakeClip(media, 0, 0.1));
        var soloId = p.MainSequence.Tracks[Audio1].Id;
        p = EditTrack(p, Audio1, t => t with { IsSolo = true });
        var mixer = new AudioMixer(new FunctionSource(_ => 0.25f));

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800);

        AssertFlat(buffer, 0, 4800, 0.25f); // só uma trilha soa, não 0,5
        var entry = Assert.Single(mixer.LastLevels.Tracks);
        Assert.Equal(soloId, entry.Key);
    }

    [Fact]
    public void Levels_report_track_peak_and_master_peak_of_the_output()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        var mixer = new AudioMixer(new FunctionSource(Sine440));

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800);

        var peak = buffer.Max(v => Math.Abs(v));
        var track = mixer.LastLevels.Tracks[p.MainSequence.Tracks[Audio1].Id];
        Assert.InRange(track.PeakL, peak - 1e-6f, peak + 1e-6f);
        Assert.InRange(mixer.LastLevels.Master.PeakL, peak - 1e-6f, peak + 1e-6f);
        Assert.InRange(mixer.LastLevels.Master.PeakR, peak - 1e-6f, peak + 1e-6f);
        Assert.True(track.RmsL > 0.1f);
    }

    // ---------------------------------------------------------------- Clipe: envelope de volume

    [Fact]
    public void Volume_keyframes_ramp_the_clip_gain()
    {
        var media = MediaWithAudio();
        var clip = MakeClip(media, 0, 0.1) with
        {
            Keyframes = ImmutableDictionary<string, ImmutableList<Keyframe>>.Empty.Add(AnimatedProperty.Volume,
                ImmutableList.Create(new Keyframe(T(0), 0, Easing.Linear), new Keyframe(T(0.1), 1, Easing.Linear))),
        };
        var p = PlaceOn(Setup(media), Audio1, clip);

        var buffer = Mix(new AudioMixer(new FunctionSource(_ => 1f)), p, PathsFor(media), 0, 4800);

        Assert.InRange(buffer[0], 0.0, 0.01);
        Assert.InRange(buffer[2400 * Ch], 0.49, 0.51); // metade do caminho → ganho 0,5
        for (var f = 1; f < 4800; f++)
            Assert.True(buffer[f * Ch] >= buffer[(f - 1) * Ch] - 1e-6f, $"rampa não monotônica no quadro {f}");
    }

    [Fact]
    public void Fade_in_starts_from_silence_and_reaches_full_gain()
    {
        var media = MediaWithAudio();
        var clip = MakeClip(media, 0, 0.1) with { FadeIn = T(0.05) };
        var p = PlaceOn(Setup(media), Audio1, clip);

        var buffer = Mix(new AudioMixer(new FunctionSource(_ => 0.5f)), p, PathsFor(media), 0, 4800);

        Assert.Equal(0f, buffer[0]);
        Assert.InRange(buffer[1200 * Ch], 0.12, 0.13); // 25 ms de 50 ms: ganho quadrático 0,25 → 0,125
        Assert.InRange(buffer[2400 * Ch], 0.49, 0.51);
    }

    // ---------------------------------------------------------------- Limitação

    [Fact]
    public void Summed_tracks_are_limited_to_negative_full_scale()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        p = AppendTrack(p, AudioTrack("Áudio 2"));
        p = PlaceOn(p, p.MainSequence.Tracks.Count - 1, MakeClip(media, 0, 0.1));

        var buffer = Mix(new AudioMixer(new FunctionSource(_ => -0.9f)), p, PathsFor(media), 0, 4800);

        AssertFlat(buffer, 0, 4800, -1f); // −1,8 limitado a −1
    }

    // ---------------------------------------------------------------- Continuidade e pré-rolagem

    [Fact]
    public void Contiguous_blocks_with_an_equalizer_match_one_long_block()
    {
        var media = MediaWithAudio();
        var eq = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.Equalizer), "lowGain", 6);
        var clip = MakeClip(media, 0, 0.2) with { Effects = [eq] };
        var p = PlaceOn(Setup(media), Audio1, clip);
        var paths = PathsFor(media);
        var source = new FunctionSource(Sine440);

        var whole = Mix(new AudioMixer(source), p, paths, 0, 4800);

        var mixer = new AudioMixer(source);
        var first = Mix(mixer, p, paths, 0, 2400);
        var second = Mix(mixer, p, paths, 2400, 2400);
        var joined = first.Concat(second).ToArray();

        Assert.Equal(whole.Length, joined.Length);
        Assert.InRange(joined.Zip(whole, (a, b) => Math.Abs(a - b)).Max(), 0f, 1e-6f);
        Assert.True(whole.Max(v => Math.Abs(v)) > 0.1f);
    }

    [Fact]
    public void Seeking_into_an_echo_clip_matches_continuous_playback()
    {
        var media = MediaWithAudio();
        var echo = EffectCatalog.WithValue(EffectCatalog.WithValue(EffectCatalog.WithValue(
            EffectCatalog.Create(EffectCatalog.Echo), "delay", 300), "feedback", 20), "mix", 50);
        var clip = MakeClip(media, 0, 4) with { Effects = [echo] };
        var p = PlaceOn(Setup(media), Audio1, clip);
        var paths = PathsFor(media);
        var source = new FunctionSource(Sine440);
        const int seek = 2 * Rate; // 2 s
        const int window = 4800;

        // Referência: reprodução contínua desde o início, em blocos de 2400 quadros.
        var continuous = new AudioMixer(source);
        var reference = new float[(seek + window) * Ch];
        for (var f = 0; f < seek + window; f += 2400)
            continuous.Mix(p, p.MainSequence, paths, f, reference.AsSpan(f * Ch, 2400 * Ch));

        // Busca direta: o fluxo do clipe começa a frio e usa pré-rolagem.
        var seeked = Mix(new AudioMixer(source), p, paths, seek, window);

        var maxDiff = Enumerable.Range(0, window * Ch).Max(i => Math.Abs(seeked[i] - reference[seek * Ch + i]));
        Assert.InRange(maxDiff, 0f, 1e-4f);
        Assert.True(seeked.Max(v => Math.Abs(v)) > 0.1f);
    }
}
