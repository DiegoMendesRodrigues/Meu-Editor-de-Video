using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace MeuEditorDeVideo.Infrastructure.Hardware;

/// <summary>
/// Detecta CPU, memória, placas de vídeo, discos e aceleração por hardware (§12.3).
/// A detecção nunca lança exceção: falhas de registro ou de API do Windows são registradas no log.
/// </summary>
public sealed partial class HardwareDetector : IHardwareInfoService
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
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CpuKeyPath);
            if (key?.GetValue("ProcessorNameString") is string raw && raw.Trim().Length > 0)
                return CollapseSpaces(raw);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Não foi possível ler o nome do processador no registro.");
        }

        var fallback = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        return string.IsNullOrWhiteSpace(fallback) ? "Processador desconhecido" : CollapseSpaces(fallback);
    }

    private static string CollapseSpaces(string value) =>
        MultipleSpaces().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();

    private (long Total, long Available) ReadMemory()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
                return ((long)status.ullTotalPhys, (long)status.ullAvailPhys);

            _logger.LogWarning("GlobalMemoryStatusEx falhou (erro {Erro}).", Marshal.GetLastWin32Error());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Não foi possível consultar a memória do sistema.");
        }

        return (0, 0);
    }

    private List<GpuInfo> ReadGpus()
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
