// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Infrastructure.Hardware;

/// <summary>
/// Funções puras que interpretam textos do Linux (<c>/proc</c>, <c>/sys</c>) e do macOS (<c>sysctl</c>, <c>vm_stat</c>,
/// <c>system_profiler</c>). Nenhuma lança exceção: texto inesperado resulta em valor vazio ou zero.
/// </summary>
public static partial class HardwareParsing
{
    /// <summary>Total e disponível (bytes) a partir de <c>/proc/meminfo</c> (valores em kB). Usa MemAvailable, ou MemFree como reserva.</summary>
    public static (long Total, long Available) ParseMeminfo(string text)
    {
        long total = 0, available = 0, free = 0;
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = Digits(line[(colon + 1)..]) * 1024;
            switch (key)
            {
                case "MemTotal": total = value; break;
                case "MemAvailable": available = value; break;
                case "MemFree": free = value; break;
            }
        }

        return (total, available > 0 ? available : free);
    }

    private static long Digits(string raw)
    {
        var digits = new string(raw.Trim().TakeWhile(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>Primeiro "model name" (ou "Hardware"/"Model" em ARM) de <c>/proc/cpuinfo</c>; vazio se não houver.</summary>
    public static string ParseCpuInfoModelName(string text)
    {
        foreach (var wanted in new[] { "model name", "Hardware", "Model" })
        {
            foreach (var line in text.Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0 || !line[..colon].Trim().Equals(wanted, StringComparison.Ordinal)) continue;
                var value = line[(colon + 1)..].Trim();
                if (value.Length > 0) return CollapseSpaces(value);
            }
        }

        return "";
    }

    /// <summary>Interpreta um número inteiro simples, como a saída de <c>sysctl -n hw.memsize</c> ou <c>mem_info_vram_total</c>.</summary>
    public static long ParseNumber(string text) => Digits(text);

    /// <summary>Memória disponível (livre + inativa + especulativa) a partir da saída de <c>vm_stat</c>.</summary>
    public static long ParseVmStatAvailable(string text)
    {
        var pageSize = 4096L;
        var m = PageSize().Match(text);
        if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
            pageSize = size;

        long pages = 0;
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            if (key is "Pages free" or "Pages inactive" or "Pages speculative")
                pages += Digits(line[(colon + 1)..]);
        }

        return pages * pageSize;
    }

    [GeneratedRegex(@"page size of (\d+) bytes")]
    private static partial Regex PageSize();

    /// <summary>Nome do fabricante pelo identificador PCI (ex.: 0x10de = NVIDIA); null se desconhecido.</summary>
    public static string? PciVendorName(string vendorId) => vendorId.Trim().ToLowerInvariant() switch
    {
        "0x10de" => "NVIDIA",
        "0x1002" or "0x1022" => "AMD",
        "0x8086" => "Intel",
        "0x15ad" => "VMware",
        "0x1af4" or "0x1234" => "QEMU/Virtio",
        "0x1414" => "Microsoft",
        "0x5143" => "Qualcomm",
        _ => null,
    };

    /// <summary>Monta o nome de uma placa Linux a partir dos ids de <c>/sys/class/drm/cardN/device/{vendor,device}</c>.</summary>
    public static string GpuNameFromPci(string vendorId, string deviceId)
    {
        var vendor = PciVendorName(vendorId) ?? $"Fabricante {vendorId.Trim()}";
        var device = deviceId.Trim().ToLowerInvariant();
        return device.Length == 0 ? $"{vendor} GPU" : $"{vendor} GPU [{device}]";
    }

    /// <summary>
    /// Placas da saída de <c>system_profiler SPDisplaysDataType -json</c>. Em Apple Silicon (sem VRAM própria) a memória
    /// é unificada: usa <paramref name="totalMemoryBytes"/>.
    /// </summary>
    public static IReadOnlyList<GpuInfo> ParseSystemProfilerDisplays(string json, long totalMemoryBytes)
    {
        var gpus = new List<GpuInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("SPDisplaysDataType", out var list) || list.ValueKind != JsonValueKind.Array)
                return gpus;

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var name = Text(item, "sppci_model") ?? Text(item, "_name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                name = CollapseSpaces(name);

                var vram = Math.Max(ParseSize(Text(item, "spdisplays_vram")), ParseSize(Text(item, "spdisplays_vram_shared")));
                if (vram == 0 && name.StartsWith("Apple", StringComparison.OrdinalIgnoreCase))
                    vram = totalMemoryBytes; // memória unificada
                gpus.Add(new GpuInfo(name, vram, HardwareDetector.IsDedicated(name, vram)));
            }
        }
        catch (JsonException)
        {
            gpus.Clear();
        }

        return gpus;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Converte textos como "8 GB" ou "1536 MB" em bytes; 0 se não reconhecer.</summary>
    public static long ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var m = SizeText().Match(text);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return 0;
        var factor = m.Groups[2].Value.ToUpperInvariant() switch { "TB" => 1L << 40, "GB" => 1L << 30, "MB" => 1L << 20, _ => 1L << 10 };
        return (long)(n * factor);
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(TB|GB|MB|KB)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeText();

    internal static string CollapseSpaces(string value) => MultipleSpaces().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
