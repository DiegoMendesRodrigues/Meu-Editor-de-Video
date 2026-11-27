// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;

namespace MeuEditorDeVideo.Presentation.Services;

/// <summary>Argumentos da linha de comando: o sistema passa o caminho do projeto ao abrir um arquivo .med (associação do instalador).</summary>
public static class StartupArguments
{
    /// <summary>Primeiro argumento que é um arquivo .med existente (caminho completo), ou null.</summary>
    public static string? ProjectPath(IEnumerable<string> args, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        foreach (var raw in args)
        {
            var arg = raw.Trim().Trim('"');
            if (arg.Length == 0 || arg.StartsWith('-')) continue;
            // "/" inicia uma opção só no Windows; no Linux e no macOS é o começo de todo caminho absoluto.
            if (OperatingSystem.IsWindows() && arg.StartsWith('/')) continue;
            if (!arg.EndsWith(".med", StringComparison.OrdinalIgnoreCase)) continue;
            string full;
            try
            {
                full = Path.GetFullPath(arg);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (exists(full)) return full;
        }
        return null;
    }
}
