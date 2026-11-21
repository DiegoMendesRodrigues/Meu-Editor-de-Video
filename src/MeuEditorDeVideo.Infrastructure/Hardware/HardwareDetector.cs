using System.Runtime.Versioning;
using MeuEditorDeVideo.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace MeuEditorDeVideo.Infrastructure.Hardware;

/// <summary>
/// Detecta CPU, memória, placas de vídeo, discos e aceleração por hardware (§12.3).
/// A detecção nunca lança exceção: falhas de registro, de arquivos do sistema ou de programas de consulta são registradas no log.
/// </summary>
public sealed class HardwareDetector : IHardwareInfoService
{
    private const string CpuKeyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string DisplayClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const long OneGiB = 1024L * 1024 * 1024;

    private static readonly string[] NonDedicatedMarkers =
    [
        "Microsoft Basic", "Basic Render", "Remote Display", "Virtual", "Parsec", "Hyper-V", "Meta Virtual",
    ];

    private readonly IReadOnlyList<(string Label, string Path)> _folders;
    private readonly Func<IReadOnlyList<string>>? _encoders;
    private readonly Func<bool>? _hardwareDecoding;
    private readonly ILogger<HardwareDetector> _logger;
    private readonly object _gate = new();
    private HardwareInfo? _cached;

    public HardwareDetector(
        IEnumerable<(string Label, string Path)> folders,
        Func<IReadOnlyList<string>>? encoders = null,
        Func<bool>? hardwareDecoding = null,
        ILogger<HardwareDetector>? logger = null)
    {
        _folders = folders.ToList();
        _encoders = encoders;
        _hardwareDecoding = hardwareDecoding;
        _logger = logger ?? NullLogger<HardwareDetector>.Instance;
    }

    /// <summary>Indica se a placa é dedicada: memória própria ≥ 1 GB e não é adaptador básico, remoto ou virtual.</summary>
    public static bool IsDedicated(string name, long videoMemoryBytes)
    {
        foreach (var marker in NonDedicatedMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return videoMemoryBytes >= OneGiB;
    }

    public HardwareInfo Get(bool refresh = false)
    {
        lock (_gate)
        {
            if (_cached is null)
            {
                _cached = Detect();
                return _cached;
            }

            if (refresh)
            {
                var (total, available) = ReadMemory();
                _cached = _cached with
                {
                    TotalMemoryBytes = total,
                    AvailableMemoryBytes = available,
                    Disks = ReadDisks(),
                };
            }

            return _cached;
        }
    }

    private HardwareInfo Detect()
    {
        var (total, available) = ReadMemory();
        return new HardwareInfo
        {
            CpuName = ReadCpuName(),
            LogicalProcessors = Environment.ProcessorCount,
            TotalMemoryBytes = total,
            AvailableMemoryBytes = available,
            Gpus = ReadGpus(),
            Disks = ReadDisks(),
            HardwareEncoders = ReadEncoders(),
            HardwareDecoding = ReadHardwareDecoding(),
        };
    }

    private string ReadCpuName()
    {
        var name = "";
        try
        {
            if (OperatingSystem.IsWindows()) name = ReadWindowsCpuName();
            else if (OperatingSystem.IsLinux()) name = File.Exists("/proc/cpuinfo") ? HardwareParsing.ParseCpuInfoModelName(File.ReadAllText("/proc/cpuinfo")) : "";
            else if (OperatingSystem.IsMacOS()) name = CollapseSpaces(ProcessRunner.Run("sysctl", "-n machdep.cpu.brand_string") ?? "");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Não foi possível ler o nome do processador.");
        }

        if (name.Length > 0) return name;
        var fallback = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        return string.IsNullOrWhiteSpace(fallback) ? "Processador desconhecido" : CollapseSpaces(fallback);
    }

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CpuKeyPath);
        return key?.GetValue("ProcessorNameString") is string raw && raw.Trim().Length > 0 ? CollapseSpaces(raw) : "";
    }

    private static string CollapseSpaces(string value) => HardwareParsing.CollapseSpaces(value);

    private (long Total, long Available) ReadMemory() => SystemMemory.Read();

    private List<GpuInfo> ReadGpus()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindowsGpus();
            if (OperatingSystem.IsLinux()) return ReadLinuxGpus();
            if (OperatingSystem.IsMacOS())
            {
                var json = ProcessRunner.Run("system_profiler", "SPDisplaysDataType -json", timeoutMs: 8000);
                return json is null ? [] : [.. HardwareParsing.ParseSystemProfilerDisplays(json, SystemMemory.Read().Total)];
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Não foi possível detectar as placas de vídeo.");
        }

        return [];
    }

    /// <summary>Placas em <c>/sys/class/drm/card*/device</c> (ids PCI; memória de vídeo só quando o driver informa, como o AMD).</summary>
    private List<GpuInfo> ReadLinuxGpus()
    {
        var gpus = new List<GpuInfo>();
        const string drm = "/sys/class/drm";
        if (!Directory.Exists(drm)) return gpus;

        foreach (var card in Directory.GetDirectories(drm, "card*").Order(StringComparer.Ordinal))
        {
            // Ignora as saídas de vídeo ("card0-HDMI-A-1"): só "cardN" tem o dispositivo.
            if (!Path.GetFileName(card).Skip(4).All(char.IsAsciiDigit)) continue;
            var device = Path.Combine(card, "device");
            var vendorFile = Path.Combine(device, "vendor");
            if (!File.Exists(vendorFile)) continue;

            var deviceId = Path.Combine(device, "device");
            var name = HardwareParsing.GpuNameFromPci(File.ReadAllText(vendorFile), File.Exists(deviceId) ? File.ReadAllText(deviceId) : "");
            var vramFile = Path.Combine(device, "mem_info_vram_total");
            var vram = File.Exists(vramFile) ? HardwareParsing.ParseNumber(File.ReadAllText(vramFile)) : 0;
            if (gpus.All(g => g.Name != name || g.VideoMemoryBytes != vram))
                gpus.Add(new GpuInfo(name, vram, IsDedicated(name, vram)));
        }

        return gpus;
    }

    [SupportedOSPlatform("windows")]
    private List<GpuInfo> ReadWindowsGpus()
    {
        var gpus = new List<GpuInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKeyPath);
            if (classKey is null)
                return gpus;

            foreach (var subName in classKey.GetSubKeyNames())
            {
                if (!IsAdapterKeyName(subName))
                    continue;

                try
                {
                    using var adapter = classKey.OpenSubKey(subName);
                    if (adapter?.GetValue("DriverDesc") is not string name || name.Trim().Length == 0)
                        continue;

                    name = CollapseSpaces(name);
                    var vram = ReadVideoMemory(adapter);
                    if (seen.Add($"{name}|{vram}"))
                        gpus.Add(new GpuInfo(name, vram, IsDedicated(name, vram)));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    _logger.LogWarning(ex, "Não foi possível ler o adaptador de vídeo {Chave}.", subName);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Não foi possível enumerar as placas de vídeo no registro.");
        }

        return gpus;
    }

    private static bool IsAdapterKeyName(string name) =>
        name.Length == 4 && name.All(char.IsAsciiDigit);

    [SupportedOSPlatform("windows")]
    private static long ReadVideoMemory(RegistryKey adapter)
    {
        // Os valores ficam na própria chave do adaptador, com o ponto no nome ("HardwareInformation.qwMemorySize").
        // O DWORD "MemorySize" satura em 4 GB; o QWORD (drivers WDDM 2.x) traz o valor real.
        if (Number(adapter.GetValue("HardwareInformation.qwMemorySize")) is > 0 and var qword)
            return qword;
        return Math.Max(0, Number(adapter.GetValue("HardwareInformation.MemorySize")));
    }

    /// <summary>Converte um valor de registro (QWORD, DWORD com sinal ou binário) em bytes.</summary>
    public static long Number(object? value) => value switch
    {
        long qword => qword,
        int dword => (uint)dword, // DWORD é lido como int com sinal: 0xFFF00000 vira negativo
        byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
        byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
        _ => 0,
    };

    private IReadOnlyList<DiskInfo> ReadDisks()
    {
        var disks = new List<DiskInfo>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<List<string>>();

        foreach (var (label, path) in _folders)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                    continue;

                if (index.TryGetValue(root, out var position))
                {
                    labels[position].Add(label);
                    continue;
                }

                var drive = new DriveInfo(root);
                index[root] = disks.Count;
                labels.Add([label]);
                disks.Add(new DiskInfo(label, root, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning(ex, "Não foi possível ler o disco da pasta {Pasta}.", path);
            }
        }

        return disks
            .Select((disk, i) => disk with { Label = string.Join(" e ", labels[i]) })
            .ToList();
    }

    private IReadOnlyList<string> ReadEncoders()
    {
        if (_encoders is null)
            return [];

        try
        {
            return _encoders() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível detectar os codificadores de vídeo por hardware.");
            return [];
        }
    }

    private bool ReadHardwareDecoding()
    {
        if (_hardwareDecoding is null)
            return false;

        try
        {
            return _hardwareDecoding();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível detectar a decodificação por hardware (D3D11VA).");
            return false;
        }
    }
}
