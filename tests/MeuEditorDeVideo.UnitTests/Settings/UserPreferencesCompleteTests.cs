using MeuEditorDeVideo.Core.Abstractions;
using Xunit;

namespace MeuEditorDeVideo.UnitTests.Settings;

/// <summary>Janela de boas-vindas: aparece quando alguma preferência do registro falta ou está vazia.</summary>
public sealed class UserPreferencesCompleteTests
{
    private static readonly UserPreferences Full = new("Ana Silva", "pt-BR", "Dark", true, DateTimeOffset.Now);

    [Fact]
    public void Completa_quando_todos_os_valores_existem() => Assert.True(Full.IsComplete);

    [Fact]
    public void Registro_vazio_nao_esta_completo() => Assert.False(new UserPreferences(null, null, null, null, null).IsComplete);

    [Theory]
    [InlineData("", "pt-BR", "Dark")]
    [InlineData("   ", "pt-BR", "Dark")]
    [InlineData("Ana", "", "Dark")]
    [InlineData("Ana", "xx", "Dark")]
    [InlineData("Ana", "pt-BR", "")]
    [InlineData("Ana", "pt-BR", "Azul")]
    public void Campo_vazio_ou_invalido_nao_esta_completo(string name, string language, string theme) =>
        Assert.False((Full with { Name = name, Language = language, ThemeColor = theme }).IsComplete);

    [Fact]
    public void Sem_verificacao_ou_data_nao_esta_completo()
    {
        Assert.False((Full with { CheckForNewVersions = null }).IsComplete);
        Assert.False((Full with { LastCheck = null }).IsComplete);
    }
}
