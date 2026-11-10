using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.Services.Help;

/// <summary>
/// Texto de "Ajuda › Informações do sistema": versão, Windows, hardware, aceleração, modelos de IA e pastas. Fica só na tela
/// (o usuário pode copiar para pedir suporte); nada é enviado a lugar nenhum.
/// </summary>
public static class SystemInfoReport
{
    public static string Build(string appVersion, HardwareInfo hardware, IEnumerable<InstalledModel> models, IAppPaths paths,
        string? osDescription = null, string? runtime = null)
    {
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder();
        void Line(string label, string value) => sb.Append(label).Append(": ").AppendLine(value);
        string Gb(long bytes) => bytes > 0 ? (bytes / 1073741824.0).ToString("0.0", pt) + " GB" : "desconhecida";

        sb.AppendLine("Meu Editor de Vídeo — informações do sistema");
        sb.AppendLine();
        Line("Versão", appVersion);
        Line("Windows", osDescription ?? RuntimeInformation.OSDescription);
        Line("Arquitetura", RuntimeInformation.OSArchitecture.ToString());
        Line(".NET", runtime ?? RuntimeInformation.FrameworkDescription);
        sb.AppendLine();

        Line("Processador", $"{(hardware.CpuName.Length > 0 ? hardware.CpuName : "desconhecido")} ({hardware.LogicalProcessors} núcleos lógicos)");
        Line("Memória", $"{Gb(hardware.TotalMemoryBytes)} (livre: {Gb(hardware.AvailableMemoryBytes)})");
        if (hardware.Gpus.Count == 0) Line("Placa de vídeo", "não detectada");
        foreach (var gpu in hardware.Gpus)
            Line("Placa de vídeo", $"{gpu.Name} — {Gb(gpu.VideoMemoryBytes)}{(gpu.Dedicated ? " (dedicada)" : "")}");
        Line("Codificação por hardware", hardware.HardwareEncoders.Count > 0 ? string.Join(", ", hardware.HardwareEncoders) : "nenhuma (usa o processador)");
        Line("Decodificação por hardware", hardware.HardwareDecoding ? "disponível (D3D11VA)" : "indisponível");
        foreach (var disk in hardware.Disks)
            Line($"Disco ({disk.Label})", $"{disk.Root} — livre {Gb(disk.FreeBytes)} de {Gb(disk.TotalBytes)}");
        sb.AppendLine();

        var list = models.OrderBy(m => m.Manifest.Name, StringComparer.CurrentCulture).ToList();
        sb.AppendLine(list.Count == 0 ? "Modelos de IA: nenhum instalado" : "Modelos de IA:");
        foreach (var m in list)
            sb.Append("  - ").Append(m.Manifest.Name).Append(' ').Append(m.Manifest.Version)
              .Append(m.BuiltIn ? " (incluído)" : "").AppendLine();
        sb.AppendLine();

        Line("Configurações", paths.SettingsFile);
        Line("Dados locais", paths.LocalData);
        Line("Logs", paths.Logs);
        Line("Cache", paths.Cache);
        Line("Modelos de IA", paths.AiModels);
        return sb.ToString();
    }
}
