using System.IO;

namespace MeuEditorDeVideo.UnitTests.AI;

/// <summary>
/// Localiza arquivos de modelos de IA para os testes. Os modelos não fazem parte do repositório (pacotes .medmodel
/// separados): procura em <c>%MED_MODELS_DIR%</c> e nas pastas dos modelos instalados
/// (<c>%LOCALAPPDATA%\MeuEditorDeVideo\ai-models\*\</c>).
/// </summary>
public static class TestModels
{
    public static string? Find(string fileName)
    {
        if (Environment.GetEnvironmentVariable("MED_MODELS_DIR") is { Length: > 0 } dir)
        {
            var p = Path.Combine(dir, fileName);
            if (File.Exists(p)) return p;
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeuEditorDeVideo", "ai-models");
        if (!Directory.Exists(installed)) return null;
        return Directory.EnumerateFiles(installed, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    public static string Require(string fileName) =>
        Find(fileName) ?? throw new InvalidOperationException($"Modelo {fileName} não encontrado (defina MED_MODELS_DIR).");
}

/// <summary>Teste que depende de um modelo de IA: é ignorado (Skip) quando o arquivo não está disponível.</summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute(string fileName)
    {
        if (TestModels.Find(fileName) is null) Skip = $"Modelo {fileName} não disponível (defina MED_MODELS_DIR).";
    }
}
