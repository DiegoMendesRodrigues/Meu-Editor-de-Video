// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Immutable;
using MeuEditorDeVideo.Domain.Model;
using static MeuEditorDeVideo.Domain.Editing.TimelineOps;

namespace MeuEditorDeVideo.Domain.Editing;

/// <summary>
/// Aplicar recorte ao vídeo: o tamanho do projeto passa a ser o da área recortada do clipe, em pixels da mídia original
/// (uma gravação 1920×1080 sem 25% da esquerda vira 1440×1080), e a sequência é reenquadrada para essa área preencher o
/// quadro. Os outros clipes visuais acompanham (posição, escala e keyframes delas), mantendo a composição. A exportação
/// sai com as novas dimensões. Uma única etapa de desfazer.
/// </summary>
public sealed record ApplyCropCommand(Guid SequenceId, Guid ClipId) : IEditCommand
{
    public string Description => "Aplicar recorte ao vídeo";

    /// <summary>Novo tamanho do projeto para o recorte do clipe (pares, mínimo 16).</summary>
    public static (int Width, int Height) CroppedSize(Transform t, int imageWidth, int imageHeight)
    {
        var w = imageWidth * (1 - Math.Clamp(t.CropLeft, 0, 1) - Math.Clamp(t.CropRight, 0, 1));
        var h = imageHeight * (1 - Math.Clamp(t.CropTop, 0, 1) - Math.Clamp(t.CropBottom, 0, 1));
        return (Even(w), Even(h));

        static int Even(double v)
        {
            var i = (int)Math.Round(v);
            return Math.Max(16, i - i % 2);
        }
    }

    public Project Apply(Project project)
    {
        var seq = RequireSequence(project, SequenceId);
        var (_, _, clip) = Require(seq, ClipId);
        var t = clip.Transform;
        if (clip.MediaAssetId is not { } assetId || project.FindMedia(assetId) is not { HasVideo: true, Width: > 0, Height: > 0 } asset)
            throw new EditRejectedException("Selecione um clipe de vídeo ou imagem para aplicar o recorte.");
        if (t.CropLeft <= 0 && t.CropTop <= 0 && t.CropRight <= 0 && t.CropBottom <= 0)
            throw new EditRejectedException("O clipe não tem recorte. Ajuste Esquerda, Topo, Direita ou Base na seção Recorte e tente de novo.");
        if (t.CropLeft + t.CropRight >= 1 || t.CropTop + t.CropBottom >= 1)
            throw new EditRejectedException("O recorte não deixa nenhuma parte do vídeo visível.");
        if (t.RotationDegrees % 360 != 0 || t.Is3D)
            throw new EditRejectedException("Para aplicar o recorte, o clipe não pode estar girado. Zere o Giro (e o giro 3D) e tente de novo.");
        if (clip.Keyframes.ContainsKey(AnimatedProperty.X) || clip.Keyframes.ContainsKey(AnimatedProperty.Y) || clip.Keyframes.ContainsKey(AnimatedProperty.Scale))
            throw new EditRejectedException("Para aplicar o recorte, o clipe não pode ter keyframes de posição ou escala. Remova-os e tente de novo.");

        var settings = project.Settings;
        int pw = settings.Width, ph = settings.Height;
        var fit = CompositionMath.FitScale(asset.Width, asset.Height, pw, ph);

        // Área recortada no quadro atual (pixels do projeto, relativos ao centro).
        double u = (t.CropLeft + 1 - t.CropRight) / 2, v = (t.CropTop + 1 - t.CropBottom) / 2;
        var (cx, cy) = CompositionMath.ToProject(t, u, v, asset.Width, asset.Height, fit);
        var regionW = asset.Width * (1 - t.CropLeft - t.CropRight) * fit * Math.Abs(t.ScaleX);
        var regionH = asset.Height * (1 - t.CropTop - t.CropBottom) * fit * Math.Abs(t.ScaleY);

        var (nw, nh) = CroppedSize(t, asset.Width, asset.Height);
        if (nw == pw && nh == ph && Math.Abs(cx) < 0.5 && Math.Abs(cy) < 0.5)
            throw new EditRejectedException("O projeto já tem o tamanho deste recorte.");
        double kx = nw / regionW, ky = nh / regionH;

        project = new ChangeProjectSettingsCommand(settings with { Width = nw, Height = nh }).Apply(project);
        seq = RequireSequence(project, SequenceId);

        foreach (var track in seq.Tracks.Where(tr => tr.IsVisual))
        {
            if (track.Clips.Count == 0) continue;
            var clips = track.Clips.Select(c => Remap(project, c)).ToList();
            if (clips.SequenceEqual(track.Clips)) continue;
            EnsureUnlocked(track);
            seq = ReplaceTrack(seq, WithClips(track, clips));
        }
        return Commit(project, seq);

        // Leva o clipe do quadro antigo para o novo: a área recortada vira o quadro inteiro.
        Clip Remap(Project p, Clip c)
        {
            // Imagens da mídia são ajustadas ao quadro ("contain"); textos e formas já estão em pixels do projeto.
            double sx = kx, sy = ky;
            if (c.MediaAssetId is { } id && p.FindMedia(id) is { Width: > 0, Height: > 0 } m)
            {
                var oldFit = CompositionMath.FitScale(m.Width, m.Height, pw, ph);
                var newFit = CompositionMath.FitScale(m.Width, m.Height, nw, nh);
                sx = oldFit * kx / newFit;
                sy = oldFit * ky / newFit;
            }
            var ct = c.Transform;
            var keys = c.Keyframes;
            keys = Map(keys, AnimatedProperty.X, x => (x - cx) * kx);
            keys = Map(keys, AnimatedProperty.Y, y => (y - cy) * ky);
            keys = Map(keys, AnimatedProperty.Scale, s => s * sx);
            return c with
            {
                Transform = ct with { X = (ct.X - cx) * kx, Y = (ct.Y - cy) * ky, ScaleX = ct.ScaleX * sx, ScaleY = ct.ScaleY * sy },
                Keyframes = keys,
            };
        }

        static ImmutableDictionary<string, ImmutableList<Keyframe>> Map(
            ImmutableDictionary<string, ImmutableList<Keyframe>> keys, string property, Func<double, double> f) =>
            keys.TryGetValue(property, out var list)
                ? keys.SetItem(property, list.Select(k => k with { Value = Math.Round(f(k.Value), 4) }).ToImmutableList())
                : keys;
    }
}
