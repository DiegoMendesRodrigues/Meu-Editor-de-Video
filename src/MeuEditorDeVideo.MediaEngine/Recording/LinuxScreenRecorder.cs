using System.Diagnostics;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>
/// Linux: tela com x11grab (sessão X11), webcam com v4l2 e microfone com PulseAudio (o servidor pulse do PipeWire também
/// serve). Janelas e tamanho da tela vêm das ferramentas do sistema (wmctrl, xprop, xwininfo, xrandr, xdpyinfo), sempre
/// com tempo limite. Em sessão Wayland a tela não é gravada (veja <see cref="ScreenCaptureUnavailableReason"/>); a webcam e o
/// microfone continuam funcionando.
/// </summary>
public sealed class LinuxScreenRecorder : FfmpegRecorderBase
{
    private readonly Func<string, string?> _env;
    private readonly string _videoRoot;
    private readonly string _sysRoot;

    public LinuxScreenRecorder() : this(Environment.GetEnvironmentVariable, "/dev", "/sys/class/video4linux")
    {
    }

    /// <summary>Construtor para testes: ambiente e pastas de dispositivos injetados.</summary>
    internal LinuxScreenRecorder(Func<string, string?> env, string videoRoot, string sysRoot)
    {
        _env = env;
        _videoRoot = videoRoot;
        _sysRoot = sysRoot;
    }

    private LinuxSession Session => RecordingCommandLines.DetectLinuxSession(_env);

    public override string? ScreenCaptureUnavailableReason => Session switch
    {
        LinuxSession.X11 => null,
        LinuxSession.Wayland => RecordingCommandLines.WaylandMessage,
        _ => RecordingCommandLines.NoGraphicalSessionMessage,
    };

    public override Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var sources = RunTool("pactl", ["list", "short", "sources"]);
        var mics = sources is { } s ? RecordingCommandLines.ParsePactlSources(s.Output) : ["default"];
        return new CaptureDevices(Cameras(), mics);
    }, ct);

    private IReadOnlyList<string> Cameras()
    {
        try
        {
            var nodes = new List<(string, string?, string?)>();
            foreach (var path in Directory.EnumerateFiles(_videoRoot, "video*"))
            {
                var node = Path.GetFileName(path);
                if (!node.StartsWith("video", StringComparison.Ordinal) || !node[5..].All(char.IsAsciiDigit) || node.Length == 5) continue;
                nodes.Add(($"/dev/{node}", ReadText(Path.Combine(_sysRoot, node, "name")), ReadText(Path.Combine(_sysRoot, node, "index"))));
            }
            return RecordingCommandLines.ParseVideo4LinuxCameras(nodes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public override IReadOnlyList<ScreenArea> Screens()
    {
        if (Session != LinuxSession.X11) return [];
        if (RunTool("xrandr", ["--current"]) is { } xr && RecordingCommandLines.ParseXrandr(xr.Output) is { Count: > 0 } monitors) return monitors;
        return RunTool("xdpyinfo", []) is { } info ? RecordingCommandLines.ParseXdpyinfo(info.Output) : [];
    }

    public override IReadOnlyList<CaptureWindow> Windows(ScreenArea? area)
    {
        if (Session != LinuxSession.X11) return [];
        var own = Environment.ProcessId;
        var found = new List<CaptureWindow>();
        foreach (var w in ListX11Windows())
        {
            if (w.Pid == own || w.Width < 80 || w.Height < 60 || w.Title.Length == 0) continue;
            if (area is { } a)
            {
                var cx = w.X + w.Width / 2;
                var cy = w.Y + w.Height / 2;
                if (cx < a.X || cx >= a.X + a.Width || cy < a.Y || cy >= a.Y + a.Height) continue;
            }
            found.Add(new CaptureWindow(w.Id, w.Title, ProcessName(w.Pid)));
        }
        return found;
    }

    /// <summary>Janelas visíveis: wmctrl (uma chamada só), ou xprop + xwininfo quando o wmctrl não existe.</summary>
    private static IReadOnlyList<X11Window> ListX11Windows()
    {
        if (RunTool("wmctrl", ["-lGp"]) is { } wm) return RecordingCommandLines.ParseWmctrl(wm.Output);
        if (RunTool("xprop", ["-root", "_NET_CLIENT_LIST"]) is not { } list) return [];
        var result = new List<X11Window>();
        foreach (var id in RecordingCommandLines.ParseXpropClientList(list.Output))
        {
            if (Inspect(id) is not { } info) continue;
            var pid = RunTool("xprop", ["-id", $"0x{id:x}", "_NET_WM_PID"]) is { } p ? RecordingCommandLines.ParseXpropPid(p.Output) : 0;
            result.Add(info with { Pid = pid });
        }
        return result;
    }

    private static X11Window? Inspect(long id) =>
        RunTool("xwininfo", ["-id", $"0x{id:x}"]) is { } r ? RecordingCommandLines.ParseXwininfo(r.Output) : null;

    private static string ProcessName(long pid)
    {
        if (pid <= 0) return "";
        try { return File.ReadAllText($"/proc/{pid}/comm").Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    protected override Task<IReadOnlyList<string>> PrepareAsync(ScreenRecordingOptions options, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        ScreenArea? capture = null;
        if (options.Screen)
        {
            if (ScreenCaptureUnavailableReason is { } reason) throw new InvalidOperationException(reason);
            if (options.Window is { } window)
            {
                capture = PrepareWindow(window, options.Area);
            }
            else
            {
                var screens = Screens();
                capture = options.Area ?? RecordingCommandLines.BoundingArea(screens)
                          ?? throw new InvalidOperationException("Não foi possível descobrir o tamanho da tela (instale xrandr ou xdpyinfo).");
            }
            options = options with { Area = capture, Window = null };
        }
        return RecordingCommandLines.BuildLinux(options, _env("DISPLAY") ?? ":0", capture);
    }, ct);

    /// <summary>Traz a janela para a frente (se houver wmctrl) e devolve a área dela, recortada pela tela e com tamanho par.</summary>
    private ScreenArea PrepareWindow(CaptureWindow window, ScreenArea? screen)
    {
        var id = $"0x{window.Handle:x}";
        var info = Inspect(window.Handle) ?? throw new InvalidOperationException($"A janela \"{window.Title}\" foi fechada. Escolha outra janela.");
        _ = RunTool("wmctrl", ["-i", "-a", id]);
        Thread.Sleep(300);
        info = Inspect(window.Handle) ?? info;
        var bounds = screen is { } s ? (s.X, s.Y, s.Width, s.Height)
            : RecordingCommandLines.BoundingArea(Screens()) is { } v ? (v.X, v.Y, v.Width, v.Height)
            : throw new InvalidOperationException("Não foi possível descobrir o tamanho da tela (instale xrandr ou xdpyinfo).");
        return RecordingCommandLines.WindowArea((info.X, info.Y, info.X + info.Width, info.Y + info.Height), bounds)
               ?? throw new InvalidOperationException($"A janela \"{window.Title}\" está fora da tela escolhida.");
    }

    protected override string? FriendlyFailure(string log) => RecordingCommandLines.FriendlyFailure(Native.MediaOs.Linux, log);
}
