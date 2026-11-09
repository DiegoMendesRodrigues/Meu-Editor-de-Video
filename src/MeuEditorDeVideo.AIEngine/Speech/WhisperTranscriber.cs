using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Whisper.net;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>Reconhecimento de fala local com Whisper (whisper.cpp via Whisper.net) — RF142, RF157, RF158.</summary>
public sealed class WhisperTranscriber(IModelRegistry registry, IAudioSampleSourceFactory audio) : ISpeechRecognitionService
{
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
        var stereo = ClipAudio.RenderStereo(audio, project, clip, paths, new Scaled(progress, 0, 0.2), ct);
        var mono = ClipAudio.ToMono16k(stereo);

        using var factory = WhisperFactory.FromPath(modelPath);
        await using var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithProgressHandler(p => progress?.Report(0.2 + 0.8 * p / 100.0))
            .Build();

        var segments = new List<TranscriptSegment>();
        await foreach (var s in processor.ProcessAsync(mono, ct))
        {
            var text = TranscriptCleanup.Clean(s.Text);
            if (text.Length == 0) continue;
            var start = clip.Start + MediaTime.FromSecondsApprox(s.Start.TotalSeconds);
            var end = MediaTime.Min(clip.End, clip.Start + MediaTime.FromSecondsApprox(s.End.TotalSeconds));
            if (start >= clip.End || end <= start) continue;
            segments.Add(new TranscriptSegment(start, end, text));
        }
        progress?.Report(1);
        return segments;
    }

    /// <summary>Reescala o progresso de uma etapa para a faixa [offset, offset + scale].</summary>
    private sealed class Scaled(IProgress<double>? inner, double offset, double scale) : IProgress<double>
    {
        public void Report(double value) => inner?.Report(offset + scale * value);
    }
}
