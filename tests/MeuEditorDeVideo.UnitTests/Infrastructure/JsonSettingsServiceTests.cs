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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
