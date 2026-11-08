using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Core.Media;

/// <summary>Formato interno de áudio da reprodução/mixagem: float 32 bits intercalado, estéreo, 48 kHz.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    public static long ToFrames(MediaTime t) => (long)Math.Round(t.TotalSeconds * SampleRate);

    public static MediaTime FromFrames(long frames) => MediaTime.FromSamples(frames, SampleRate);
}

/// <summary>
/// Fonte de quadros de vídeo para a pré-visualização (§6.5). Otimizada para leitura sequencial:
/// pedidos de instantes crescentes continuam a decodificação sem nova busca.
/// Não é thread-safe: use a partir de uma única thread de renderização.
/// </summary>
public interface IVideoFrameSource : IDisposable
{
    /// <summary>
    /// Quadro da mídia no instante de origem, redimensionado para caber em <paramref name="maxWidth"/> × <paramref name="maxHeight"/>
    /// (proporção preservada, rotação aplicada). Nulo se a mídia não tiver vídeo ou não puder ser lida.
    /// </summary>
    BgraImage? GetFrame(MediaAsset asset, string path, MediaTime sourceTime, int maxWidth, int maxHeight);

    /// <summary>Libera decodificadores de mídias que não estão mais em uso.</summary>
    void Trim(IReadOnlySet<Guid> inUse);
}

/// <summary>Fonte de amostras de áudio no formato <see cref="AudioFormat"/>. Não é thread-safe.</summary>
public interface IAudioSampleSource : IDisposable
{
    /// <summary>
    /// Preenche <paramref name="destination"/> (amostras intercaladas estéreo) a partir de <paramref name="sourceTime"/>.
    /// Retorna quantos quadros (pares de amostras) foram escritos; o restante deve ser tratado como silêncio.
    /// </summary>
    int Read(MediaAsset asset, string path, MediaTime sourceTime, Span<float> destination);

    void Trim(IReadOnlySet<Guid> inUse);
}

public interface IVideoFrameSourceFactory
{
    IVideoFrameSource Create(bool hardwareDecoding);
}

public interface IAudioSampleSourceFactory
{
    IAudioSampleSource Create();
}
