using System.IO;

namespace MeuEditorDeVideo.IntegrationTests.Support;

/// <summary>
/// Localiza modelos de IA: <c>%MED_MODELS_DIR%</c> ou os modelos incluídos em <c>deps\ai-models</c> (copiados para a
/// pasta de saída pelo App, como faz UnitTests/AI/ModelFactAttribute).
/// </summary>
internal static class ModelosDeTeste
{
    public static string? Find(string fileName)
    {
        if (Environment.GetEnvironmentVariable("MED_MODELS_DIR") is { Length: > 0 } dir)
        {
            var p = Path.Combine(dir, fileName);
            if (File.Exists(p)) return p;
        }
        for (var up = AppContext.BaseDirectory; up is not null; up = Path.GetDirectoryName(up))
        {
            var deps = Path.Combine(up, "deps", "ai-models");
            if (!Directory.Exists(deps)) continue;
            if (Directory.EnumerateFiles(deps, fileName, SearchOption.AllDirectories).FirstOrDefault() is { } builtIn) return builtIn;
            break;
        }
        return null;
    }
}

/// <summary>Teste que depende de um modelo de IA: é ignorado (Skip) quando o arquivo não está disponível.</summary>
public sealed class ModeloFactAttribute : FactAttribute
{
    public ModeloFactAttribute(string fileName)
    {
        if (ModelosDeTeste.Find(fileName) is null) Skip = $"Modelo {fileName} não disponível (defina MED_MODELS_DIR).";
    }
}
