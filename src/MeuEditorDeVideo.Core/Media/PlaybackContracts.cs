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

    /// <summary>
    /// Primeira mídia que não pôde ser decodificada (e a causa). A pré-visualização segue sem ela; a exportação usa isto
    /// para falhar com erro claro em vez de gravar quadros pretos.
    /// </summary>
    (string Media, Exception Error)? Failure => null;
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

/// <summary>Medição de loudness de um clipe (EBU R128) já com efeitos e volume aplicados.</summary>
public sealed record LoudnessMeasurement(double IntegratedLufs, double PeakDbfs)
{
    public bool HasSignal => !double.IsNegativeInfinity(IntegratedLufs);
}

/// <summary>Resultado da sincronização por áudio: novo início do clipe e confiança (0…1).</summary>
public sealed record AudioSyncResult(MediaTime NewStart, double Confidence);

/// <summary>Análises de áudio de clipes (RF126 normalização, RF128 silêncios, RF129 sincronização).</summary>
public interface IAudioAnalysisService
{
    Task<LoudnessMeasurement> MeasureLoudnessAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>Trechos silenciosos do clipe, em tempo da timeline.</summary>
    Task<IReadOnlyList<MeuEditorDeVideo.Domain.Editing.TimeRange>> DetectSilenceAsync(Project project, Clip clip,
        IReadOnlyDictionary<Guid, string> paths, double thresholdDb, MediaTime minDuration, MediaTime padding,
        CancellationToken cancellationToken = default);

    /// <summary>Batidas da música do clipe, em tempo da timeline. <paramref name="sensitivity"/> 0…1 (maior = mais batidas).</summary>
    Task<IReadOnlyList<MediaTime>> DetectBeatsAsync(Project project, Clip clip, IReadOnlyDictionary<Guid, string> paths,
        double sensitivity, CancellationToken cancellationToken = default);

    /// <summary>Onde o clipe <paramref name="other"/> deve começar para que seu áudio coincida com o de <paramref name="reference"/>.</summary>
    Task<AudioSyncResult?> FindSyncAsync(Project project, Clip reference, Clip other, IReadOnlyDictionary<Guid, string> paths,
        CancellationToken cancellationToken = default);
}

/// <summary>Texto para fala com as vozes do sistema (SAPI no Windows, say no macOS, espeak-ng no Linux); local, sem rede.</summary>
public interface ISpeechSynthesisService
{
    bool IsAvailable { get; }

    /// <summary>Mensagem para mostrar quando <see cref="IsAvailable"/> é falso, dizendo o que instalar.</summary>
    string UnavailableReason => "O texto para fala não está disponível neste computador.";

    /// <summary>Nomes das vozes instaladas.</summary>
    IReadOnlyList<string> Voices();

    /// <summary>Gera um WAV com o texto falado. <paramref name="rate"/>: −10 (lento) a 10 (rápido).</summary>
    Task SynthesizeAsync(string text, string? voiceName, int rate, string outputPath, CancellationToken cancellationToken = default);
}
