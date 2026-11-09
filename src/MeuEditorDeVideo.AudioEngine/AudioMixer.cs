using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>Nível de um canal estéreo em um bloco (amplitude linear 0…1+).</summary>
public readonly record struct ChannelLevel(float PeakL, float PeakR, float RmsL, float RmsR)
{
    public static ChannelLevel Silence => default;

    public static double ToDb(float amplitude) => amplitude <= 1e-6f ? -120 : 20 * Math.Log10(amplitude);

    public static ChannelLevel Measure(ReadOnlySpan<float> s)
    {
        float pl = 0, pr = 0;
        double sl = 0, sr = 0;
        var frames = s.Length / 2;
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var l = s[i];
            var r = s[i + 1];
            pl = Math.Max(pl, Math.Abs(l));
            pr = Math.Max(pr, Math.Abs(r));
            sl += l * l;
            sr += r * r;
        }
        return frames == 0 ? default : new ChannelLevel(pl, pr, (float)Math.Sqrt(sl / frames), (float)Math.Sqrt(sr / frames));
    }
}

/// <summary>Níveis de um bloco mixado: por trilha (após volume/panorâmica) e o total (RF130).</summary>
public sealed record MixerLevels(IReadOnlyDictionary<Guid, ChannelLevel> Tracks, ChannelLevel Master)
{
    public static MixerLevels Empty { get; } = new(new Dictionary<Guid, ChannelLevel>(), default);
}

/// <summary>
/// Mistura as trilhas de áudio de uma sequência (§6.5, RF119–RF131) no formato <see cref="AudioFormat"/>.
/// Cada clipe tem um fluxo sequencial próprio: fonte → velocidade (com ou sem preservação de tom) → efeitos → envelope
/// de volume (keyframes e rampas). Depois, volume e panorâmica da trilha, medição de níveis e limitação do total.
/// Após buscas, os fluxos são reiniciados com pré-rolagem para que filtros e caudas soem como na reprodução contínua —
/// assim a exportação e a pré-visualização produzem o mesmo resultado.
/// Não é thread-safe: uma instância por thread produtora.
/// </summary>
public sealed class AudioMixer(IAudioSampleSource source)
{
    private const int Ch = AudioFormat.Channels;
    private readonly Dictionary<Guid, ClipStream> _streams = [];
    private readonly HashSet<Guid> _used = [];
    private float[] _track = new float[4096 * Ch];
    private float[] _clip = new float[4096 * Ch];

    /// <summary>Níveis do último bloco mixado.</summary>
    public MixerLevels LastLevels { get; private set; } = MixerLevels.Empty;

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
        if (_track.Length < destination.Length) _track = new float[destination.Length];
        if (_clip.Length < destination.Length) _clip = new float[destination.Length];

        var from = AudioFormat.FromFrames(fromFrame);
        var to = AudioFormat.FromFrames(fromFrame + frames);
        var levels = new Dictionary<Guid, ChannelLevel>();
        _used.Clear();

        foreach (var group in TimelineEvaluator.AudioIn(project, sequence, from, to).GroupBy(s => s.Track.Id))
        {
            var trackBuf = _track.AsSpan(0, destination.Length);
            trackBuf.Clear();
            Track? track = null;
            foreach (var segment in group)
            {
                track = segment.Track;
                if (!paths.TryGetValue(segment.Asset.Id, out var path)) continue;
                var start = (int)Math.Clamp(AudioFormat.ToFrames(segment.TimelineStart) - fromFrame, 0, frames);
                var end = (int)Math.Clamp(AudioFormat.ToFrames(segment.TimelineEnd) - fromFrame, start, frames);
                var count = end - start;
                if (count <= 0) continue;

                if (!_streams.TryGetValue(segment.ClipId, out var stream))
                    _streams[segment.ClipId] = stream = new ClipStream(source);
                _used.Add(segment.ClipId);
                var buf = _clip.AsSpan(0, count * Ch);
                stream.Render(segment.Clip, segment.Asset, path, fromFrame + start, buf);
                var target = trackBuf.Slice(start * Ch, count * Ch);
                for (var i = 0; i < buf.Length; i++) target[i] += buf[i];
            }
            if (track is null) continue;

            // Volume e panorâmica da trilha (lei de balanço: centro sem atenuação).
            var vol = (float)Math.Clamp(track.Volume, 0, ClipAnimation.MaxVolume);
            var pan = Math.Clamp(track.Pan, -1, 1);
            var gl = vol * (float)(pan > 0 ? 1 - pan : 1);
            var gr = vol * (float)(pan < 0 ? 1 + pan : 1);
            for (var i = 0; i + 1 < trackBuf.Length; i += 2)
            {
                trackBuf[i] *= gl;
                trackBuf[i + 1] *= gr;
            }
            levels[track.Id] = ChannelLevel.Measure(trackBuf);
            for (var i = 0; i < trackBuf.Length; i++) destination[i] += trackBuf[i];
        }

        // Fluxos de clipes que deixaram de soar são descartados (voltar a eles é uma descontinuidade).
        if (_streams.Count > _used.Count)
            foreach (var id in _streams.Keys.Where(k => !_used.Contains(k)).ToList())
                _streams.Remove(id);

        var master = (float)masterVolume;
        for (var i = 0; i < destination.Length; i++)
            destination[i] = Math.Clamp(destination[i] * master, -1f, 1f);
        LastLevels = new MixerLevels(levels, ChannelLevel.Measure(destination));
    }

    /// <summary>Esquece o estado dos clipes (próxima mixagem recomeça com pré-rolagem).</summary>
    public void Reset() => _streams.Clear();
}

/// <summary>
/// Fluxo sequencial de um clipe. Mantém o processamento contínuo enquanto os pedidos forem contíguos; numa descontinuidade
/// (ou mudança de mídia, posição, velocidade ou estrutura dos efeitos) reinicia: começa a ler
/// <see cref="AudioEffectChain.PreRollFrames"/> antes e descarta pré-rolagem + latência da cadeia.
/// </summary>
public sealed class ClipStream(IAudioSampleSource source)
{
    private const int Ch = AudioFormat.Channels;
    private const int GainStep = 32;

    private readonly AudioEffectChain _chain = new();
    private ISpeedProducer? _producer;
    private long _next = long.MinValue;
    private (Guid Asset, MediaTime Start, MediaTime SourceIn, Rational Speed, bool Pitch) _signature;
    private Clip? _clipRef;
    private MediaAsset _asset = null!;
    private string _path = "";
    private float[] _discard = new float[4096 * Ch];

    /// <summary>Aplica volume, keyframes de volume e rampas (desligado em análises de silêncio/sincronia).</summary>
    public bool ApplyGainEnvelope { get; init; } = true;

    /// <summary>Escreve em <paramref name="destination"/> o áudio processado do clipe a partir do quadro <paramref name="timelineFrame"/>.</summary>
    public void Render(Clip clip, MediaAsset asset, string path, long timelineFrame, Span<float> destination)
    {
        var restart = timelineFrame != _next || _producer is null;
        var signature = (asset.Id, clip.Start, clip.SourceIn, clip.Speed, clip.PreservePitch);
        if (signature != _signature || path != _path)
        {
            _signature = signature;
            restart = true;
        }
        _asset = asset;
        _path = path;
        if (!ReferenceEquals(clip, _clipRef))
        {
            _clipRef = clip;
            if (_chain.Update(EffectCatalog.ActiveAudioEffects(clip))) restart = true;
        }

        var clipStart = AudioFormat.ToFrames(clip.Start);
        if (restart) Restart(clip, timelineFrame, clipStart);

        _producer!.Read(destination);
        _chain.Process(destination);
        if (ApplyGainEnvelope) ApplyGain(clip, timelineFrame - clipStart, destination);
        _next = timelineFrame + destination.Length / Ch;
    }

    private void Restart(Clip clip, long timelineFrame, long clipStart)
    {
        _chain.Reset();
        var preRoll = _chain.PreRollFrames;
        var latency = _chain.LatencyFrames;
        var speed = clip.Speed.Value;
        var startOffset = timelineFrame - preRoll - clipStart; // em quadros da timeline, relativo ao início do clipe
        var sourceStart = AudioFormat.ToFrames(clip.SourceIn) + startOffset * speed;

        if (Math.Abs(speed - 1) < 1e-9) _producer = new DirectProducer(ReadSource, (long)Math.Round(sourceStart));
        else if (clip.PreservePitch) _producer = new TimeStretchProducer(ReadSource, sourceStart, speed);
        else _producer = new ResampleProducer(ReadSource, sourceStart, speed);

        // Aquece a cadeia e compensa a latência, descartando a saída.
        var toDiscard = preRoll + latency;
        while (toDiscard > 0)
        {
            var n = Math.Min(toDiscard, _discard.Length / Ch);
            var span = _discard.AsSpan(0, n * Ch);
            _producer.Read(span);
            _chain.Process(span);
            toDiscard -= n;
        }
    }

    private void ReadSource(long position, Span<float> destination)
    {
        destination.Clear();
        if (position < 0)
        {
            var skip = (int)Math.Min(-position, destination.Length / Ch);
            destination = destination[(skip * Ch)..];
            position += skip;
            if (destination.IsEmpty) return;
        }
        source.Read(_asset, _path, AudioFormat.FromFrames(position), destination);
    }

    private static void ApplyGain(Clip clip, long offsetFrames, Span<float> s)
    {
        var frames = s.Length / Ch;
        var g0 = (float)ClipAnimation.GainAt(clip, AudioFormat.FromFrames(offsetFrames));
        for (var b = 0; b < frames; b += GainStep)
        {
            var n = Math.Min(GainStep, frames - b);
            var g1 = (float)ClipAnimation.GainAt(clip, AudioFormat.FromFrames(offsetFrames + b + n));
            for (var i = 0; i < n; i++)
            {
                var g = g0 + (g1 - g0) * i / n;
                s[(b + i) * Ch] *= g;
                s[(b + i) * Ch + 1] *= g;
            }
            g0 = g1;
        }
    }
}
