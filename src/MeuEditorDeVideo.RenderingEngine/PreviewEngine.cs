using System.Diagnostics;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.RenderingEngine;

/// <summary>Estado imutável que a pré-visualização desenha: projeto, sequência e caminhos resolvidos das mídias.</summary>
public sealed record PreviewSnapshot(Project Project, Sequence Sequence, IReadOnlyDictionary<Guid, string> Paths)
{
    private IReadOnlyDictionary<Guid, string>? _videoPaths;

    public FrameRate FrameRate => Project.Settings.FrameRate;

    /// <summary>Proxies prontos (mídia → arquivo 540p), usados só nos quadros da pré-visualização.</summary>
    public IReadOnlyDictionary<Guid, string> ProxyPaths { get; init; } = new Dictionary<Guid, string>();

    /// <summary>Caminhos para os quadros da pré-visualização: o proxy quando houver, senão o original.</summary>
    public IReadOnlyDictionary<Guid, string> VideoPaths => _videoPaths ??= ProxyPaths.Count == 0
        ? Paths
        : Paths.ToDictionary(p => p.Key, p => ProxyPaths.TryGetValue(p.Key, out var proxy) ? proxy : p.Value);
}

/// <summary>Quadro pronto para exibir.</summary>
public sealed record PreviewFrame(BgraImage Image, MediaTime Time, TimeSpan RenderTime);

/// <summary>
/// Cadeia de pré-visualização (§6.5), independente da exportação: avalia a timeline, decodifica, compõe e entrega quadros;
/// na reprodução, mistura o áudio e usa o dispositivo de áudio como relógio mestre (o vídeo segue o que está sendo ouvido).
/// Duas threads dedicadas: renderização de vídeo e produção de áudio (decodificadores não são compartilhados).
/// </summary>
public sealed class PreviewEngine : IDisposable
{
    private const int CacheCapacity = 48;
    private const int AudioChunkFrames = 1024;

    private readonly IVideoFrameSourceFactory _videoFactory;
    private readonly IAudioSampleSourceFactory _audioFactory;
    private readonly IAudioOutputFactory _outputFactory;
    private readonly FrameRenderer _frames;
    private readonly ILogger<PreviewEngine> _logger;

    private readonly Thread _renderThread;
    private readonly AutoResetEvent _renderSignal = new(false);
    private readonly Lock _gate = new();
    private readonly LinkedList<(CacheKey Key, PreviewFrame Frame)> _cache = new();
    private readonly Queue<(MediaTime Time, int W, int H, TaskCompletionSource<BgraImage?> Done)> _captures = new();

    private volatile PreviewSnapshot? _snapshot;
    private volatile bool _disposed;
    private MediaTime? _pendingTime;
    private bool _forceRender;
    private int _width = 960;
    private int _height = 540;
    private bool _hardware = true;
    private bool _recreateVideoSource;

    // Reprodução
    private volatile bool _playing;
    private MediaTime _playFrom;
    private double _clockBase;
    private bool _clockRunning;
    private IAudioOutput? _output;
    private Thread? _audioThread;
    private AudioRingBuffer? _ring;
    private float _volume = 1;
    private readonly Queue<(long Frame, MixerLevels Levels)> _levels = new();
    private MixerLevels _currentLevels = MixerLevels.Empty;

    public PreviewEngine(IVideoFrameSourceFactory videoFactory, IAudioSampleSourceFactory audioFactory,
        IAudioOutputFactory outputFactory, ICompositor compositor, ILogger<PreviewEngine> logger)
    {
        _videoFactory = videoFactory;
        _audioFactory = audioFactory;
        _outputFactory = outputFactory;
        _frames = new FrameRenderer(compositor);
        _logger = logger;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "Pré-visualização", Priority = ThreadPriority.AboveNormal };
        _renderThread.Start();
    }

    /// <summary>Quadro renderizado (disparado na thread de renderização).</summary>
    public event EventHandler<PreviewFrame>? FrameReady;

    /// <summary>A reprodução chegou ao fim da sequência (disparado fora da thread da interface).</summary>
    public event EventHandler? PlaybackEnded;

    public bool IsPlaying => _playing;

    /// <summary>Se a última reprodução usou um dispositivo de áudio real.</summary>
    public bool HasAudioDevice => _output?.HasDevice ?? true;

    /// <summary>Instante atual: relógio do áudio durante a reprodução; último pedido quando pausado.</summary>
    public MediaTime Position
    {
        get
        {
            lock (_gate)
            {
                if (!_clockRunning || _output is null) return _pendingTime ?? _playFrom;
                var elapsed = Math.Max(0, _output.PlayedSeconds - _clockBase);
                return _playFrom + MediaTime.FromSecondsApprox(elapsed);
            }
        }
    }

    /// <summary>Níveis das trilhas e do total no instante que está sendo ouvido (vazio quando pausado).</summary>
    public MixerLevels CurrentLevels
    {
        get
        {
            if (!_playing) return MixerLevels.Empty;
            var now = AudioFormat.ToFrames(Position);
            lock (_levels)
            {
                while (_levels.Count > 0 && _levels.Peek().Frame <= now) _currentLevels = _levels.Dequeue().Levels;
                return _currentLevels;
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_output is { } o) o.Volume = _volume;
        }
    }

    public void SetSnapshot(PreviewSnapshot snapshot)
    {
        _snapshot = snapshot;
        lock (_gate) _forceRender = true;
        _renderSignal.Set();
    }

    /// <summary>Resolução da pré-visualização (RF: redução de resolução durante a edição).</summary>
    public void SetPreviewSize(int width, int height)
    {
        lock (_gate)
        {
            if (_width == width && _height == height) return;
            _width = Math.Max(16, width);
            _height = Math.Max(16, height);
            _forceRender = true;
        }
        _renderSignal.Set();
    }

    /// <summary>Comparação antes/depois dos efeitos e da correção de cor (RF101).</summary>
    public bool CompareMode
    {
        get => _compareMode;
        set
        {
            if (_compareMode == value) return;
            _compareMode = value;
            lock (_gate) _forceRender = true;
            _renderSignal.Set();
        }
    }

    private volatile bool _compareMode;

    public void SetHardwareDecoding(bool enabled)
    {
        lock (_gate)
        {
            if (_hardware == enabled) return;
            _hardware = enabled;
            _recreateVideoSource = true;
            _forceRender = true;
        }
        _renderSignal.Set();
    }

    /// <summary>Pede o quadro de um instante (cursor parado, arraste na régua). Pedidos antigos são descartados.</summary>
    public void Seek(MediaTime time)
    {
        if (_playing)
        {
            Play(time);
            return;
        }
        lock (_gate) _pendingTime = time;
        _renderSignal.Set();
    }

    public void Play(MediaTime from)
    {
        StopAudio();
        var snapshot = _snapshot;
        if (snapshot is null) return;

        lock (_gate)
        {
            _playFrom = snapshot.FrameRate.SnapToFrame(from);
            _pendingTime = null;
        }

        _ring = new AudioRingBuffer(AudioFormat.SampleRate * AudioFormat.Channels / 2); // 0,5 s
        _output ??= _outputFactory.Create();
        _output.Volume = _volume;

        // Pré-carrega ~150 ms para começar sem estalos.
        var startFrame = AudioFormat.ToFrames(_playFrom);
        _audioThread = new Thread(() => AudioLoop(startFrame)) { IsBackground = true, Name = "Áudio da pré-visualização", Priority = ThreadPriority.Highest };
        _playing = true;
        _audioThread.Start();
        var wait = Stopwatch.StartNew();
        while (_ring.Count < AudioFormat.SampleRate * AudioFormat.Channels * 15 / 100 && wait.ElapsedMilliseconds < 300) Thread.Sleep(2);

        lock (_gate)
        {
            _output.Start(_ring);
            _clockBase = _output.PlayedSeconds;
            _clockRunning = true;
        }
        _renderSignal.Set();
    }

    /// <summary>Pausa e devolve o instante em que parou.</summary>
    public MediaTime Pause()
    {
        if (!_playing) return Position;
        var at = Position;
        StopAudio();
        lock (_gate)
        {
            _pendingTime = _snapshot?.FrameRate.SnapToFrame(at) ?? at;
            _playFrom = _pendingTime.Value;
        }
        _renderSignal.Set();
        return _pendingTime ?? at;
    }

    private void StopAudio()
    {
        lock (_gate) _clockRunning = false;
        _playing = false;
        _audioThread?.Join(500);
        _audioThread = null;
        _output?.Stop();
        _ring = null;
        lock (_levels)
        {
            _levels.Clear();
            _currentLevels = MixerLevels.Empty;
        }
    }

    /// <summary>Captura um quadro numa resolução específica (ex.: resolução total do projeto para salvar PNG).</summary>
    public Task<BgraImage?> CaptureAsync(MediaTime time, int width, int height)
    {
        var tcs = new TaskCompletionSource<BgraImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _captures.Enqueue((time, width, height, tcs));
        _renderSignal.Set();
        return tcs.Task;
    }

    // ------------------------------------------------------------------ vídeo

    private void RenderLoop()
    {
        IVideoFrameSource? video = null;
        long lastFrame = long.MinValue;
        PreviewSnapshot? lastSnapshot = null;
        try
        {
            while (!_disposed)
            {
                (MediaTime Time, int W, int H, TaskCompletionSource<BgraImage?> Done)? capture = null;
                MediaTime? request = null;
                bool force;
                int w, h;
                lock (_gate)
                {
                    if (_captures.Count > 0) capture = _captures.Dequeue();
                    if (_recreateVideoSource)
                    {
                        video?.Dispose();
                        video = null;
                        _recreateVideoSource = false;
                        ClearCache();
                    }
                    force = _forceRender;
                    _forceRender = false;
                    w = _width;
                    h = _height;
                    if (!_playing && _pendingTime is { } p) request = p;
                }

                var snapshot = _snapshot;
                if (snapshot is null)
                {
                    _renderSignal.WaitOne(100);
                    continue;
                }
                video ??= _videoFactory.Create(_hardware);
                if (!ReferenceEquals(snapshot, lastSnapshot))
                {
                    video.Trim(TimelineEvaluator.AssetsInUse(snapshot.Sequence));
                    lastSnapshot = snapshot;
                }

                if (capture is { } c)
                {
                    try
                    {
                        var t = snapshot.FrameRate.SnapToFrame(c.Time);
                        c.Done.TrySetResult(Render(video, snapshot, t, c.W, c.H, useCache: false).Image);
                    }
                    catch (Exception ex)
                    {
                        c.Done.TrySetException(ex);
                    }
                    continue;
                }

                if (_playing)
                {
                    var now = Position;
                    var duration = snapshot.Sequence.Duration;
                    if (now >= duration)
                    {
                        // Fim da sequência: para no último quadro.
                        Pause();
                        lock (_gate) _pendingTime = duration;
                        PlaybackEnded?.Invoke(this, EventArgs.Empty);
                        continue;
                    }
                    var frame = snapshot.FrameRate.TimeToFrame(now);
                    if (frame != lastFrame || force)
                    {
                        Publish(Render(video, snapshot, snapshot.FrameRate.FrameToTime(frame), w, h, useCache: true));
                        lastFrame = frame;
                    }
                    else
                    {
                        _renderSignal.WaitOne(3);
                    }
                    continue;
                }

                if (request is { } r)
                {
                    var frame = snapshot.FrameRate.TimeToFrame(r);
                    if (frame != lastFrame || force)
                    {
                        Publish(Render(video, snapshot, snapshot.FrameRate.FrameToTime(frame), w, h, useCache: true));
                        lastFrame = frame;
                    }
                }
                _renderSignal.WaitOne(250);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A thread de pré-visualização falhou.");
        }
        finally
        {
            video?.Dispose();
        }
    }

    private void Publish(PreviewFrame frame) => FrameReady?.Invoke(this, frame);

    private PreviewFrame Render(IVideoFrameSource video, PreviewSnapshot snapshot, MediaTime t, int w, int h, bool useCache)
    {
        // Capturas (useCache = false) sempre usam o original em resolução total, nunca o proxy.
        var paths = useCache ? snapshot.VideoPaths : snapshot.Paths;
        var key = new CacheKey(snapshot.Sequence, snapshot.Project.Media, paths, snapshot.FrameRate.TimeToFrame(t), w, h, CompareMode);
        if (useCache && TryCache(key) is { } cached) return cached;

        var watch = Stopwatch.StartNew();
        var image = CompareMode
            ? _frames.RenderCompare(video, snapshot.Project, snapshot.Sequence, paths, t, w, h)
            : _frames.Render(video, snapshot.Project, snapshot.Sequence, paths, t, w, h);
        var result = new PreviewFrame(image, t, watch.Elapsed);
        if (useCache) AddCache(key, result);
        return result;
    }

    private readonly record struct CacheKey(Sequence Sequence, object Media, object Paths, long Frame, int W, int H, bool Compare);

    private PreviewFrame? TryCache(CacheKey key)
    {
        for (var n = _cache.First; n is not null; n = n.Next)
        {
            var k = n.Value.Key;
            if (ReferenceEquals(k.Sequence, key.Sequence) && ReferenceEquals(k.Media, key.Media) && ReferenceEquals(k.Paths, key.Paths) && k.Frame == key.Frame && k.W == key.W && k.H == key.H && k.Compare == key.Compare)
            {
                _cache.Remove(n);
                _cache.AddFirst(n);
                return n.Value.Frame;
            }
        }
        return null;
    }

    private void AddCache(CacheKey key, PreviewFrame frame)
    {
        _cache.AddFirst((key, frame));
        while (_cache.Count > CacheCapacity) _cache.RemoveLast();
    }

    private void ClearCache() => _cache.Clear();

    /// <summary>Quadros em cache (diagnóstico/testes).</summary>
    public int CachedFrames => _cache.Count;

    // ------------------------------------------------------------------ áudio

    private void AudioLoop(long startFrame)
    {
        var ring = _ring;
        if (ring is null) return;
        using var source = _audioFactory.Create();
        var mixer = new AudioMixer(source);
        var chunk = new float[AudioChunkFrames * AudioFormat.Channels];
        var next = startFrame;
        PreviewSnapshot? last = null;
        try
        {
            while (_playing && !_disposed)
            {
                if (ring.Free < chunk.Length)
                {
                    Thread.Sleep(5);
                    continue;
                }
                var snapshot = _snapshot;
                if (snapshot is null) break;
                if (!ReferenceEquals(snapshot, last))
                {
                    source.Trim(TimelineEvaluator.AssetsInUse(snapshot.Sequence));
                    last = snapshot;
                }
                mixer.Mix(snapshot.Project, snapshot.Sequence, snapshot.Paths, next, chunk);
                ring.Write(chunk);
                lock (_levels)
                {
                    _levels.Enqueue((next, mixer.LastLevels));
                    while (_levels.Count > 64) _levels.Dequeue();
                }
                next += AudioChunkFrames;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na mixagem de áudio da pré-visualização.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAudio();
        _disposed = true;
        _renderSignal.Set();
        _renderThread.Join(1000);
        _output?.Dispose();
        _renderSignal.Dispose();
        lock (_gate)
            while (_captures.Count > 0) _captures.Dequeue().Done.TrySetCanceled();
    }
}
