using System.Globalization;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>Sessão gráfica do Linux, que decide como a tela pode ser gravada.</summary>
public enum LinuxSession
{
    /// <summary>X11 (Xorg): captura com x11grab.</summary>
    X11,

    /// <summary>Wayland: a captura exigiria o portal do sistema (ScreenCast + PipeWire), que não é usado aqui.</summary>
    Wayland,

    /// <summary>Sem sessão gráfica.</summary>
    None,
}

/// <summary>Um dispositivo do AVFoundation (macOS): índice e nome.</summary>
public sealed record AvfDevice(int Index, string Name);

/// <summary>Dispositivos do AVFoundation separados por tipo (telas aparecem como "Capture screen N").</summary>
public sealed record AvfDevices(IReadOnlyList<AvfDevice> Video, IReadOnlyList<AvfDevice> Audio)
{
    /// <summary>Câmeras: os dispositivos de vídeo que não são telas.</summary>
    public IReadOnlyList<AvfDevice> Cameras => [.. Video.Where(d => !IsScreen(d.Name))];

    /// <summary>Telas ("Capture screen N"), em ordem.</summary>
    public IReadOnlyList<AvfDevice> Screens => [.. Video.Where(d => IsScreen(d.Name))];

    public static bool IsScreen(string name) => name.StartsWith("Capture screen", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Janela do X11 lida das ferramentas do sistema (wmctrl ou xwininfo).</summary>
public sealed record X11Window(long Id, string Title, int X, int Y, int Width, int Height, long Pid);

/// <summary>
/// Montagem das linhas de comando do ffmpeg e leitura da saída das ferramentas do sistema, por sistema operacional.
/// Tudo função pura (o texto das ferramentas entra como parâmetro), para testar os três sistemas em qualquer máquina.
/// </summary>
public static partial class RecordingCommandLines
{
    public const string MacPermissionMessage =
        "O macOS não permitiu a gravação. Em Ajustes do Sistema › Privacidade e Segurança, permita a Gravação de Tela, a Câmera e o Microfone para este aplicativo e tente de novo.";

    public const string WaylandMessage =
        "A gravação da tela não funciona numa sessão Wayland. Entre numa sessão X11 (escolha \"Xorg\" na tela de login) para gravar a tela; a webcam e o microfone continuam disponíveis.";

    public const string NoGraphicalSessionMessage =
        "Não há uma sessão gráfica (variável DISPLAY) para gravar a tela.";

    // ------------------------------------------------------------ argumentos do ffmpeg

    /// <summary>
    /// Argumentos comuns: entradas (tela, webcam, microfone, já no formato de cada sistema), sobreposição da webcam e
    /// codificação H.264 + AAC num MP4. Uma lista nula = aquela fonte não é gravada.
    /// </summary>
    public static IReadOnlyList<string> Compose(
        ScreenRecordingOptions o, int screenWidth,
        IReadOnlyList<string>? screenInput, IReadOnlyList<string>? webcamInput, IReadOnlyList<string>? microphoneInput)
    {
        if (screenInput is null && webcamInput is null) throw new InvalidOperationException("Escolha gravar a tela, a webcam ou as duas.");
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "error" };
        var fps = Fps(o);
        int index = 0, screen = -1, cam = -1, mic = -1;
        if (screenInput is not null)
        {
            args.AddRange(screenInput);
            screen = index++;
        }
        if (webcamInput is not null)
        {
            args.AddRange(webcamInput);
            cam = index++;
        }
        if (microphoneInput is not null)
        {
            args.AddRange(microphoneInput);
            mic = index++;
        }

        string filter;
        if (screen >= 0 && cam >= 0)
        {
            var w = Math.Max(160, (int)Math.Round(screenWidth * Math.Clamp(o.WebcamSize, 0.05, 0.5) / 2) * 2);
            const int margin = 24;
            var (x, y) = o.Corner switch
            {
                WebcamCorner.BottomLeft => ($"{margin}", $"main_h-overlay_h-{margin}"),
                WebcamCorner.TopRight => ($"main_w-overlay_w-{margin}", $"{margin}"),
                WebcamCorner.TopLeft => ($"{margin}", $"{margin}"),
                _ => ($"main_w-overlay_w-{margin}", $"main_h-overlay_h-{margin}"),
            };
            filter = $"[{cam}:v]scale={w}:-2[cam];[{screen}:v][cam]overlay={x}:{y}:shortest=0,scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p[v]";
        }
        else
        {
            filter = $"[{(screen >= 0 ? screen : cam)}:v]scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p[v]";
        }
        args.AddRange(["-filter_complex", filter, "-map", "[v]"]);
        if (mic >= 0) args.AddRange(["-map", $"{mic}:a", "-c:a", "aac", "-b:a", "160k"]);
        args.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-r", fps, "-movflags", "+faststart", o.OutputPath]);
        return args;
    }

    private static string Fps(ScreenRecordingOptions o) => Math.Clamp(o.FrameRate, 5, 60).ToString(CultureInfo.InvariantCulture);

    /// <summary>Windows: gdigrab (tela) e DirectShow (webcam e microfone).</summary>
    public static IReadOnlyList<string> BuildWindows(ScreenRecordingOptions o, int screenWidth)
    {
        var inv = CultureInfo.InvariantCulture;
        List<string>? screen = null;
        if (o.Screen)
        {
            screen = ["-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", Fps(o), "-draw_mouse", o.ShowCursor ? "1" : "0"];
            // Uma janela é gravada como a área dela na tela: a captura pelo título (title=) sai preta em programas
            // desenhados pela placa de vídeo (navegadores, WPF, apps da Store).
            if (o.Area is { } a)
                screen.AddRange(["-offset_x", a.X.ToString(inv), "-offset_y", a.Y.ToString(inv), "-video_size", $"{a.Width}x{a.Height}", "-i", "desktop"]);
            else
                screen.AddRange(["-i", "desktop"]);
        }
        List<string>? cam = o.Webcam is { } webcam ? ["-thread_queue_size", "1024", "-f", "dshow", "-rtbufsize", "256M", "-i", "video=" + webcam] : null;
        List<string>? mic = o.Microphone is { } microphone ? ["-thread_queue_size", "1024", "-f", "dshow", "-i", "audio=" + microphone] : null;
        return Compose(o, screenWidth, screen, cam, mic);
    }

    /// <summary>
    /// Linux (X11): x11grab na área <paramref name="capture"/> (obrigatória com a tela), v4l2 para a webcam e PulseAudio
    /// (também o servidor pulse do PipeWire) para o microfone.
    /// </summary>
    public static IReadOnlyList<string> BuildLinux(ScreenRecordingOptions o, string display, ScreenArea? capture)
    {
        var inv = CultureInfo.InvariantCulture;
        List<string>? screen = null;
        if (o.Screen)
        {
            if (capture is null) throw new InvalidOperationException("Não foi possível descobrir o tamanho da tela.");
            screen =
            [
                "-thread_queue_size", "1024", "-f", "x11grab", "-framerate", Fps(o), "-draw_mouse", o.ShowCursor ? "1" : "0",
                "-video_size", $"{capture.Width}x{capture.Height}", "-i", $"{display}+{capture.X.ToString(inv)},{capture.Y.ToString(inv)}",
            ];
        }
        List<string>? cam = o.Webcam is { } webcam ? ["-thread_queue_size", "1024", "-f", "v4l2", "-i", V4lPath(webcam)] : null;
        List<string>? mic = o.Microphone is { } microphone
            ? ["-thread_queue_size", "1024", "-f", "pulse", "-i", string.IsNullOrWhiteSpace(microphone) ? "default" : microphone]
            : null;
        return Compose(o, capture?.Width ?? 1920, screen, cam, mic);
    }

    /// <summary>
    /// macOS: AVFoundation. As fontes são entradas separadas, cada uma com seu índice (<c>"N:none"</c> para vídeo e
    /// <c>":N"</c> para áudio). Os índices vêm de <see cref="ParseAvfoundationDevices"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildMac(ScreenRecordingOptions o, int screenWidth, int? screenIndex, int? cameraIndex, int? microphoneIndex)
    {
        List<string>? screen = null;
        if (o.Screen)
        {
            if (screenIndex is not { } s) throw new InvalidOperationException("Nenhuma tela foi encontrada para gravar.");
            screen = ["-thread_queue_size", "1024", "-f", "avfoundation", "-framerate", Fps(o), "-capture_cursor", o.ShowCursor ? "1" : "0", "-i", $"{s}:none"];
        }
        List<string>? cam = null;
        if (o.Webcam is not null)
        {
            if (cameraIndex is not { } c) throw new InvalidOperationException($"A câmera \"{o.Webcam}\" não foi encontrada.");
            cam = ["-thread_queue_size", "1024", "-f", "avfoundation", "-i", $"{c}:none"];
        }
        List<string>? mic = null;
        if (o.Microphone is not null)
        {
            if (microphoneIndex is not { } m) throw new InvalidOperationException($"O microfone \"{o.Microphone}\" não foi encontrado.");
            mic = ["-thread_queue_size", "1024", "-f", "avfoundation", "-i", $":{m}"];
        }
        return Compose(o, screenWidth, screen, cam, mic);
    }

    // ------------------------------------------------------------ Windows (DirectShow)

    /// <summary>Lê a lista do DirectShow: linhas <c>"Nome" (video)</c> e <c>"Nome" (audio)</c>; nomes alternativos são ignorados.</summary>
    public static CaptureDevices ParseDirectShowDevices(string output)
    {
        var cams = new List<string>();
        var mics = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase)) continue;
            var m = DirectShowLine().Match(line);
            if (!m.Success) continue;
            var list = m.Groups[2].Value.Equals("video", StringComparison.OrdinalIgnoreCase) ? cams : mics;
            if (!list.Contains(m.Groups[1].Value)) list.Add(m.Groups[1].Value);
        }
        return new CaptureDevices(cams, mics);
    }

    [GeneratedRegex("\"([^\"]+)\"\\s*\\((video|audio)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex DirectShowLine();

    // ------------------------------------------------------------ Linux

    /// <summary>Descobre a sessão gráfica pelas variáveis de ambiente (injetadas para testar).</summary>
    public static LinuxSession DetectLinuxSession(Func<string, string?> env)
    {
        static bool Has(string? v) => !string.IsNullOrWhiteSpace(v);
        var type = env("XDG_SESSION_TYPE")?.Trim().ToLowerInvariant();
        if (type == "wayland") return LinuxSession.Wayland;
        if (type == "x11") return Has(env("DISPLAY")) ? LinuxSession.X11 : LinuxSession.None;
        // Sem tipo de sessão: Wayland vence mesmo com DISPLAY (o XWayland captura só janelas X, em preto).
        if (Has(env("WAYLAND_DISPLAY"))) return LinuxSession.Wayland;
        return Has(env("DISPLAY")) ? LinuxSession.X11 : LinuxSession.None;
    }

    /// <summary>Microfones de <c>pactl list short sources</c>: "default" primeiro; as fontes <c>.monitor</c> (som do sistema) ficam de fora.</summary>
    public static IReadOnlyList<string> ParsePactlSources(string output)
    {
        var mics = new List<string> { "default" };
        foreach (var line in output.Split('\n'))
        {
            var cols = line.Split('\t');
            if (cols.Length < 2) continue;
            var name = cols[1].Trim();
            if (name.Length == 0 || name.EndsWith(".monitor", StringComparison.OrdinalIgnoreCase) || mics.Contains(name)) continue;
            mics.Add(name);
        }
        return mics;
    }

    /// <summary>
    /// Câmeras a partir de <c>/dev/videoN</c> e <c>/sys/class/video4linux/videoN/name</c>. Só entra o nó de captura
    /// (<c>index</c> 0, ou sem o arquivo); os nós de metadados de uma mesma câmera ficam de fora. Formato: <c>Nome (/dev/videoN)</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseVideo4LinuxCameras(IEnumerable<(string Node, string? Name, string? Index)> nodes)
    {
        var result = new List<string>();
        foreach (var (node, name, index) in nodes.OrderBy(n => VideoNumber(n.Node)))
        {
            if (index is not null && index.Trim() != "0") continue;
            var title = string.IsNullOrWhiteSpace(name) ? "Câmera" : name.Trim();
            result.Add($"{title} ({node})");
        }
        return result;
    }

    private static int VideoNumber(string node)
    {
        var m = VideoNode().Match(node);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : int.MaxValue;
    }

    [GeneratedRegex(@"/dev/video(\d+)")]
    private static partial Regex VideoNode();

    /// <summary>Caminho <c>/dev/videoN</c> a partir do nome mostrado ao usuário (ou o próprio texto, se já for um caminho).</summary>
    public static string V4lPath(string cameraName)
    {
        var m = VideoNode().Match(cameraName);
        return m.Success ? m.Value : cameraName;
    }

    /// <summary>Monitores de <c>xrandr --current</c> (linhas <c>HDMI-1 connected primary 1920x1080+0+0</c>); o principal primeiro, depois da esquerda para a direita.</summary>
    public static IReadOnlyList<ScreenArea> ParseXrandr(string output)
    {
        var found = new List<(int X, int Y, int W, int H, bool Primary)>();
        foreach (var line in output.Split('\n'))
        {
            var m = XrandrLine().Match(line);
            if (!m.Success) continue;
            found.Add((Int(m, 4), Int(m, 5), Int(m, 2), Int(m, 3), m.Groups[1].Success));
        }
        return Number(found);
    }

    [GeneratedRegex(@"^\S+\s+connected\s+(primary\s+)?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)")]
    private static partial Regex XrandrLine();

    /// <summary>Tamanho da tela de <c>xdpyinfo</c> (linha <c>dimensions: 1920x1080 pixels</c>), usado quando não há xrandr.</summary>
    public static IReadOnlyList<ScreenArea> ParseXdpyinfo(string output)
    {
        var m = XdpyDimensions().Match(output);
        return m.Success ? Number([(0, 0, Int(m, 1), Int(m, 2), true)]) : [];
    }

    [GeneratedRegex(@"dimensions:\s+(\d+)x(\d+)\s+pixels")]
    private static partial Regex XdpyDimensions();

    private static IReadOnlyList<ScreenArea> Number(List<(int X, int Y, int W, int H, bool Primary)> found) =>
        [.. found.OrderByDescending(m => m.Primary).ThenBy(m => m.X).ThenBy(m => m.Y)
            .Select((m, i) => new ScreenArea(i + 1, m.X, m.Y, m.W / 2 * 2, m.H / 2 * 2, m.Primary))];

    private static int Int(Match m, int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    /// <summary>Retângulo da janela recortado pela tela, com largura e altura pares (exigência do H.264); null se não sobra área.</summary>
    public static ScreenArea? WindowArea((int Left, int Top, int Right, int Bottom) window, (int X, int Y, int Width, int Height) screen)
    {
        var left = Math.Max(window.Left, screen.X);
        var top = Math.Max(window.Top, screen.Y);
        var right = Math.Min(window.Right, screen.X + screen.Width);
        var bottom = Math.Min(window.Bottom, screen.Y + screen.Height);
        var w = (right - left) / 2 * 2;
        var h = (bottom - top) / 2 * 2;
        return w < 16 || h < 16 ? null : new ScreenArea(0, left, top, w, h, false);
    }

    /// <summary>Retângulo que cobre todos os monitores (a área de trabalho virtual), com tamanho par; nulo se não há monitores.</summary>
    public static ScreenArea? BoundingArea(IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0) return null;
        var x = screens.Min(s => s.X);
        var y = screens.Min(s => s.Y);
        var w = screens.Max(s => s.X + s.Width) - x;
        var h = screens.Max(s => s.Y + s.Height) - y;
        return new ScreenArea(0, x, y, w / 2 * 2, h / 2 * 2, false);
    }

    /// <summary>Janelas de <c>wmctrl -lGp</c>: <c>id desktop pid x y w h máquina título</c>. Janelas fixas (desktop -1: painéis) são ignoradas.</summary>
    public static IReadOnlyList<X11Window> ParseWmctrl(string output)
    {
        var list = new List<X11Window>();
        foreach (var line in output.Split('\n'))
        {
            var m = WmctrlLine().Match(line);
            if (!m.Success || m.Groups[2].Value == "-1") continue;
            list.Add(new X11Window(Hex(m.Groups[1].Value), m.Groups[9].Value.Trim(), Int(m, 4), Int(m, 5), Int(m, 6), Int(m, 7), long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)));
        }
        return list;
    }

    [GeneratedRegex(@"^(0x[0-9a-fA-F]+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(\d+)\s+(\d+)\s+(\S+)\s+(.*)$")]
    private static partial Regex WmctrlLine();

    /// <summary>Ids de <c>xprop -root _NET_CLIENT_LIST</c> (<c>window id # 0x1400003, 0x1600004</c>).</summary>
    public static IReadOnlyList<long> ParseXpropClientList(string output)
    {
        var hash = output.IndexOf('#');
        if (hash < 0) return [];
        return [.. HexId().Matches(output[hash..]).Select(m => Hex(m.Value))];
    }

    [GeneratedRegex("0x[0-9a-fA-F]+")]
    private static partial Regex HexId();

    /// <summary>Pid de <c>xprop -id … _NET_WM_PID</c> (<c>_NET_WM_PID(CARDINAL) = 1234</c>); 0 se ausente.</summary>
    public static long ParseXpropPid(string output)
    {
        var m = XpropPid().Match(output);
        return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    [GeneratedRegex(@"_NET_WM_PID\(CARDINAL\)\s*=\s*(\d+)")]
    private static partial Regex XpropPid();

    /// <summary>Janela de <c>xwininfo -id …</c>: título, posição absoluta e tamanho; nula se não está visível (<c>IsViewable</c>) ou faltam dados.</summary>
    public static X11Window? ParseXwininfo(string output)
    {
        var head = XwininfoHead().Match(output);
        var x = XwininfoNumber("Absolute upper-left X").Match(output);
        var y = XwininfoNumber("Absolute upper-left Y").Match(output);
        var w = XwininfoNumber("Width").Match(output);
        var h = XwininfoNumber("Height").Match(output);
        if (!head.Success || !x.Success || !y.Success || !w.Success || !h.Success) return null;
        if (!output.Contains("IsViewable", StringComparison.Ordinal)) return null;
        return new X11Window(Hex(head.Groups[1].Value), head.Groups[2].Success ? head.Groups[2].Value : "", Int(x, 1), Int(y, 1), Int(w, 1), Int(h, 1), 0);
    }

    [GeneratedRegex("Window id:\\s+(0x[0-9a-fA-F]+)(?:\\s+\"(.*)\")?")]
    private static partial Regex XwininfoHead();

    private static Regex XwininfoNumber(string label) => new($@"{Regex.Escape(label)}:\s+(-?\d+)", RegexOptions.CultureInvariant);

    private static long Hex(string value) => Convert.ToInt64(value[2..], 16);

    // ------------------------------------------------------------ macOS

    /// <summary>
    /// Lê <c>ffmpeg -f avfoundation -list_devices true -i ""</c> (na saída de erro): seções "video devices" e "audio devices"
    /// com linhas <c>[AVFoundation indev @ 0x…] [N] Nome</c>.
    /// </summary>
    public static AvfDevices ParseAvfoundationDevices(string output)
    {
        var video = new List<AvfDevice>();
        var audio = new List<AvfDevice>();
        List<AvfDevice>? current = null;
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("video devices", StringComparison.OrdinalIgnoreCase)) { current = video; continue; }
            if (line.Contains("audio devices", StringComparison.OrdinalIgnoreCase)) { current = audio; continue; }
            if (current is null) continue;
            var m = AvfLine().Match(line);
            if (m.Success) current.Add(new AvfDevice(Int(m, 1), m.Groups[2].Value));
        }
        return new AvfDevices(video, audio);
    }

    [GeneratedRegex(@"\]\s*\[(\d+)\]\s+(.+?)\s*$")]
    private static partial Regex AvfLine();

    /// <summary>Resoluções de <c>system_profiler SPDisplaysDataType</c> (linhas <c>Resolution: 3024 x 1964 Retina</c>), na ordem das telas.</summary>
    public static IReadOnlyList<(int Width, int Height)> ParseMacResolutions(string output) =>
        [.. MacResolution().Matches(output).Select(m => (Int(m, 1), Int(m, 2)))];

    [GeneratedRegex(@"Resolution:\s+(\d+)\s*x\s*(\d+)")]
    private static partial Regex MacResolution();

    // ------------------------------------------------------------ mensagens de falha

    /// <summary>Mensagem amigável (pt-BR) para falhas conhecidas do ffmpeg no <paramref name="os"/>; nula se o log não é reconhecido.</summary>
    public static string? FriendlyFailure(MediaOs os, string log)
    {
        switch (os)
        {
            case MediaOs.MacOs:
                if (log.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                    || log.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
                    || log.Contains("Cannot Open", StringComparison.OrdinalIgnoreCase)
                    || log.Contains("Error opening input", StringComparison.OrdinalIgnoreCase)
                    || log.Contains("Input/output error", StringComparison.OrdinalIgnoreCase))
                    return MacPermissionMessage;
                return null;
            case MediaOs.Linux:
                if (log.Contains("Unknown input format", StringComparison.OrdinalIgnoreCase))
                    return "Esta versão do ffmpeg não inclui a captura necessária (x11grab, pulse ou v4l2).";
                if (log.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) && log.Contains("/dev/video", StringComparison.Ordinal))
                    return "Sem permissão para usar a câmera. Adicione seu usuário ao grupo \"video\" e entre de novo na sessão.";
                return null;
            default:
                return null;
        }
    }
}
