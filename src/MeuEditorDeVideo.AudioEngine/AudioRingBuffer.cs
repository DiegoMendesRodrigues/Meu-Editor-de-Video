namespace MeuEditorDeVideo.AudioEngine;

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
