using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Runtime;

/// <summary>
/// Áudio de um clipe exatamente como ele soa na timeline (velocidade e efeitos de áudio; sem volume/rampas),
/// no formato interno 48 kHz estéreo intercalado — base para os modelos de fala.
/// </summary>
public static class ClipAudio
{
    private const int Chunk = 8192;

    /// <summary>Amostras intercaladas estéreo 48 kHz cobrindo toda a duração do clipe.</summary>
    public static float[] RenderStereo(IAudioSampleSourceFactory factory, Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { HasAudio: true } asset)
            throw new InvalidOperationException("O clipe não tem áudio.");
        if (!paths.TryGetValue(id, out var path))
            throw new InvalidOperationException("A mídia do clipe não foi encontrada.");

        using var source = factory.Create();
        var stream = new ClipStream(source) { ApplyGainEnvelope = false };
        var start = AudioFormat.ToFrames(clip.Start);
        var total = AudioFormat.ToFrames(clip.Duration);
        var result = new float[total * AudioFormat.Channels];
        for (long done = 0; done < total; done += Chunk)
        {
            ct.ThrowIfCancellationRequested();
            var n = (int)Math.Min(Chunk, total - done);
            stream.Render(clip, asset, path, start + done, result.AsSpan((int)(done * AudioFormat.Channels), n * AudioFormat.Channels));
            progress?.Report((double)(done + n) / total);
        }
        return result;
    }

    /// <summary>Mono 16 kHz (entrada dos modelos de fala): média dos canais e decimação 3:1 com filtro passa-baixas.</summary>
    public static float[] ToMono16k(ReadOnlySpan<float> stereo48k)
    {
        var frames = stereo48k.Length / 2;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++) mono[i] = 0.5f * (stereo48k[2 * i] + stereo48k[2 * i + 1]);
        return Resampler.Downsample3(mono);
    }

    public static MediaTime SamplesToTime(long samples, int rate) => MediaTime.FromSecondsApprox((double)samples / rate);
}
