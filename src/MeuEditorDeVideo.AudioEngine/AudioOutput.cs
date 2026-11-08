using System.Diagnostics;
using System.Runtime.InteropServices;
using MeuEditorDeVideo.Core.Media;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>
/// Saída de áudio que consome uma <see cref="AudioRingBuffer"/> e informa quanto já foi tocado —
/// esse valor é o relógio mestre da reprodução.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>True se há um dispositivo real; false quando o relógio é simulado (sem placa de som).</summary>
    bool HasDevice { get; }

    void Start(AudioRingBuffer buffer);

    void Stop();

    /// <summary>Segundos efetivamente reproduzidos desde <see cref="Start"/>.</summary>
    double PlayedSeconds { get; }

    float Volume { get; set; }
}

public interface IAudioOutputFactory
{
    IAudioOutput Create();
}

public sealed class AudioOutputFactory(ILoggerFactory loggers) : IAudioOutputFactory
{
    public IAudioOutput Create()
    {
        var logger = loggers.CreateLogger<WasapiAudioOutput>();
        try
        {
            return new WasapiAudioOutput();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Nenhum dispositivo de áudio disponível; a reprodução seguirá sem som.");
            return new SilentAudioOutput();
        }
    }
}

/// <summary>Saída WASAPI compartilhada (latência ~60 ms) via NAudio.</summary>
public sealed class WasapiAudioOutput : IAudioOutput
{
    private readonly WasapiOut _device;
    private readonly RingProvider _provider = new();

    public WasapiAudioOutput()
    {
        _device = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        _device.Init(_provider);
    }

    public bool HasDevice => true;

    public float Volume
    {
        get => _provider.Volume;
        set => _provider.Volume = Math.Clamp(value, 0f, 1f);
    }

    public double PlayedSeconds
    {
        get
        {
            var format = _device.OutputWaveFormat;
            return format.AverageBytesPerSecond > 0 ? (double)_device.GetPosition() / format.AverageBytesPerSecond : 0;
        }
    }

    public void Start(AudioRingBuffer buffer)
    {
        _provider.Buffer = buffer;
        _device.Play();
    }

    public void Stop()
    {
        _device.Stop();
        _provider.Buffer = null;
    }

    public void Dispose() => _device.Dispose();

    private sealed class RingProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);

        public volatile AudioRingBuffer? Buffer;

        public float Volume { get; set; } = 1;

        public int Read(byte[] buffer, int offset, int count)
        {
            var floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count - count % 4));
            var read = Buffer?.Read(floats) ?? 0;
            var volume = Volume;
            if (volume < 0.999f)
                for (var i = 0; i < read; i++) floats[i] *= volume;
            floats[read..].Clear(); // falta de dados: silêncio, sem interromper o relógio
            return count;
        }
    }
}

/// <summary>Sem dispositivo: consome o buffer em tempo real para que o relógio avance normalmente.</summary>
public sealed class SilentAudioOutput : IAudioOutput
{
    private readonly Stopwatch _clock = new();
    private Timer? _timer;
    private AudioRingBuffer? _buffer;
    private long _consumedFrames;
    private readonly float[] _sink = new float[AudioFormat.SampleRate / 50 * AudioFormat.Channels];

    public bool HasDevice => false;

    public float Volume { get; set; } = 1;

    public double PlayedSeconds => _clock.Elapsed.TotalSeconds;

    public void Start(AudioRingBuffer buffer)
    {
        _buffer = buffer;
        _consumedFrames = 0;
        _clock.Restart();
        _timer = new Timer(_ => Drain(), null, 0, 10);
    }

    private void Drain()
    {
        if (_buffer is not { } b) return;
        var due = (long)(_clock.Elapsed.TotalSeconds * AudioFormat.SampleRate) - _consumedFrames;
        while (due > 0)
        {
            var n = (int)Math.Min(due * AudioFormat.Channels, _sink.Length);
            b.Read(_sink.AsSpan(0, n));
            _consumedFrames += n / AudioFormat.Channels;
            due -= n / AudioFormat.Channels;
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _clock.Stop();
        _buffer = null;
    }

    public void Dispose() => Stop();
}
