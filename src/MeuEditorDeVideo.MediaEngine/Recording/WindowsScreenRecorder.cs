using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.MediaEngine.Recording;

/// <summary>
/// Windows: gravação de tela (gdigrab), webcam e microfone (DirectShow) ao mesmo tempo com o <c>ffmpeg.exe</c> que
/// acompanha o aplicativo. A webcam entra num canto sobre a tela; o resultado é um MP4 (H.264 + AAC). Tudo local, sem rede.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsScreenRecorder : FfmpegRecorderBase
{
    public override async Task<CaptureDevices> ListDevicesAsync(CancellationToken ct = default)
    {
        var exe = Executable() ?? throw new InvalidOperationException(MissingExecutableMessage);
        var psi = NewStartInfo(exe, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível listar os dispositivos.");
        var stderr = p.StandardError.ReadToEndAsync(ct);
        _ = p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return ParseDevices(await stderr);
    }

    /// <summary>Lê a lista do DirectShow: linhas <c>"Nome" (video)</c> e <c>"Nome" (audio)</c>; nomes alternativos são ignorados.</summary>
    public static CaptureDevices ParseDevices(string output) => RecordingCommandLines.ParseDirectShowDevices(output);

    protected override Task<IReadOnlyList<string>> PrepareAsync(ScreenRecordingOptions options, CancellationToken ct)
    {
        if (options.Window is { } window) options = options with { Area = PrepareWindow(window, options.Area), Window = null };
        return Task.FromResult(BuildArguments(options, options.Area?.Width ?? VirtualScreenWidth()));
    }

    /// <summary>Argumentos do ffmpeg: entradas (tela, webcam, microfone), sobreposição da webcam e codificação.</summary>
    public static IReadOnlyList<string> BuildArguments(ScreenRecordingOptions o, int screenWidth) => RecordingCommandLines.BuildWindows(o, screenWidth);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    public override IReadOnlyList<ScreenArea> Screens()
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

    public override IReadOnlyList<CaptureWindow> Windows(ScreenArea? area)
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
        => RecordingCommandLines.WindowArea(window, screen);

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

}
