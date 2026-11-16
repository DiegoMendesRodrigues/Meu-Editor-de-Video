using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>
/// Gravação de tela (gdigrab), webcam e microfone (DirectShow) ao mesmo tempo com o <c>ffmpeg.exe</c> que acompanha o
/// aplicativo. A webcam entra num canto sobre a tela; o resultado é um MP4 (H.264 + AAC). Tudo local, sem rede.
/// </summary>
public sealed partial class ScreenRecorder : IScreenRecorder
{
    private static string? Executable()
    {
        try { FFmpegRuntime.EnsureInitialized(); }
        catch (Exception) { return null; }
        var exe = FFmpegRuntime.Directory is { } dir ? Path.Combine(dir, "ffmpeg.exe") : null;
        return exe is not null && File.Exists(exe) ? exe : null;
    }

    public bool IsAvailable => Executable() is not null;

    public async Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default)
    {
        var exe = Executable() ?? throw new InvalidOperationException("O ffmpeg.exe não foi encontrado na pasta do aplicativo.");
        var psi = NewStartInfo(exe, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível listar os dispositivos.");
        var stderr = p.StandardError.ReadToEndAsync(ct);
        _ = p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return ParseDevices(await stderr);
    }

    /// <summary>Lê a lista do DirectShow: linhas <c>"Nome" (video)</c> e <c>"Nome" (audio)</c>; nomes alternativos são ignorados.</summary>
    public static CaptureDevices ParseDevices(string output)
    {
        var cams = new List<string>();
        var mics = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase)) continue;
            var m = DeviceLine().Match(line);
            if (!m.Success) continue;
            var list = m.Groups[2].Value.Equals("video", StringComparison.OrdinalIgnoreCase) ? cams : mics;
            if (!list.Contains(m.Groups[1].Value)) list.Add(m.Groups[1].Value);
        }
        return new CaptureDevices(cams, mics);
    }

    [GeneratedRegex("\"([^\"]+)\"\\s*\\((video|audio)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceLine();

    public async Task<IScreenRecording> StartAsync(ScreenRecordingOptions options, CancellationToken ct = default)
    {
        var exe = Executable() ?? throw new InvalidOperationException("O ffmpeg.exe não foi encontrado na pasta do aplicativo.");
        if (options.Window is { } window) options = options with { Area = PrepareWindow(window, options.Area), Window = null };
        var args = BuildArguments(options, options.Area?.Width ?? VirtualScreenWidth());
        var dir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var process = Process.Start(NewStartInfo(exe, args, redirectInput: true))
                      ?? throw new InvalidOperationException("Não foi possível iniciar a gravação.");
        var recording = new Recording(process, options.OutputPath);
        // Erros de dispositivo aparecem logo: espera um pouco para avisar já no início.
        await Task.WhenAny(process.WaitForExitAsync(ct), Task.Delay(1500, ct));
        if (process.HasExited)
        {
            var log = recording.Log;
            await recording.DisposeAsync();
            throw new InvalidOperationException("A gravação não começou. " + Summary(log));
        }
        return recording;
    }

    /// <summary>Argumentos do ffmpeg: entradas (tela, webcam, microfone), sobreposição da webcam e codificação.</summary>
    public static IReadOnlyList<string> BuildArguments(ScreenRecordingOptions o, int screenWidth)
    {
        if (!o.Screen && o.Webcam is null) throw new InvalidOperationException("Escolha gravar a tela, a webcam ou as duas.");
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "error" };
        var fps = Math.Clamp(o.FrameRate, 5, 60).ToString(CultureInfo.InvariantCulture);
        int index = 0, screen = -1, cam = -1, mic = -1;
        if (o.Screen)
        {
            args.AddRange(["-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", fps, "-draw_mouse", o.ShowCursor ? "1" : "0"]);
            // Uma janela é gravada como a área dela na tela (ver PrepareWindow): a captura pelo título (title=) sai
            // preta em programas desenhados pela placa de vídeo (navegadores, WPF, apps da Store).
            if (o.Area is { } a)
            {
                var inv = CultureInfo.InvariantCulture;
                args.AddRange(["-offset_x", a.X.ToString(inv), "-offset_y", a.Y.ToString(inv), "-video_size", $"{a.Width}x{a.Height}", "-i", "desktop"]);
            }
            else
            {
                args.AddRange(["-i", "desktop"]);
            }
            screen = index++;
        }
        if (o.Webcam is { } webcam)
        {
            args.AddRange(["-thread_queue_size", "1024", "-f", "dshow", "-rtbufsize", "256M", "-i", "video=" + webcam]);
            cam = index++;
        }
        if (o.Microphone is { } microphone)
        {
            args.AddRange(["-thread_queue_size", "1024", "-f", "dshow", "-i", "audio=" + microphone]);
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

    private static ProcessStartInfo NewStartInfo(string exe, IEnumerable<string> args, bool redirectInput = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = redirectInput,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static string Summary(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "Verifique se a câmera e o microfone não estão em uso por outro programa." : string.Join(" ", lines.TakeLast(3));
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    public IReadOnlyList<ScreenArea> Screens()
    {
        if (!OperatingSystem.IsWindows()) return [];
        // Coordenadas físicas (as do gdigrab), mesmo com escalas diferentes por monitor.
        var previous = SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);
        try
        {
            var found = new List<(int X, int Y, int W, int H, bool Primary)>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfoW(monitor, ref info))
                {
                    var r = info.Monitor;
                    found.Add((r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (info.Flags & 1) != 0));
                }
                return true;
            }, IntPtr.Zero);
            // Principal primeiro; depois da esquerda para a direita.
            return [.. found.OrderByDescending(m => m.Primary).ThenBy(m => m.X).ThenBy(m => m.Y)
                .Select((m, i) => new ScreenArea(i + 1, m.X, m.Y, m.W / 2 * 2, m.H / 2 * 2, m.Primary))];
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    public IReadOnlyList<CaptureWindow> Windows(ScreenArea? area)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var previous = SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);
        try
        {
            var own = Environment.ProcessId;
            var found = new List<CaptureWindow>();
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                if ((GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return true;
                if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                var length = GetWindowTextLengthW(hwnd);
                if (length == 0) return true;
                var buffer = new char[length + 1];
                var title = new string(buffer, 0, GetWindowTextW(hwnd, buffer, buffer.Length)).Trim();
                if (title.Length == 0 || title == "Program Manager") return true;
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == own || !GetWindowRect(hwnd, out var r) || r.Right - r.Left < 80 || r.Bottom - r.Top < 60) return true;
                if (area is { } a)
                {
                    var cx = (r.Left + r.Right) / 2;
                    var cy = (r.Top + r.Bottom) / 2;
                    if (cx < a.X || cx >= a.X + a.Width || cy < a.Y || cy >= a.Y + a.Height) return true;
                }
                string process;
                try { using var p = Process.GetProcessById((int)pid); process = p.ProcessName; }
                catch (ArgumentException) { process = ""; }
                catch (InvalidOperationException) { process = ""; }
                found.Add(new CaptureWindow(hwnd.ToInt64(), title, process));
                return true;
            }, IntPtr.Zero);
            return found;
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>
    /// Traz a janela para a frente (restaurando-a se minimizada) e devolve a área dela na tela, sem a sombra da borda,
    /// presa à tela <paramref name="screen"/> (ou à área de trabalho virtual) e com largura e altura pares.
    /// </summary>
    private static ScreenArea PrepareWindow(CaptureWindow window, ScreenArea? screen)
    {
        var hwnd = new IntPtr(window.Handle);
        if (!IsWindow(hwnd)) throw new InvalidOperationException($"A janela \"{window.Title}\" foi fechada. Escolha outra janela.");
        var previous = SetThreadDpiAwarenessContext(DpiAwarenessPerMonitorV2);
        try
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);
            Thread.Sleep(300);
            if (DwmGetWindowBounds(hwnd, DwmwaExtendedFrameBounds, out Rect r, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(hwnd, out r))
                throw new InvalidOperationException($"Não foi possível localizar a janela \"{window.Title}\".");
            var bounds = screen is { } s ? (s.X, s.Y, s.Width, s.Height) : VirtualScreen();
            return WindowArea((r.Left, r.Top, r.Right, r.Bottom), bounds)
                   ?? throw new InvalidOperationException($"A janela \"{window.Title}\" está fora da tela escolhida.");
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

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

    private static (int X, int Y, int Width, int Height) VirtualScreen() =>
        (GetSystemMetrics(76), GetSystemMetrics(77), VirtualScreenWidth(), Math.Max(1, GetSystemMetrics(79)));

    private const int SwRestore = 9;
    private const int DwmwaExtendedFrameBounds = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hwnd);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static partial int DwmGetWindowBounds(IntPtr hwnd, int attribute, out Rect value, int size);

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const int DwmwaCloaked = 14;

    private delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(IntPtr hwnd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowTextW(IntPtr hwnd, [Out] char[] text, int max);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private static readonly IntPtr DpiAwarenessPerMonitorV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    private static int VirtualScreenWidth()
    {
        const int SmCxVirtualScreen = 78;
        var w = OperatingSystem.IsWindows() ? GetSystemMetrics(SmCxVirtualScreen) : 0;
        return w > 0 ? w : 1920;
    }

    private sealed class Recording : IScreenRecording
    {
        private readonly Process _process;
        private readonly string _output;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly StringBuilder _log = new();
        private readonly Task _drain;

        public Recording(Process process, string output)
        {
            _process = process;
            _output = output;
            _drain = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                    lock (_log) _log.AppendLine(line);
            });
            _ = process.StandardOutput.ReadToEndAsync();
        }

        public string Log
        {
            get { lock (_log) return _log.ToString(); }
        }

        public TimeSpan Elapsed => _clock.Elapsed;

        public bool IsRunning => !_process.HasExited;

        public async Task<string> StopAsync()
        {
            _clock.Stop();
            if (!_process.HasExited)
            {
                try
                {
                    // "q" encerra o ffmpeg normalmente (o MP4 é finalizado).
                    await _process.StandardInput.WriteAsync('q');
                    await _process.StandardInput.FlushAsync();
                }
                catch (IOException) { }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await _process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
            }
            await Task.WhenAny(_drain, Task.Delay(2000));
            if (!File.Exists(_output) || new FileInfo(_output).Length == 0)
                throw new InvalidOperationException("A gravação falhou. " + Summary(Log));
            return _output;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await _process.WaitForExitAsync();
            }
            _process.Dispose();
        }
    }
}
