using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests;

public sealed class ProjectNamesTests
{
    [Theory]
    [InlineData("a*b")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("a\tb")]
    public void Nome_invalido_no_Windows_e_recusado_em_qualquer_sistema(string name) => Assert.True(ProjectNames.HasInvalidChars(name));

    [Fact]
    public void Nome_com_acentos_e_espacos_e_aceito() => Assert.False(ProjectNames.HasInvalidChars("Viagem à Amazônia - 2026"));
}
