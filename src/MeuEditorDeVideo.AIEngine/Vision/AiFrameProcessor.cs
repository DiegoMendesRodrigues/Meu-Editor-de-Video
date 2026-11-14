using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>Efeitos de imagem com IA na renderização (pré-visualização e exportação usam o mesmo caminho).</summary>
public sealed class AiFrameProcessor(ISegmentationService segmentation, IFaceLandmarkService faces) : IAiFrameProcessor
{
    public (BgraImage Image, bool AlphaChanged) Apply(EffectInstance effect, BgraImage image, MediaAsset asset, string? path, MediaTime sourceTime,
        Func<int, int, BgraImage?> background, double pixelScale)
    {
        if (effect.Type is EffectCatalog.Beauty or EffectCatalog.Makeup)
        {
            var face = faces.Detect(asset, path, sourceTime, image);
            if (face is null) return (image, false);
            return (effect.Type == EffectCatalog.Beauty ? FaceRetouch.Beauty(image, face, effect) : FaceRetouch.Makeup(image, face, effect), false);
        }
        if (effect.Type == EffectCatalog.FaceBlur)
        {
            var face = faces.Detect(asset, path, sourceTime, image);
            return (face is null ? image : FaceBlur.Apply(image, face, effect), false);
        }
        if (effect.Type == EffectCatalog.CameraTrack)
        {
            var tracked = segmentation.GetMatte(asset, path, sourceTime, image, EffectCatalog.Value(effect, "temporal") / 100);
            return (CameraTracker.Apply(image, tracked, effect, sourceTime.TotalSeconds, pixelScale), false);
        }
        if (effect.Type != EffectCatalog.AiBackground) return (image, false);
        var matte = segmentation.GetMatte(asset, path, sourceTime, image, EffectCatalog.Value(effect, "temporal") / 100);
        return matte is null ? (image, false) : BackgroundCompositor.Apply(image, matte, effect, background, pixelScale);
    }

    public BgraImage? Interpolate(BgraImage a, BgraImage b, double weight) => FrameInterpolator.Interpolate(a, b, weight);
}
