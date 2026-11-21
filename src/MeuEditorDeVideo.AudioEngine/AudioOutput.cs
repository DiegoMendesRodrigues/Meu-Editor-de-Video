using System.Diagnostics;
using MeuEditorDeVideo.Core.Media;
using Microsoft.Extensions.Logging;

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
    /// <summary>Variável de ambiente que força o backend de áudio no Windows (<c>sdl</c>); nos demais SOs o SDL3 é sempre usado.</summary>
    public const string BackendEnvVar = "MED_AUDIO_BACKEND";

    public IAudioOutput Create()
    {
        var logger = loggers.CreateLogger<AudioOutputFactory>();
        try
        {
#if MED_WASAPI
            var forceSdl = string.Equals(Environment.GetEnvironmentVariable(BackendEnvVar), "sdl", StringComparison.OrdinalIgnoreCase);
            if (OperatingSystem.IsWindows() && !forceSdl) return new WasapiAudioOutput();
#endif
            return new SdlAudioOutput();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DllNotFoundException
                                       or EntryPointNotFoundException or BadImageFormatException
                                       or System.Runtime.InteropServices.COMException)
        {
            logger.LogWarning(ex, "Nenhum dispositivo de áudio disponível; a reprodução seguirá sem som.");
            return new SilentAudioOutput();
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
