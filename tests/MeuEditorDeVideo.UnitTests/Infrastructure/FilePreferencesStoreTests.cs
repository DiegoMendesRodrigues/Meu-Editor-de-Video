// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.IO;
using MeuEditorDeVideo.Infrastructure.Settings;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

public sealed class FilePreferencesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-prefs-ção-" + Guid.NewGuid().ToString("N"));

    private string Arquivo => Path.Combine(_dir, "sub", "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Arquivo_ausente_equivale_a_preferencias_vazias()
    {
        var prefs = new FilePreferencesStore(Arquivo).Read();

        Assert.Null(prefs.Name);
        Assert.Null(prefs.CheckForNewVersions);
        Assert.False(prefs.IsComplete);
    }

    [Fact]
    public void Valores_gravados_voltam_na_leitura_e_completam_as_preferencias()
    {
        var store = new FilePreferencesStore(Arquivo);
        var at = new DateTimeOffset(2026, 5, 3, 10, 20, 30, TimeSpan.FromHours(-3));
        store.WriteName("João Çalves");
        store.WriteLanguage("pt-BR");
        store.WriteThemeColor("Dark");
        store.WriteCheckForNewVersions(true);
        store.WriteLastCheck(at);

        var prefs = new FilePreferencesStore(Arquivo).Read();

        Assert.Equal("João Çalves", prefs.Name);
        Assert.Equal("pt-BR", prefs.Language);
        Assert.Equal("Dark", prefs.ThemeColor);
        Assert.True(prefs.CheckForNewVersions);
        Assert.Equal(at, prefs.LastCheck);
        Assert.True(prefs.IsComplete);
        Assert.False(File.Exists(Arquivo + ".tmp"));
    }

    [Fact]
    public void Gravar_um_valor_preserva_os_demais()
    {
        var store = new FilePreferencesStore(Arquivo);
        store.WriteName("Ana");
        store.WriteCheckForNewVersions(false);
        store.WriteName("Bia");

        var prefs = store.Read();
        Assert.Equal("Bia", prefs.Name);
        Assert.False(prefs.CheckForNewVersions);
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public void Arquivo_corrompido_e_tratado_como_vazio_e_pode_ser_sobrescrito(string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
        File.WriteAllText(Arquivo, conteudo);
        var store = new FilePreferencesStore(Arquivo);

        var prefs = store.Read();
        Assert.Null(prefs.Name);
        Assert.False(prefs.IsComplete);

        store.WriteName("Carlos");
        Assert.Equal("Carlos", store.Read().Name);
    }

    [Fact]
    public void Tipos_inesperados_sao_ignorados()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
        File.WriteAllText(Arquivo, """{"Name":5,"CheckForNewVersions":"talvez","LastCheck":"ontem"}""");

        var prefs = new FilePreferencesStore(Arquivo).Read();

        Assert.Null(prefs.Name);
        Assert.Null(prefs.CheckForNewVersions);
        Assert.Null(prefs.LastCheck);
    }
}
