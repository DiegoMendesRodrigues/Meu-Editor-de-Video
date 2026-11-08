using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>
/// Mistura as trilhas de áudio de uma sequência (§6.5 — sincronização audiovisual) no formato <see cref="AudioFormat"/>.
/// Respeita mudo de trilha, volume do clipe e velocidade (reamostragem linear; preservação de tom chega na F7).
/// Não é thread-safe: uma instância por thread produtora.
/// </summary>
public sealed class AudioMixer(IAudioSampleSource source)
{
    private const int Ch = AudioFormat.Channels;
    private float[] _scratch = new float[4096 * Ch];

    /// <summary>
    /// Preenche <paramref name="destination"/> com o áudio da timeline a partir do quadro de áudio <paramref name="fromFrame"/>
    /// (posição em quadros de 48 kHz desde o início da sequência).
    /// </summary>
    public void Mix(Project project, Sequence sequence, IReadOnlyDictionary<Guid, string> paths, long fromFrame,
        Span<float> destination, double masterVolume = 1)
    {
        destination.Clear();
        var frames = destination.Length / Ch;
        if (frames == 0) return;

        var from = AudioFormat.FromFrames(fromFrame);
        var to = AudioFormat.FromFrames(fromFrame + frames);
        foreach (var segment in TimelineEvaluator.AudioIn(project, sequence, from, to))
        {
            if (!paths.TryGetValue(segment.Asset.Id, out var path)) continue;
            var start = (int)Math.Clamp(AudioFormat.ToFrames(segment.TimelineStart) - fromFrame, 0, frames);
            var end = (int)Math.Clamp(AudioFormat.ToFrames(segment.TimelineEnd) - fromFrame, start, frames);
            var count = end - start;
            if (count <= 0) continue;

            var gain = (float)(segment.Volume * masterVolume);
            var target = destination.Slice(start * Ch, count * Ch);
            if (segment.Speed == Rational.One)
            {
                var buffer = Scratch(count);
                var read = source.Read(segment.Asset, path, segment.SourceStart, buffer);
                Accumulate(target, buffer[..(read * Ch)], gain);
            }
            else
            {
                MixResampled(segment, path, target, count, gain);
            }
        }

        // Limita picos de soma de várias trilhas.
        for (var i = 0; i < destination.Length; i++)
            destination[i] = Math.Clamp(destination[i], -1f, 1f);
    }

    private void MixResampled(AudioSegment segment, string path, Span<float> target, int count, float gain)
    {
        var speed = segment.Speed.Value;
        var sourceFrames = (int)Math.Ceiling(count * speed) + 2;
        var buffer = Scratch(sourceFrames);
        var read = source.Read(segment.Asset, path, segment.SourceStart, buffer);
        if (read < 2) return;
        for (var i = 0; i < count; i++)
        {
            var pos = i * speed;
            var i0 = (int)pos;
            if (i0 + 1 >= read) break;
            var frac = (float)(pos - i0);
            for (var c = 0; c < Ch; c++)
            {
                var a = buffer[i0 * Ch + c];
                var b = buffer[(i0 + 1) * Ch + c];
                target[i * Ch + c] += (a + (b - a) * frac) * gain;
            }
        }
    }

    private Span<float> Scratch(int frames)
    {
        if (_scratch.Length < frames * Ch) _scratch = new float[frames * Ch];
        return _scratch.AsSpan(0, frames * Ch);
    }

    private static void Accumulate(Span<float> target, ReadOnlySpan<float> source, float gain)
    {
        var n = Math.Min(target.Length, source.Length);
        for (var i = 0; i < n; i++) target[i] += source[i] * gain;
    }
}

/// <summary>Fila circular de amostras entre a thread produtora (mixagem) e o dispositivo de áudio.</summary>
public sealed class AudioRingBuffer(int capacitySamples)
{
    private readonly float[] _data = new float[capacitySamples];
    private readonly Lock _lock = new();
    private int _read;
    private int _count;

    public int Capacity => _data.Length;

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public int Free
    {
        get { lock (_lock) return _data.Length - _count; }
    }

    public int Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            var n = Math.Min(samples.Length, _data.Length - _count);
            var write = (_read + _count) % _data.Length;
            var first = Math.Min(n, _data.Length - write);
            samples[..first].CopyTo(_data.AsSpan(write));
            samples.Slice(first, n - first).CopyTo(_data);
            _count += n;
            return n;
        }
    }

    public int Read(Span<float> destination)
    {
        lock (_lock)
        {
            var n = Math.Min(destination.Length, _count);
            var first = Math.Min(n, _data.Length - _read);
            _data.AsSpan(_read, first).CopyTo(destination);
            _data.AsSpan(0, n - first).CopyTo(destination[first..]);
            _read = (_read + n) % _data.Length;
            _count -= n;
            return n;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _read = 0;
            _count = 0;
        }
    }
}
