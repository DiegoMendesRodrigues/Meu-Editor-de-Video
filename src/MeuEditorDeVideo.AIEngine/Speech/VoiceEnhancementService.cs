using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// Isolamento de voz e redução de ruído (RF127, RF159, RF160) com GTCRN. Gera WAVs 48 kHz estéreo com a duração do clipe.
/// </summary>
public sealed class VoiceEnhancementService(IModelRegistry registry, OnnxSessionFactory sessions, IAudioSampleSourceFactory audio) : IVoiceEnhancementService
{
    private const int OutputRate = 48000;
    private const int Channels = 2;

    public bool IsAvailable => registry.IsAvailable(ModelCapability.SpeechEnhancement);

    public Task<VoiceProcessingResult> ProcessAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        VoiceProcessingMode mode, double strength, string outputDirectory, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || registry.Find(ModelCapability.SpeechEnhancement) is not { } model)
            throw new InvalidOperationException("O modelo de melhoria de voz não está instalado.");

        var baseName = BaseName(project, clip);
        var keep = (float)(1 - Math.Clamp(strength, 0, 1));

        return Task.Run(() =>
        {
            var original = ClipAudio.RenderStereo(audio, project, clip, paths, new Scaled(progress, 0, 0.25), cancellationToken);
            var mono16 = ClipAudio.ToMono16k(original);

            float[] voice48;
            using (var session = sessions.Create(model))
            {
                var enh = new GtcrnEnhancer(session.Session).Enhance(mono16, new Scaled(progress, 0.25, 0.65), cancellationToken);
                voice48 = Resampler.Upsample3(enh, original.Length / Channels);
            }
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(outputDirectory);
            var frames = voice48.Length;

            if (mode == VoiceProcessingMode.Isolate)
            {
                var voice = new float[frames * Channels];
                var residual = new float[frames * Channels];
                for (var i = 0; i < frames; i++)
                    for (var c = 0; c < Channels; c++)
                    {
                        voice[2 * i + c] = voice48[i];
                        residual[2 * i + c] = original[2 * i + c] - voice48[i];
                    }

                var voicePath = UniquePath(outputDirectory, baseName, "voz");
                WavFile.Write(voicePath, voice, OutputRate, Channels);
                progress?.Report(0.92);
                var residualPath = UniquePath(outputDirectory, baseName, "ambiente");
                WavFile.Write(residualPath, residual, OutputRate, Channels);
                progress?.Report(1);
                return new VoiceProcessingResult(voicePath, residualPath);
            }

            // Denoise: voz + (1 − força) × (original − voz); força 1 deixa só a voz.
            var denoised = new float[frames * Channels];
            for (var i = 0; i < frames; i++)
                for (var c = 0; c < Channels; c++)
                    denoised[2 * i + c] = voice48[i] + keep * (original[2 * i + c] - voice48[i]);

            var path = UniquePath(outputDirectory, baseName, "sem ruído");
            WavFile.Write(path, denoised, OutputRate, Channels);
            progress?.Report(1);
            return new VoiceProcessingResult(path, null);
        }, cancellationToken);
    }

    private static string BaseName(Project project, Clip clip)
    {
        var name = clip.MediaAssetId is { } id && project.FindMedia(id) is { } asset
            ? Path.GetFileNameWithoutExtension(asset.Name)
            : "Clipe";
        if (string.IsNullOrWhiteSpace(name)) name = "Clipe";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>Caminho livre: "base - sufixo.wav", ou "base - sufixo (2).wav", (3)… se já existir.</summary>
    private static string UniquePath(string directory, string baseName, string suffix)
    {
        var stem = $"{baseName} - {suffix}";
        var path = Path.Combine(directory, stem + ".wav");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(directory, $"{stem} ({n}).wav");
        return path;
    }

    /// <summary>Reescala o progresso de uma etapa para a faixa [offset, offset + scale].</summary>
    private sealed class Scaled(IProgress<double>? inner, double offset, double scale) : IProgress<double>
    {
        public void Report(double value) => inner?.Report(offset + scale * value);
    }
}
