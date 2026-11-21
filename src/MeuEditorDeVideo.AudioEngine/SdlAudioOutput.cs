using System.Runtime.InteropServices;
using MeuEditorDeVideo.Core.Media;
using SDL;
using static SDL.SDL3;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>
/// Saída de áudio multiplataforma (SDL3): um fluxo de áudio (<c>SDL_AudioStream</c>) float 48 kHz estéreo,
/// alimentado por callback a partir do <see cref="AudioRingBuffer"/>. A posição tocada é
/// quadros enviados − quadros ainda na fila do fluxo − latência do buffer do dispositivo.
/// </summary>
public sealed unsafe class SdlAudioOutput : IAudioOutput
{
    private static readonly object InitLock = new();
    private static int _initCount;

    private readonly SDL_AudioStream* _stream;
    private readonly int _deviceLatencyFrames;
    private readonly PlayedPositionClock _clock = new(AudioFormat.SampleRate);
    private readonly GCHandle _self;
    private volatile AudioRingBuffer? _buffer;
    private readonly Lock _feedLock = new();
    private float[] _scratch = new float[4096 * AudioFormat.Channels];
    private long _submittedFrames;
    private volatile float _volume = 1;
    private bool _disposed;

    public SdlAudioOutput()
    {
        lock (InitLock)
        {
            if (_initCount == 0 && !SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
                throw new InvalidOperationException("SDL3 não iniciou o áudio: " + SDL_GetError());
            _initCount++;
        }

        _self = GCHandle.Alloc(this);
        try
        {
            var spec = new SDL_AudioSpec
            {
                format = SDL_AudioFormat.SDL_AUDIO_F32LE,
                channels = AudioFormat.Channels,
                freq = AudioFormat.SampleRate,
            };
            _stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, &OnStreamNeedsData, GCHandle.ToIntPtr(_self));
            if (_stream == null)
                throw new InvalidOperationException("SDL3 não abriu um dispositivo de áudio: " + SDL_GetError());

            // Latência: o buffer do dispositivo ainda não aparece em SDL_GetAudioStreamQueued.
            var device = SDL_GetAudioStreamDevice(_stream);
            SDL_AudioSpec dev;
            int frames = 0;
            if (SDL_GetAudioDeviceFormat(device, &dev, &frames) && frames > 0)
                _deviceLatencyFrames = (int)((long)frames * AudioFormat.SampleRate / Math.Max(1, dev.freq));
        }
        catch
        {
            ReleaseInit();
            _self.Free();
            throw;
        }
    }

    public bool HasDevice => true;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public double PlayedSeconds
    {
        get
        {
            if (_disposed) return _clock.Last;
            var queuedFrames = SDL_GetAudioStreamQueued(_stream) / (sizeof(float) * AudioFormat.Channels);
            return _clock.Sample(Interlocked.Read(ref _submittedFrames), queuedFrames, _deviceLatencyFrames);
        }
    }

    public void Start(AudioRingBuffer buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SDL_PauseAudioStreamDevice(_stream);
        SDL_ClearAudioStream(_stream);
        Interlocked.Exchange(ref _submittedFrames, 0);
        _clock.Reset();
        _buffer = buffer;
        // Pré-enche o fluxo para o dispositivo não começar com falta de dados.
        Feed(_deviceLatencyFrames + AudioFormat.SampleRate / 50);
        SDL_ResumeAudioStreamDevice(_stream);
    }

    public void Stop()
    {
        if (_disposed) return;
        _clock.Pause(); // congela o relógio antes de esvaziar
        SDL_PauseAudioStreamDevice(_stream);
        SDL_ClearAudioStream(_stream);
        _buffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        SDL_DestroyAudioStream(_stream);
        _self.Free();
        ReleaseInit();
    }

    private static void ReleaseInit()
    {
        lock (InitLock)
        {
            if (--_initCount == 0) SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
        }
    }

    /// <summary>Move até <paramref name="frames"/> quadros do anel para o fluxo; faltando dados, completa com silêncio (o relógio não para).</summary>
    private void Feed(int frames)
    {
        if (frames <= 0) return;
        var samples = frames * AudioFormat.Channels;
        lock (_feedLock)
        {
            if (_scratch.Length < samples) _scratch = new float[samples];
            var span = _scratch.AsSpan(0, samples);
            var read = _buffer?.Read(span) ?? 0;
            var volume = _volume;
            if (volume < 0.999f)
                for (var i = 0; i < read; i++) span[i] *= volume;
            span[read..].Clear();
            fixed (float* p = span)
            {
                if (SDL_PutAudioStreamData(_stream, (IntPtr)p, samples * sizeof(float)))
                    Interlocked.Add(ref _submittedFrames, frames);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnStreamNeedsData(IntPtr userdata, SDL_AudioStream* stream, int additionalAmount, int totalAmount)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is SdlAudioOutput self && additionalAmount > 0)
                self.Feed(additionalAmount / (sizeof(float) * AudioFormat.Channels));
        }
        catch
        {
            // Exceções não podem atravessar o limite nativo.
        }
    }
}
