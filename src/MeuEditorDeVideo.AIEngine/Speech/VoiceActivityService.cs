using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>Detecção de fala (RF165) com Silero VAD: intervalos de fala do clipe em tempo da timeline.</summary>
public sealed class VoiceActivityService(IModelRegistry registry, OnnxSessionFactory sessions, IAudioSampleSourceFactory audio) : IVoiceActivityService
{
    private const int Rate = 16000;

    public bool IsAvailable => registry.IsAvailable(ModelCapability.VoiceActivity);

    public Task<IReadOnlyList<TimeRange>> DetectSpeechAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        double threshold = 0.5, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || registry.Find(ModelCapability.VoiceActivity) is not { } model)
            throw new InvalidOperationException("O modelo de detecção de fala não está instalado.");

        return Task.Run<IReadOnlyList<TimeRange>>(() =>
        {
            var stereo = ClipAudio.RenderStereo(audio, project, clip, paths, new Scaled(progress, 0, 0.4), cancellationToken);
            var mono = ClipAudio.ToMono16k(stereo);

            using var session = sessions.Create(model);
            var probs = new SileroVad(session.Session).Probabilities(mono, new Scaled(progress, 0.4, 0.6), cancellationToken);
            var segments = SpeechSegments.FromProbabilities(probs, SileroVad.WindowSamples, mono.Length, (float)threshold, Rate);

            var ranges = new List<TimeRange>(segments.Count);
            foreach (var (s, e) in segments)
            {
                var start = clip.Start + ClipAudio.SamplesToTime(s, Rate);
                var end = MediaTime.Min(clip.End, clip.Start + ClipAudio.SamplesToTime(e, Rate));
                if (end > start) ranges.Add(new TimeRange(start, end));
            }
            progress?.Report(1);
            return ranges;
        }, cancellationToken);
    }

    /// <summary>Reescala o progresso de uma etapa para a faixa [offset, offset + scale].</summary>
    private sealed class Scaled(IProgress<double>? inner, double offset, double scale) : IProgress<double>
    {
        public void Report(double value) => inner?.Report(offset + scale * value);
    }
}
