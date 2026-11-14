using System.IO;
using MeuEditorDeVideo.App.Localization;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Updates;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.Services;

/// <summary>
/// Verificar atualizações: pelo menu Ajuda ou, ao abrir o programa, quando passaram mais de 30 dias do LastCheck do
/// registro (com "Verificar mensalmente" ligado). Consulta o serviço de versões no idioma da interface, compara com a
/// versão instalada e mostra o resultado; ao fechar a janela, grava a data e a hora atuais em LastCheck.
/// </summary>
public sealed class UpdateCheckService(IUpdateChecker checker, ISettingsService settings, IDialogService dialogs,
    ILogger<UpdateCheckService> logger)
{
    private bool _running;

    /// <summary>Verificação automática ao abrir: só quando está ligada e o prazo venceu. Falhas ficam só no log.</summary>
    public Task CheckOnStartupAsync()
    {
        var s = settings.Current;
        if (!s.CheckForNewVersions || !UpdateEndpoints.IsDue(s.LastUpdateCheck, DateTimeOffset.Now)) return Task.CompletedTask;
        return CheckAsync(manual: false);
    }

    /// <summary>Consulta e mostra o resultado. Na verificação pelo menu, um erro é mostrado ao usuário.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (_running) return;
        _running = true;
        try
        {
            UpdateInfo info;
            try
            {
                info = await checker.CheckAsync(Translator.Language);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Não foi possível verificar atualizações.");
                if (manual) dialogs.ShowInfo(Translator.T("Verificar atualizações"), Translator.T(ex.Message));
                return;
            }

            var owner = System.Windows.Application.Current?.MainWindow;
            if (UpdateEndpoints.IsNewer(info.Version, AppInfo.Version))
                Views.Dialogs.UpdateWindow.ShowNewVersion(owner, info);
            else
                Views.Dialogs.UpdateWindow.ShowUpToDate(owner);

            // "Fechar essa janela": guarda a data e a hora da verificação (registro LastCheck).
            await settings.SaveAsync(settings.Current with { LastUpdateCheck = DateTimeOffset.Now });
        }
        finally
        {
            _running = false;
        }
    }
}
