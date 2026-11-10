using System.IO;

namespace MeuEditorDeVideo.IntegrationTests.Support;

/// <summary>Pastas temporárias isoladas por teste. Nada é gravado em %APPDATA% ou %LOCALAPPDATA% do usuário.</summary>
internal static class TempFiles
{
    public static string NewRoot(string nome)
    {
        var root = Path.Combine(Path.GetTempPath(), "med-integracao", nome + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Remove a pasta (inclusive arquivos somente-leitura). Falhas de limpeza não derrubam o teste.</summary>
    public static void Delete(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
