// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Domain.Model;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Cortar e ampliar: recorta o clipe (frações da mídia em cada borda) e amplia a área que sobra para ocupar o quadro do
/// projeto, centralizada (ajuste sem distorção; se a proporção do recorte for diferente da do projeto, sobram faixas).
/// O giro e os keyframes de posição e escala do clipe são removidos. Sem recorte, o clipe volta ao enquadramento original.
/// O tamanho do projeto não muda. Uma única etapa de desfazer.
/// </summary>
public sealed record CropZoomCommand(Guid SequenceId, Guid ClipId, double Left, double Top, double Right, double Bottom) : IEditCommand
{
    public string Description => "Cortar e ampliar";

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var (_, track, clip) =Require(seq, ClipId);
        if (clip.MediaAssetId is not { } assetId || project.FindMedia(assetId) is not { HasVideo: true, Width: > 0, Height: > 0 } asset)
            throw new EditRejectedException("Selecione um clipe de vídeo ou imagem para cortar e ampliar.");
        double l = Math.Clamp(Left, 0, 1), t = Math.Clamp(Top, 0, 1), r = Math.Clamp(Right, 0, 1), b = Math.Clamp(Bottom, 0, 1);
        if (l + r > 0.98 || t + b > 0.98)
            throw new EditRejectedException("A área de corte é pequena demais.");
        EnsureUnlocked(track);

        var updated = clip with
        {
            Transform = Fill(clip.Transform, l, t, r, b, asset.Width, asset.Height, project.Settings.Width, project.Settings.Height),
            Keyframes = clip.Keyframes.Remove(AnimatedProperty.X).Remove(AnimatedProperty.Y)
                .Remove(AnimatedProperty.Scale).Remove(AnimatedProperty.Rotation),
        };
        seq = ReplaceTrack(seq, track with { Clips = track.Clips.Replace(clip, updated) });
        return Commit(project, seq);
    }

    /// <summary>
    /// Transformação com o recorte dado e a escala e a posição que fazem a área recortada caber inteira no quadro,
    /// centralizada. Mantém os espelhamentos e a composição 3D; zera o giro no plano.
    /// </summary>
    public static Transform Fill(Transform current, double left, double top, double right, double bottom,
        int mediaWidth, int mediaHeight, int projectWidth, int projectHeight)
    {
        var fit = CompositionMath.FitScale(mediaWidth, mediaHeight, projectWidth, projectHeight);
        var regionW = mediaWidth * (1 - left - right) * fit;
        var regionH = mediaHeight * (1 - top - bottom) * fit;
        var scale = Math.Min(projectWidth / regionW, projectHeight / regionH);
        var baseT = current with
        {
            X = 0,
            Y = 0,
            ScaleX = scale,
            ScaleY = scale,
            RotationDegrees = 0,
            CropLeft = left,
            CropTop = top,
            CropRight = right,
            CropBottom = bottom,
        };
        // Centro da área recortada com o clipe no centro do quadro; desloca para levá-lo ao centro.
        var (cx, cy) = CompositionMath.ToProject(baseT, (left + 1 - right) / 2, (top + 1 - bottom) / 2, mediaWidth, mediaHeight, fit);
        return baseT with { X = Math.Round(-cx, 3), Y = Math.Round(-cy, 3), ScaleX = Math.Round(scale, 5), ScaleY = Math.Round(scale, 5) };
    }
}
