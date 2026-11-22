namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Nomes de teclas que o programa mostra no sistema em que os testes rodam.</summary>
internal static class Teclas
{
    /// <summary>Modificador principal: Cmd no macOS, Ctrl nos demais.</summary>
    public static string Principal { get; } = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
}
