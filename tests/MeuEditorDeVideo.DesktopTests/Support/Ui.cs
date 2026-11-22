using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using MeuEditorDeVideo.Desktop;
using MeuEditorDeVideo.Presentation.Input;

namespace MeuEditorDeVideo.DesktopTests.Support;

/// <summary>Construtor do aplicativo para a sessão sem tela: o App real (tema, paleta, estilos, fonte de ícones).</summary>
public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Sessão única do Avalonia sem tela para todos os testes (o Avalonia só pode ser iniciado uma vez por processo).
/// Todo acesso a controles passa por <see cref="Run"/> ou <see cref="Run{T}"/>, na thread da interface.
/// </summary>
public static class Ui
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder)));

    public static void Run(Action action) => Session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

    public static T Run<T>(Func<T> func) => Session.Value.Dispatch(func, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Modificador principal simulado: Cmd no macOS, Ctrl nos demais (os dois enums têm os mesmos valores).</summary>
    public static RawInputModifiers Principal => (RawInputModifiers)(int)MedModifiers.Control.ToAvalonia();

    /// <summary>Pasta das capturas: tests/.../bin/.../telas (o caminho completo vai na saída do teste).</summary>
    public static string ScreenshotsFolder { get; } = Path.Combine(AppContext.BaseDirectory, "telas");

    /// <summary>
    /// Mostra a janela, processa o layout e o desenho, salva a captura em <see cref="ScreenshotsFolder"/>/name.png e
    /// devolve o caminho. Chame dentro de <see cref="Run"/>.
    /// </summary>
    public static string Capture(Window window, string name)
    {
        if (!window.IsVisible) window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        // Toda tela capturada confere também o texto dos botões, com os dados do teste.
        Assert.Empty(Centralizacao.Desvios(window));
        // using: sem referência viva, o coletor libera os pixels durante o Save (o Avalonia codifica sem copiá-los).
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro desenhado.");
        Directory.CreateDirectory(ScreenshotsFolder);
        var path = Path.Combine(ScreenshotsFolder, name + ".png");
        // PNG padrão; a sobrecarga com BitmapEncoderOptions não acrescenta nada aqui.
#pragma warning disable CS0618
        frame.Save(path);
#pragma warning restore CS0618
        return path;
    }

    /// <summary>Janela de teste com um conteúdo (painel, controle) para capturar.</summary>
    public static Window Host(Control content, double width = 800, double height = 600) =>
        new() { Width = width, Height = height, Content = content, Background = Avalonia.Media.Brushes.Transparent };
}
