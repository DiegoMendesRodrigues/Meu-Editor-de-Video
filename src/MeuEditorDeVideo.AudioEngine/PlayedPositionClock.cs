namespace MeuEditorDeVideo.AudioEngine;

/// <summary>
/// Matemática pura do relógio de áudio de saídas baseadas em fila (SDL): posição reproduzida =
/// quadros enviados − quadros ainda na fila − latência do dispositivo, em segundos. É monotônica,
/// congela em pausa e volta a zero em <see cref="Reset"/>.
/// </summary>
public sealed class PlayedPositionClock(int sampleRate)
{
    private readonly Lock _lock = new();
    private double _last;
    private bool _paused;

    /// <summary>Segundos reproduzidos para um instantâneo dos contadores (sem estado; nunca negativo).</summary>
    public static double Seconds(long submittedFrames, long queuedFrames, long latencyFrames, int sampleRate)
    {
        if (sampleRate <= 0) return 0;
        var played = submittedFrames - queuedFrames - Math.Max(0, latencyFrames);
        return played <= 0 ? 0 : (double)played / sampleRate;
    }

    /// <summary>Zera o relógio e o deixa correndo (início de uma nova reprodução).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _last = 0;
            _paused = false;
        }
    }

    public void Pause()
    {
        lock (_lock) _paused = true;
    }

    public void Resume()
    {
        lock (_lock) _paused = false;
    }

    /// <summary>Posição atual: monotônica; em pausa devolve o último valor sem aceitar novos contadores.</summary>
    public double Sample(long submittedFrames, long queuedFrames, long latencyFrames)
    {
        lock (_lock)
        {
            if (_paused) return _last;
            var now = Seconds(submittedFrames, queuedFrames, latencyFrames, sampleRate);
            if (now > _last) _last = now;
            return _last;
        }
    }

    /// <summary>Último valor entregue (posição congelada).</summary>
    public double Last
    {
        get { lock (_lock) return _last; }
    }
}
