using System.IO;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Infrastructure;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public JsonSettingsServiceTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
    }

    private JsonSettingsService Create() => new(_paths, NullLogger<JsonSettingsService>.Instance);

    [Fact]
    public void Sem_arquivo_usa_padroes_em_portugues_e_tema_escuro()
    {
        var s = Create().Current;
        Assert.Equal("pt-BR", s.Language);
        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.Equal(5, s.AutosaveIntervalMinutes);
    }

    [Fact]
    public async Task Salva_e_recarrega_configuracoes()
    {
        var svc = Create();
        await svc.SaveAsync(svc.Current with { Theme = AppTheme.Light, RecentProjects = ["C:\\a.med"] });
        await svc.SaveAsync(svc.Current with { AutosaveIntervalMinutes = 10 });

        var reloaded = Create().Current;
        Assert.Equal(AppTheme.Light, reloaded.Theme);
        Assert.Equal(10, reloaded.AutosaveIntervalMinutes);
        Assert.Equal(["C:\\a.med"], reloaded.RecentProjects);
        Assert.False(File.Exists(_paths.SettingsFile + ".tmp"));
    }

    [Fact]
    public async Task Arquivo_corrompido_recupera_a_partir_do_backup()
    {
        var svc = Create();
        await svc.SaveAsync(svc.Current with { PreviewCacheGigabytes = 8 });
        await svc.SaveAsync(svc.Current with { PreviewCacheGigabytes = 8, AutosaveIntervalMinutes = 7 }); // gera .bak
        await File.WriteAllTextAsync(_paths.SettingsFile, "{ corrompido");

        var reloaded = Create().Current;
        Assert.Equal(8, reloaded.PreviewCacheGigabytes);
    }

    private sealed class FakePreferences(UserPreferences initial) : IUserPreferencesStore
    {
        public UserPreferences Stored = initial;
        public List<string> Writes = [];

        public UserPreferences Read() => Stored;
        public void WriteName(string name) { Stored = Stored with { Name = name }; Writes.Add("Name"); }
        public void WriteLanguage(string language) { Stored = Stored with { Language = language }; Writes.Add("Language"); }
        public void WriteThemeColor(string theme) { Stored = Stored with { ThemeColor = theme }; Writes.Add("ThemeColor"); }
        public void WriteCheckForNewVersions(bool check) { Stored = Stored with { CheckForNewVersions = check }; Writes.Add("CheckForNewVersions"); }
        public void WriteLastCheck(DateTimeOffset at) { Stored = Stored with { LastCheck = at }; Writes.Add("LastCheck"); }
    }

    private JsonSettingsService Create(FakePreferences prefs, string culture) =>
        new(_paths, NullLogger<JsonSettingsService>.Instance, prefs, culture);

    [Fact]
    public void Primeira_execucao_grava_idioma_do_Windows_tema_e_verificacao_mensal()
    {
        var prefs = new FakePreferences(new UserPreferences(null, null, null, null, null));
        var before = DateTimeOffset.Now;

        var s = Create(prefs, "de-AT").Current;

        Assert.Equal("de", s.Language);
        Assert.True(s.CheckForNewVersions);
        Assert.NotNull(s.LastUpdateCheck);
        Assert.True(s.LastUpdateCheck >= before);
        Assert.Equal("de", prefs.Stored.Language);
        Assert.Equal("Dark", prefs.Stored.ThemeColor);
        Assert.True(prefs.Stored.CheckForNewVersions);
        Assert.Equal(s.LastUpdateCheck, prefs.Stored.LastCheck);
    }

    [Fact]
    public void Idioma_do_Windows_nao_suportado_vira_ingles()
    {
        var prefs = new FakePreferences(new UserPreferences(null, null, null, null, null));
        Assert.Equal("en", Create(prefs, "ja-JP").Current.Language);
    }

    [Fact]
    public void Preferencias_do_registro_prevalecem_e_nao_sao_regravadas()
    {
        var last = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var prefs = new FakePreferences(new UserPreferences("Ana Souza", "fr", "Light", false, last));

        var s = Create(prefs, "de-DE").Current;

        Assert.Equal("Ana Souza", s.UserName);
        Assert.Equal("fr", s.Language);
        Assert.Equal(AppTheme.Light, s.Theme);
        Assert.False(s.CheckForNewVersions);
        Assert.Equal(last, s.LastUpdateCheck);
        Assert.Empty(prefs.Writes);
    }

    [Fact]
    public async Task Salvar_grava_no_registro_so_o_que_mudou()
    {
        var prefs = new FakePreferences(new UserPreferences("Ana", "en", "Dark", true, DateTimeOffset.Now));
        var svc = Create(prefs, "en-US");

        await svc.SaveAsync(svc.Current with { UserName = "Ana Souza", Language = "es", AutosaveIntervalMinutes = 9 });

        Assert.Equal(["Name", "Language"], prefs.Writes);
        Assert.Equal("Ana Souza", prefs.Stored.Name);
        Assert.Equal("es", prefs.Stored.Language);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
