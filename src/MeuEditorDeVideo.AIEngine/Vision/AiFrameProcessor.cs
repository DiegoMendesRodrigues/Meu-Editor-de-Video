using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>Efeitos de imagem com IA na renderização (pré-visualização e exportação usam o mesmo caminho).</summary>
public sealed class AiFrameProcessor(ISegmentationService segmentation) : IAiFrameProcessor
{
    public (BgraImage Image, bool AlphaChanged) Apply(EffectInstance effect, BgraImage image, MediaAsset asset, string? path, MediaTime sourceTime,
        Func<int, int, BgraImage?> background, double pixelScale)
    {
        if (effect.Type != EffectCatalog.AiBackground) return (image, false);
        var matte = segmentation.GetMatte(asset, path, sourceTime, image, EffectCatalog.Value(effect, "temporal") / 100);
        return matte is null ? (image, false) : BackgroundCompositor.Apply(image, matte, effect, background, pixelScale);
    }
}
