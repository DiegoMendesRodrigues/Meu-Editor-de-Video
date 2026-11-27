// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>
/// macOS: tela, câmera e microfone com o AVFoundation do ffmpeg. As telas são os dispositivos "Capture screen N" (gravadas
/// uma de cada vez; "todas as telas" grava a principal). Gravar uma janela sozinha não é suportado (o macOS não expõe
/// as janelas ao ffmpeg e a lista exigiria CoreGraphics): <see cref="Windows"/> devolve vazio. O macOS pede permissão de
/// Gravação de Tela, Câmera e Microfone; a falta dela vira uma mensagem para o usuário.
/// </summary>
public sealed class MacScreenRecorder : FfmpegRecorderBase
{
    private static readonly string[] ListArguments = ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""];

    private AvfDevices? _devices;

    public override Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var devices = LoadDevices(refresh: true);
        return new CaptureDevices([.. devices.Cameras.Select(d => d.Name)], [.. devices.Audio.Select(d => d.Name)]);
    }, ct);

    /// <summary>Lê a lista do AVFoundation (a saída vem no stderr do ffmpeg, que termina com erro de propósito).</summary>
    private AvfDevices LoadDevices(bool refresh = false)
    {
        if (!refresh && _devices is not null) return _devices;
        var exe = Executable() ?? throw new InvalidOperationException(MissingExecutableMessage);
        var result = RunTool(exe, ListArguments, 10000);
        return _devices = result is { } r ? RecordingCommandLines.ParseAvfoundationDevices(r.Error + "\n" + r.Output) : new AvfDevices([], []);
    }

    public override IReadOnlyList<ScreenArea> Screens()
    {
        if (Executable() is null) return [];
        var screens = LoadDevices().Screens;
        if (screens.Count == 0) return [];
        var resolutions = RunTool("system_profiler", ["SPDisplaysDataType"], 8000) is { } sp
            ? RecordingCommandLines.ParseMacResolutions(sp.Output) : [];
        // Cada tela é gravada em pixels próprios (sem deslocamento): o número é o N de "Capture screen N" mais um.
        return [.. screens.Select((s, i) =>
        {
            var (w, h) = i < resolutions.Count ? resolutions[i] : (1920, 1080);
            return new ScreenArea(ScreenNumber(s.Name) + 1, 0, 0, w / 2 * 2, h / 2 * 2, i == 0);
        })];
    }

    private static int ScreenNumber(string name) =>
        int.TryParse(name.AsSpan("Capture screen".Length).Trim(), out var n) ? n : 0;

    public override IReadOnlyList<CaptureWindow> Windows(ScreenArea? area) => [];

    protected override Task<IReadOnlyList<string>> PrepareAsync(ScreenRecordingOptions options, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        if (options.Window is not null)
            throw new InvalidOperationException("No macOS não é possível gravar uma janela sozinha. Grave a tela inteira.");
        var devices = LoadDevices(refresh: true);
        int? screenIndex = null, camIndex = null, micIndex = null;
        var width = 1920;
        if (options.Screen)
        {
            var wanted = options.Area is { } a ? $"Capture screen {a.Number - 1}" : null;
            var screen = (wanted is null ? null : devices.Screens.FirstOrDefault(s => s.Name == wanted)) ?? devices.Screens.FirstOrDefault()
                         ?? throw new InvalidOperationException(RecordingCommandLines.MacPermissionMessage);
            screenIndex = screen.Index;
            width = options.Area?.Width ?? Screens().FirstOrDefault()?.Width ?? width;
        }
        if (options.Webcam is { } cam)
            camIndex = devices.Cameras.FirstOrDefault(d => d.Name == cam)?.Index;
        if (options.Microphone is { } mic)
            micIndex = devices.Audio.FirstOrDefault(d => d.Name == mic)?.Index;
        // Área escolhida já vale por tela; o deslocamento do ScreenArea não se aplica ao AVFoundation.
        return RecordingCommandLines.BuildMac(options, width, screenIndex, camIndex, micIndex);
    }, ct);

    protected override string? FriendlyFailure(string log) => RecordingCommandLines.FriendlyFailure(MediaOs.MacOs, log);
}
