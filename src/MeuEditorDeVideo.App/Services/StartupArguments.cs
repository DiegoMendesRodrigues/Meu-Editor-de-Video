using System.IO;

namespace MeuEditorDeVideo.App.Services;

/// <summary>Argumentos da linha de comando: o Windows passa o caminho do projeto ao abrir um arquivo .med (associação do instalador).</summary>
public static class StartupArguments
{
    /// <summary>Primeiro argumento que é um arquivo .med existente (caminho completo), ou null.</summary>
    public static string? ProjectPath(IEnumerable<string> args, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        foreach (var raw in args)
        {
            var arg = raw.Trim().Trim('"');
            if (arg.Length == 0 || arg.StartsWith('-') || arg.StartsWith("/", StringComparison.Ordinal)) continue;
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
