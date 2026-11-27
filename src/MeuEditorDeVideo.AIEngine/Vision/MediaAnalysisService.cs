// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Immutable;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>
/// Análise de mídia (etiquetas para a busca da biblioteca) e "Combinar cor" entre clipes. Tudo local: fala pelo modelo de
/// detecção de voz, cenas pela detecção de cortes, pessoa pelo modelo de segmentação (quando instalados).
/// </summary>
public sealed class MediaAnalysisService(
    IVoiceActivityService vad,
    ISceneDetectionService scenes,
    ISegmentationService segmentation,
    IVideoFrameSourceFactory frames) : IMediaAnalysisService
{
    public async Task<ImmutableList<string>> AnalyzeAsync(Project project, MediaAsset asset, IReadOnlyDictionary<Guid, string> paths,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var tags = new List<string>(MediaTags.Basic(asset));
        if (!paths.TryGetValue(asset.Id, out var path)) return [.. tags];
        var clip = new Clip { Id = Guid.NewGuid(), MediaAssetId = asset.Id, Start = MediaTime.Zero, Duration = asset.Duration };
        var timed = asset.Duration > MediaTime.Zero;

        if (timed && asset.HasAudio && vad.IsAvailable)
        {
            var speech = await vad.DetectSpeechAsync(project, clip, paths, 0.5, Part(progress, 0, 0.4), cancellationToken);
            var seconds = speech.Sum(r => (r.End - r.Start).TotalSeconds);
            tags.Add(seconds >= Math.Min(2, asset.Duration.TotalSeconds * 0.1) ? "fala" : "sem fala");
        }
        if (timed && asset.Kind == MediaKind.Video)
        {
            var result = await scenes.DetectAsync(project, clip, paths, 0.5, Part(progress, 0.4, 0.4), cancellationToken);
            var count = result.Cuts.Count + 1;
            tags.Add(count == 1 ? "1 cena" : $"{count} cenas");
        }
        if (asset.HasVideo && segmentation.IsAvailable)
        {
            var person = await Task.Run(() => HasPerson(asset, path, cancellationToken), cancellationToken);
            tags.Add(person ? "pessoa" : "sem pessoas");
        }
        progress?.Report(1);
        return [.. tags.Distinct()];
    }

    /// <summary>Há uma pessoa em pelo menos um de três quadros (máscara cobrindo mais de 3 % da imagem).</summary>
    private bool HasPerson(MediaAsset asset, string path, CancellationToken ct)
    {
        using var source = frames.Create(false);
        foreach (var f in new[] { 0.2, 0.5, 0.8 })
        {
            ct.ThrowIfCancellationRequested();
            var t = new MediaTime((long)(asset.Duration.Ticks * f));
            if (source.GetFrame(asset, path, t, 384, 384) is not { } frame) continue;
            if (segmentation.GetMatte(asset, path, t, frame) is not { } matte) continue;
            var covered = matte.Alpha.Count(a => a > 128);
            if (covered > matte.Alpha.Length * 0.03) return true;
        }
        return false;
    }

    public Task<(double Gain, double Offset)[]> MatchColorAsync(Project project, Clip target, Clip reference, IReadOnlyDictionary<Guid, string> paths,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var a = Stats(project, target, paths, cancellationToken);
        var b = Stats(project, reference, paths, cancellationToken);
        return EffectProcessor.MatchColor(a, b);
    }, cancellationToken);

    /// <summary>Estatística média de três quadros do clipe (sem os efeitos).</summary>
    private (double[] Mean, double[] Std) Stats(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths, CancellationToken ct)
    {
        if (clip.MediaAssetId is not { } id || project.FindMedia(id) is not { HasVideo: true } asset || !paths.TryGetValue(id, out var path))
            throw new InvalidOperationException("O clipe não tem imagem.");
        using var source = frames.Create(false);
        var mean = new double[3];
        var std = new double[3];
        var n = 0;
        foreach (var f in new[] { 0.25, 0.5, 0.75 })
        {
            ct.ThrowIfCancellationRequested();
            var t = clip.FreezeAt ?? clip.SourceTimeAt(clip.Start + new MediaTime((long)(clip.Duration.Ticks * f)));
            if (source.GetFrame(asset, path, t, 256, 256) is not { } frame) continue;
            var (m, s) = EffectProcessor.ColorStats(frame);
            for (var c = 0; c < 3; c++) { mean[c] += m[c]; std[c] += s[c]; }
            n++;
        }
        if (n == 0) throw new InvalidOperationException("Não foi possível ler os quadros do clipe.");
        return (mean.Select(v => v / n).ToArray(), std.Select(v => v / n).ToArray());
    }

    private static Progress<double>? Part(IProgress<double>? progress, double from, double size) =>
        progress is null ? null : new Progress<double>(p => progress.Report(from + p * size));
}
