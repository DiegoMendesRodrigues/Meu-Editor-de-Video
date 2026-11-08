using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Audio;

public sealed class AudioMixerTests
{
    private const int Audio1 = 2;
    private const int Ch = AudioFormat.Channels;
    private const float Tolerance = 1e-6f;

    /// <summary>Fonte falsa: devolve sempre o mesmo valor e registra cada pedido.</summary>
    private sealed class FakeAudioSource(float value) : IAudioSampleSource
    {
        public readonly record struct Call(Guid AssetId, string Path, MediaTime SourceTime, int Frames);

        public List<Call> Calls { get; } = [];

        public int Read(MediaAsset asset, string path, MediaTime sourceTime, Span<float> destination)
        {
            var frames = destination.Length / Ch;
            for (var i = 0; i < destination.Length; i++) destination[i] = value;
            Calls.Add(new Call(asset.Id, path, sourceTime, frames));
            return frames;
        }

        public void Trim(IReadOnlySet<Guid> inUse) { }

        public void Dispose() { }
    }

    private static MediaTime T(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static MediaAsset MediaWithAudio(string name = "v.mp4") => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Video,
        Name = name,
        OriginalPath = @"C:\m\" + name,
        Duration = T(10),
        Info = new MediaTechnicalInfo { AudioCodec = "aac" },
    };

    private static Clip MakeClip(MediaAsset asset, double start, double duration, double sourceIn = 0,
        Rational? speed = null, double volume = 1) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = asset.Id,
        Start = T(start),
        Duration = T(duration),
        SourceIn = T(sourceIn),
        Speed = speed ?? Rational.One,
        Volume = volume,
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

    private static IReadOnlyDictionary<Guid, string> PathsFor(MediaAsset asset) =>
        new Dictionary<Guid, string> { [asset.Id] = asset.OriginalPath };

    private static float[] Mix(AudioMixer mixer, Project project, IReadOnlyDictionary<Guid, string> paths,
        long fromFrame, int frames, double masterVolume = 1)
    {
        var buffer = new float[frames * Ch];
        mixer.Mix(project, project.MainSequence, paths, fromFrame, buffer, masterVolume);
        return buffer;
    }

    private static void AssertRangeEquals(float[] buffer, int fromFrame, int toFrame, float expected)
    {
        for (var f = fromFrame; f < toFrame; f++)
        {
            Assert.True(Math.Abs(buffer[f * Ch] - expected) <= Tolerance, $"Quadro {f} canal 0: {buffer[f * Ch]} != {expected}");
            Assert.True(Math.Abs(buffer[f * Ch + 1] - expected) <= Tolerance, $"Quadro {f} canal 1: {buffer[f * Ch + 1]} != {expected}");
        }
    }

    // ---------------------------------------------------------------- Mix

    [Fact]
    public void Clip_fills_only_its_frame_range_within_window()
    {
        // Clipe começa em 0,05 s (quadro 2400) e dura 0,1 s; janela = primeiros 0,1 s (4800 quadros).
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0.05, 0.1));
        var source = new FakeAudioSource(0.25f);
        var mixer = new AudioMixer(source);

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800);

        AssertRangeEquals(buffer, 0, 2400, 0f);
        AssertRangeEquals(buffer, 2400, 4800, 0.25f);
        var call = Assert.Single(source.Calls);
        Assert.Equal(2400, call.Frames);
        Assert.Equal(media.Id, call.AssetId);
        Assert.Equal(media.OriginalPath, call.Path);
    }

    [Fact]
    public void Read_starts_at_source_time_mapped_from_window_position()
    {
        // Clipe em [0,05 s; 0,15 s) com entrada na mídia em 1 s. Janela começa em 0,1 s ⇒ origem = 1 + 0,05 = 1,05 s.
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0.05, 0.1, sourceIn: 1));
        var source = new FakeAudioSource(0.25f);
        var mixer = new AudioMixer(source);

        var buffer = Mix(mixer, p, PathsFor(media), 4800, 4800);

        AssertRangeEquals(buffer, 0, 2400, 0.25f);
        AssertRangeEquals(buffer, 2400, 4800, 0f);
        var call = Assert.Single(source.Calls);
        Assert.Equal(2400, call.Frames);
        Assert.InRange(call.SourceTime.TotalSeconds, 1.05 - 1e-6, 1.05 + 1e-6);
    }

    [Fact]
    public void Window_after_clip_end_is_silent_and_does_not_read()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        var source = new FakeAudioSource(0.25f);
        var mixer = new AudioMixer(source);

        var buffer = Mix(mixer, p, PathsFor(media), 9600, 4800);

        AssertRangeEquals(buffer, 0, 4800, 0f);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void Two_overlapping_clips_sum()
    {
        var media = MediaWithAudio();
        var p = Setup(media);
        p = PlaceOn(p, Audio1, MakeClip(media, 0, 0.1));
        p = AppendTrack(p, AudioTrack("Áudio 2"));
        p = PlaceOn(p, p.MainSequence.Tracks.Count - 1, MakeClip(media, 0, 0.1));
        var source = new FakeAudioSource(0.25f);
        var mixer = new AudioMixer(source);

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800);

        AssertRangeEquals(buffer, 0, 4800, 0.5f);
        Assert.Equal(2, source.Calls.Count);
    }

    [Fact]
    public void Sum_of_overlapping_clips_is_clamped_to_one()
    {
        var media = MediaWithAudio();
        var p = Setup(media);
        p = PlaceOn(p, Audio1, MakeClip(media, 0, 0.1));
        p = AppendTrack(p, AudioTrack("Áudio 2"));
        p = PlaceOn(p, p.MainSequence.Tracks.Count - 1, MakeClip(media, 0, 0.1));
        var mixer = new AudioMixer(new FakeAudioSource(0.75f));

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800);

        AssertRangeEquals(buffer, 0, 4800, 1f);
    }

    [Fact]
    public void Muted_track_is_silent()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        var seq = p.MainSequence;
        p = p.WithSequence(seq with { Tracks = seq.Tracks.SetItem(Audio1, seq.Tracks[Audio1] with { IsMuted = true }) });
        var source = new FakeAudioSource(0.25f);

        var buffer = Mix(new AudioMixer(source), p, PathsFor(media), 0, 4800);

        AssertRangeEquals(buffer, 0, 4800, 0f);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void Master_volume_scales_output()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        var mixer = new AudioMixer(new FakeAudioSource(0.25f));

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800, masterVolume: 0.5);

        AssertRangeEquals(buffer, 0, 4800, 0.125f);
    }

    [Fact]
    public void Clip_volume_multiplies_with_master_volume()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1, volume: 0.5));
        var mixer = new AudioMixer(new FakeAudioSource(0.5f));

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800, masterVolume: 0.5);

        AssertRangeEquals(buffer, 0, 4800, 0.125f);
    }

    [Fact]
    public void Double_speed_requests_about_twice_the_source_frames()
    {
        // 4800 quadros de timeline a 2x consomem ~9600 quadros de origem.
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1, speed: new Rational(2, 1)));
        var source = new FakeAudioSource(0.25f);
        var mixer = new AudioMixer(source);

        var buffer = Mix(mixer, p, PathsFor(media), 0, 4800);

        var call = Assert.Single(source.Calls);
        Assert.InRange(call.Frames, 9600, 9610);
        AssertRangeEquals(buffer, 0, 4800, 0.25f);
    }

    [Fact]
    public void Segments_without_path_are_silent()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        var source = new FakeAudioSource(0.25f);

        var buffer = Mix(new AudioMixer(source), p, new Dictionary<Guid, string>(), 0, 4800);

        AssertRangeEquals(buffer, 0, 4800, 0f);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void Empty_destination_is_a_no_op()
    {
        var media = MediaWithAudio();
        var p = PlaceOn(Setup(media), Audio1, MakeClip(media, 0, 0.1));
        var source = new FakeAudioSource(0.25f);

        new AudioMixer(source).Mix(p, p.MainSequence, PathsFor(media), 0, Span<float>.Empty);

        Assert.Empty(source.Calls);
    }

    // ---------------------------------------------------------------- AudioFormat

    [Fact]
    public void AudioFormat_is_48k_stereo()
    {
        Assert.Equal(48000, AudioFormat.SampleRate);
        Assert.Equal(2, AudioFormat.Channels);
        Assert.Equal(48000, AudioFormat.ToFrames(MediaTime.FromSeconds(1)));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(47L)]
    [InlineData(48000L)]
    [InlineData(123457L)]
    [InlineData(9_600_000L)]
    public void ToFrames_and_FromFrames_round_trip(long frames)
    {
        Assert.Equal(frames, AudioFormat.ToFrames(AudioFormat.FromFrames(frames)));
    }

    // ---------------------------------------------------------------- AudioRingBuffer

    [Fact]
    public void Write_then_read_preserves_fifo_order()
    {
        var ring = new AudioRingBuffer(8);

        Assert.Equal(3, ring.Write(new float[] { 1, 2, 3 }));
        Assert.Equal(3, ring.Count);
        Assert.Equal(5, ring.Free);

        var output = new float[3];
        Assert.Equal(3, ring.Read(output));
        Assert.Equal(new float[] { 1, 2, 3 }, output);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void Read_returns_only_what_is_available()
    {
        var ring = new AudioRingBuffer(8);
        ring.Write(new float[] { 4, 5 });

        var output = new float[5];
        Assert.Equal(2, ring.Read(output));
        Assert.Equal(new float[] { 4, 5, 0, 0, 0 }, output);
        Assert.Equal(0, ring.Read(output));
    }

    [Fact]
    public void Wrap_around_keeps_order()
    {
        var ring = new AudioRingBuffer(8);
        ring.Write(new float[] { 1, 2, 3, 4, 5, 6 });
        var first = new float[4];
        Assert.Equal(4, ring.Read(first));
        Assert.Equal(new float[] { 1, 2, 3, 4 }, first);

        // Escreve 4 amostras: as duas últimas da escrita devem dar a volta no buffer.
        Assert.Equal(4, ring.Write(new float[] { 7, 8, 9, 10 }));

        var rest = new float[6];
        Assert.Equal(6, ring.Read(rest));
        Assert.Equal(new float[] { 5, 6, 7, 8, 9, 10 }, rest);
    }

    [Fact]
    public void Write_accepts_only_free_space()
    {
        var ring = new AudioRingBuffer(4);

        Assert.Equal(4, ring.Write(new float[] { 1, 2, 3, 4, 5, 6 }));
        Assert.Equal(4, ring.Count);
        Assert.Equal(0, ring.Free);
        Assert.Equal(0, ring.Write(new float[] { 7 }));

        var output = new float[4];
        ring.Read(output);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, output);
    }

    [Fact]
    public void Read_on_empty_buffer_returns_zero()
    {
        var ring = new AudioRingBuffer(4);

        Assert.Equal(0, ring.Read(new float[4]));
        Assert.Equal(4, ring.Capacity);
    }

    [Fact]
    public void Clear_empties_buffer_and_restarts_at_beginning()
    {
        var ring = new AudioRingBuffer(8);
        ring.Write(new float[] { 1, 2, 3, 4, 5 });
        var skip = new float[3];
        ring.Read(skip);

        ring.Clear();

        Assert.Equal(0, ring.Count);
        Assert.Equal(8, ring.Free);
        Assert.Equal(0, ring.Read(new float[8]));

        Assert.Equal(2, ring.Write(new float[] { 9, 10 }));
        var output = new float[2];
        ring.Read(output);
        Assert.Equal(new float[] { 9, 10 }, output);
    }
}
