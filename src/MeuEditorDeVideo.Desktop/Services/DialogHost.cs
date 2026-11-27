// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Janelas modais sobre a janela ativa do aplicativo. Convenção dos diálogos convertidos do WPF: OK fecha com
/// <c>Close(true)</c> (o antigo <c>DialogResult = true</c>); cancelar fecha com <c>Close(false)</c> ou <c>Close()</c>.
/// </summary>
public static class DialogHost
{
    /// <summary>Janela ativa (um diálogo sobre outro) ou a principal; null antes da janela principal existir.</summary>
    public static Window? Owner
    {
        get
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return null;
            return desktop.Windows.LastOrDefault(w => w.IsActive && w.IsVisible)
                ?? desktop.Windows.LastOrDefault(w => w.IsVisible && w != desktop.MainWindow && w.Owner is not null)
                ?? (desktop.MainWindow is { IsVisible: true } main ? main : null);
        }
    }

    /// <summary>Mostra <paramref name="dialog"/> como modal e espera (sem bloquear o desenho). Devolve o valor de Close(...).</summary>
    public static bool? ShowModal(Window dialog) => ModalLoop.Run(ShowModalAsync(dialog));

    public static Task<bool?> ShowModalAsync(Window dialog) => ShowModalAsync<bool?>(dialog);

    public static async Task<T?> ShowModalAsync<T>(Window dialog)
    {
        if (Owner is { } owner)
            return await dialog.ShowDialog<T?>(owner);
        // Sem janela principal (inicialização): janela comum, esperando fechar.
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Show();
        await closed.Task;
        return default;
    }
}
