// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Reflection;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Presentation.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>As duas verificações (menu Ajuda e automática) comparam o campo "version" com 1.4.0 por igualdade.</summary>
public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData(true, "1.4.0", false)]
    [InlineData(true, "1.3.0", true)]
    [InlineData(true, "1.5.0", true)]
    [InlineData(false, "1.4.0", false)]
    [InlineData(false, "1.3.0", true)]
    [InlineData(false, "1.5.0", true)]
    public async Task Menu_e_automatica_mostram_a_caixa_conforme_a_igualdade(bool peloMenu, string publicada, bool caixaDeNovaVersao)
    {
        var settings = new Settings();
        var dialogs = DispatchProxy.Create<IDialogService, Dialogos>();
        var service = new UpdateCheckService(new Checker(publicada), settings, dialogs, NullLogger<UpdateCheckService>.Instance);

        await (peloMenu ? service.CheckAsync(manual: true) : service.CheckOnStartupAsync());

        Assert.Equal([caixaDeNovaVersao], ((Dialogos)(object)dialogs).Resultados);
        Assert.NotNull(settings.Current.LastUpdateCheck);
    }

    private sealed class Checker(string version) : IUpdateChecker
    {
        public Task<UpdateInfo> CheckAsync(string language, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateInfo(version, "", "", "", null, null));
    }

    /// <summary>Só guarda o que foi pedido a ShowUpdateResult.</summary>
    public class Dialogos : DispatchProxy
    {
        public List<bool> Resultados { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal(nameof(IDialogService.ShowUpdateResult), method?.Name);
            Resultados.Add((bool)args![1]!);
            return null;
        }
    }

    private sealed class Settings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new() { CheckForNewVersions = true, LastUpdateCheck = null };

        public event EventHandler<AppSettings>? Changed;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }
}
