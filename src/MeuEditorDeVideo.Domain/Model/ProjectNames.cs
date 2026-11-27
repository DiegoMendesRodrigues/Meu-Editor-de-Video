// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
namespace MeuEditorDeVideo.Domain.Model;

/// <summary>
/// Regra do nome de projeto, igual em todos os sistemas: o nome vira o arquivo .med, que pode ser levado ao Windows
/// (no Linux e no macOS <c>Path.GetInvalidFileNameChars</c> só recusa a barra e o NUL).
/// </summary>
public static class ProjectNames
{
    private const string Forbidden = "\\/:*?\"<>|";

    public static bool HasInvalidChars(string name) => name.Any(c => c < ' ' || Forbidden.Contains(c));
}
