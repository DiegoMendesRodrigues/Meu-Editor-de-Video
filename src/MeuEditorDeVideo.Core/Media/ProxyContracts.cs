using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.Core.Media;

public enum ProxyState
{
    /// <summary>Sem proxy (a pré-visualização usa o original).</summary>
    None,
    Queued,
    Generating,
    Ready,
    Failed,
}

/// <summary>Situação do proxy de uma mídia; <see cref="Progress"/> vai de 0 a 1 durante a geração.</summary>
public sealed record ProxyStatus(Guid AssetId, ProxyState State, double Progress = 0, string? Error = null);

/// <summary>
/// Proxies de edição (§3.2, §6.5): cópias H.264 em 540p (lado menor), só vídeo, guardadas no cache local e usadas apenas
/// pela pré-visualização. A exportação e as capturas em resolução total sempre leem o original.
/// </summary>
public interface IProxyService
{
    event EventHandler<ProxyStatus>? StatusChanged;

    ProxyStatus StatusOf(MediaAsset asset, string sourcePath);

    /// <summary>Caminho do proxy pronto, ou nulo.</summary>
    string? ReadyPath(MediaAsset asset, string sourcePath);

    /// <summary>Coloca a mídia na fila de geração (ignorado se já estiver pronta, na fila ou em geração).</summary>
    void Request(MediaAsset asset, string sourcePath);

    /// <summary>Cancela a geração pendente/em andamento e apaga o proxy.</summary>
    void Delete(MediaAsset asset, string sourcePath);
}

public static class ProxyPolicy
{
    /// <summary>Lado menor do proxy.</summary>
    public const int ShortSide = 540;

    /// <summary>Mídias de vídeo podem ter proxy (imagens, GIFs e sequências já são leves ou não se beneficiam).</summary>
    public static bool Supports(MediaAsset asset) => asset.Kind == MediaKind.Video && asset.Width > 0 && asset.Height > 0;

    /// <summary>Geração automática: vídeos acima de 1080p (ex.: 1440p, 4K).</summary>
    public static bool IsRecommended(MediaAsset asset) =>
        Supports(asset) && (Math.Max(asset.Width, asset.Height) > 1920 || Math.Min(asset.Width, asset.Height) > 1080);

    /// <summary>
    /// Dimensões do proxy para uma imagem exibida em <paramref name="width"/> × <paramref name="height"/> (já com rotação):
    /// lado menor = 540 (sem ampliar), proporção mantida, valores pares (exigência do H.264 4:2:0).
    /// </summary>
    public static (int Width, int Height) SizeFor(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var scale = Math.Min(1.0, (double)ShortSide / Math.Min(width, height));
        static int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
        return (Even(width * scale), Even(height * scale));
    }
}
