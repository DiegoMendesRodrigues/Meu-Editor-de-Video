namespace MeuEditorDeVideo.Core.Media;

/// <summary>Dispositivos de captura (DirectShow) disponíveis: câmeras e microfones.</summary>
public sealed record CaptureDevices(IReadOnlyList<string> Cameras, IReadOnlyList<string> Microphones);

/// <summary>Posição da webcam sobre a tela na gravação.</summary>
public enum WebcamCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>Um monitor, em pixels físicos da área de trabalho virtual (as coordenadas que o gdigrab usa).</summary>
public sealed record ScreenArea(int Number, int X, int Y, int Width, int Height, bool IsPrimary)
{
    // Dois textos inteiros: um modelo com valores colados ("{0}{1}") não dá para reconhecer ao traduzir.
    public override string ToString() => IsPrimary ? $"Tela {Number} (principal) — {Width} × {Height}" : $"Tela {Number} — {Width} × {Height}";
}

/// <summary>Uma janela aberta que pode ser gravada sozinha (a área dela na tela, no início da gravação).</summary>
public sealed record CaptureWindow(long Handle, string Title, string ProcessName)
{
    public override string ToString() => string.IsNullOrEmpty(ProcessName) ? Title : $"{Title} ({ProcessName})";
}

/// <summary>O que gravar: a tela (um monitor ou todos), uma webcam e um microfone, ao mesmo tempo.</summary>
/// <param name="Webcam">Nome da câmera; nulo = sem webcam.</param>
/// <param name="Microphone">Nome do microfone; nulo = sem som.</param>
/// <param name="WebcamSize">Largura da webcam em fração da largura da tela gravada (só com a tela).</param>
/// <param name="Area">Monitor a gravar; nulo = todos os monitores.</param>
/// <param name="Window">Janela a gravar no lugar da tela inteira; nulo = a tela (ou as telas) inteira.</param>
public sealed record ScreenRecordingOptions(
    string OutputPath,
    bool Screen,
    string? Webcam,
    string? Microphone,
    int FrameRate = 30,
    bool ShowCursor = true,
    WebcamCorner Corner = WebcamCorner.BottomRight,
    double WebcamSize = 0.1,
    ScreenArea? Area = null,
    CaptureWindow? Window = null);

/// <summary>Gravação em andamento.</summary>
public interface IScreenRecording : IAsyncDisposable
{
    TimeSpan Elapsed { get; }

    bool IsRunning { get; }

    /// <summary>Encerra a gravação e espera o arquivo ser finalizado. Retorna o caminho do arquivo.</summary>
    /// <exception cref="InvalidOperationException">A gravação falhou (mensagem para o usuário).</exception>
    Task<string> StopAsync();
}

/// <summary>Gravação local de tela, webcam e microfone (sem rede).</summary>
public interface IScreenRecorder
{
    bool IsAvailable { get; }

    /// <summary>Por que a tela não pode ser gravada neste sistema (ex.: sessão Wayland); nulo = pode. Webcam e microfone não dependem disso.</summary>
    string? ScreenCaptureUnavailableReason => null;

    Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default);

    /// <summary>Monitores conectados (o principal primeiro).</summary>
    IReadOnlyList<ScreenArea> Screens();

    /// <summary>Janelas visíveis (não minimizadas) cujo centro está na tela <paramref name="area"/> (nula = qualquer tela).</summary>
    IReadOnlyList<CaptureWindow> Windows(ScreenArea? area);

    /// <exception cref="InvalidOperationException">Não foi possível começar (mensagem para o usuário).</exception>
    Task<IScreenRecording> StartAsync(ScreenRecordingOptions options, CancellationToken ct = default);
}
