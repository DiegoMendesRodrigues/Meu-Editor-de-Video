using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Whisper.net;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// Reconhecimento de fala local com Whisper (whisper.cpp via Whisper.net) — RF142, RF157, RF158. Antes de transcrever,
/// localiza os trechos com fala (Silero VAD quando instalado; senão, detector por energia) e transcreve só esses trechos,
/// o que evita frases inventadas no silêncio e deixa o início das legendas preciso.
/// </summary>
public sealed class WhisperTranscriber(IModelRegistry registry, IAudioSampleSourceFactory audio, OnnxSessionFactory? sessions = null)
    : ISpeechRecognitionService
{
    private const int Rate = 16000;

    public bool IsAvailable => registry.IsAvailable(ModelCapability.SpeechRecognition);

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        string language, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || registry.Find(ModelCapability.SpeechRecognition) is not { } model)
            throw new InvalidOperationException("O modelo de reconhecimento de fala não está instalado.");

        return Task.Run(() => RunAsync(model.ModelPath, project, clip, paths, language, progress, cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<TranscriptSegment>> RunAsync(string modelPath, Project project, Clip clip,
        IReadOnlyDictionary<Guid, string> paths, string language, IProgress<double>? progress, CancellationToken ct)
    {
        var stereo = ClipAudio.RenderStereo(audio, project, clip, paths, new Scaled(progress, 0, 0.15), ct);
        var mono = ClipAudio.ToMono16k(stereo);
        var chunks = SpeechChunks.Group(SpeechRegions(mono, ct), Rate);
        progress?.Report(0.2);

        var segments = new List<TranscriptSegment>();
        if (chunks.Count == 0)
        {
            progress?.Report(1);
            return segments;
        }

        using var factory = WhisperFactory.FromPath(modelPath);
        var total = chunks.Sum(c => (double)(c.End - c.Start));
        double done = 0;
        foreach (var (start, end) in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var length = end - start;
            var before = done;
            await using var processor = factory.CreateBuilder()
                .WithLanguage(language)
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8))
                .WithProgressHandler(p => progress?.Report(0.2 + 0.8 * (before + length * p / 100.0) / total))
                .Build();

            var offset = (double)start / Rate;
            var chunkEnd = (double)end / Rate;
            await foreach (var s in processor.ProcessAsync(mono.AsMemory((int)start, (int)length).ToArray(), ct))
            {
                var text = TranscriptCleanup.Clean(s.Text);
                if (text.Length == 0) continue;
                var from = clip.Start + MediaTime.FromSecondsApprox(offset + s.Start.TotalSeconds);
                var to = MediaTime.Min(clip.End, clip.Start + MediaTime.FromSecondsApprox(Math.Min(chunkEnd, offset + s.End.TotalSeconds)));
                if (from >= clip.End || to <= from) continue;
                segments.Add(new TranscriptSegment(from, to, text));
            }
            done += length;
        }
        progress?.Report(1);
        return segments;
    }

    /// <summary>Trechos com fala (amostras): Silero VAD se instalado; senão, energia.</summary>
    private IReadOnlyList<(long Start, long End)> SpeechRegions(float[] mono, CancellationToken ct)
    {
        float[] probs;
        if (sessions is not null && registry.IsAvailable(ModelCapability.VoiceActivity) && registry.Find(ModelCapability.VoiceActivity) is { } vad)
        {
            using var session = sessions.Create(vad);
            probs = new SileroVad(session.Session).Probabilities(mono, null, ct);
        }
        else
        {
            probs = SpeechChunks.EnergyProbabilities(mono, SileroVad.WindowSamples);
        }
        return SpeechSegments.FromProbabilities(probs, SileroVad.WindowSamples, mono.Length, 0.5f, Rate,
            minSpeechMs: 200, minSilenceMs: 500, padMs: 250);
    }

    /// <summary>Reescala o progresso de uma etapa para a faixa [offset, offset + scale].</summary>
    private sealed class Scaled(IProgress<double>? inner, double offset, double scale) : IProgress<double>
    {
        public void Report(double value) => inner?.Report(offset + scale * value);
    }
}
