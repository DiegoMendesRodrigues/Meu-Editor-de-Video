using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MeuEditorDeVideo.Infrastructure.Hardware;

/// <summary>
/// Memória física do computador em Windows, Linux e macOS. Nunca lança exceção: devolve 0 quando não consegue medir.
/// </summary>
public static class SystemMemory
{
    /// <summary>Memória física livre (bytes), ou 0 se não for possível medir.</summary>
    public static long AvailableBytes() => Read().Available;

    /// <summary>Total e livre. O total usa o valor do GC (respeita limites de contêiner) quando disponível.</summary>
    public static (long Total, long Available) Read()
    {
        var (total, available) = (0L, 0L);
        try
        {
            if (OperatingSystem.IsWindows()) (total, available) = ReadWindows();
            else if (OperatingSystem.IsLinux()) (total, available) = ReadLinux();
            else if (OperatingSystem.IsMacOS()) (total, available) = ReadMac();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException
            or EntryPointNotFoundException or SEHException or InvalidOperationException)
        {
            available = 0;
        }

        try
        {
            var gc = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (gc > 0) total = gc;
        }
        catch (InvalidOperationException)
        {
        }

        return (total, available);
    }

    [SupportedOSPlatform("windows")]
    private static (long Total, long Available) ReadWindows()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? ((long)status.ullTotalPhys, (long)status.ullAvailPhys) : (0, 0);
    }

    private static (long Total, long Available) ReadLinux() =>
        File.Exists("/proc/meminfo") ? HardwareParsing.ParseMeminfo(File.ReadAllText("/proc/meminfo")) : (0, 0);

    private static (long Total, long Available) ReadMac() =>
        (HardwareParsing.ParseNumber(ProcessRunner.Run("sysctl", "-n hw.memsize") ?? ""),
         HardwareParsing.ParseVmStatAvailable(ProcessRunner.Run("vm_stat", "") ?? ""));

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}

/// <summary>Executa um programa de consulta do sistema com prazo curto e devolve a saída padrão, ou null em qualquer falha.</summary>
internal static class ProcessRunner
{
    public static string? Run(string fileName, string arguments, int timeoutMs = 3000)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return null;
            }

            return process.ExitCode == 0 && output.Wait(timeoutMs) ? output.Result : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            return null;
        }
    }
}
