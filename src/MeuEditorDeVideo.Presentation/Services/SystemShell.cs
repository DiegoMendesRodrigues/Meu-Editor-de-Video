// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Diagnostics;
using System.IO;

namespace MeuEditorDeVideo.Presentation.Services;

/// <summary>
/// Integração com o sistema: abrir endereços e arquivos no programa padrão e mostrar um arquivo na pasta
/// (Explorador no Windows, Finder no macOS, gerenciador de arquivos no Linux).
/// </summary>
public static class SystemShell
{
    /// <summary>Abre um endereço da web ou um arquivo/pasta no programa padrão do sistema.</summary>
    public static void Open(string target)
    {
        if (OperatingSystem.IsWindows())
            Start(new ProcessStartInfo(target) { UseShellExecute = true });
        else
            Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { target } });
    }

    /// <summary>Abre a pasta do arquivo com ele selecionado (quando o sistema permite).</summary>
    public static void ShowInFolder(string file)
    {
        if (!File.Exists(file))
        {
            if (Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir)) Open(dir);
            return;
        }
        if (OperatingSystem.IsWindows())
            Start(new ProcessStartInfo(WindowsExplorer, $"/select,\"{file}\""));
        else if (OperatingSystem.IsMacOS())
            Start(new ProcessStartInfo("open") { ArgumentList = { "-R", file } });
        else if (Path.GetDirectoryName(file) is { } dir)
            Open(dir); // xdg-open não seleciona o arquivo; abre a pasta
    }

    /// <summary>Explorador pelo caminho completo: só "explorer.exe" faria o Windows procurá-lo antes na pasta do programa.</summary>
    private static string WindowsExplorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static void Start(ProcessStartInfo psi) => Process.Start(psi)?.Dispose();
}
