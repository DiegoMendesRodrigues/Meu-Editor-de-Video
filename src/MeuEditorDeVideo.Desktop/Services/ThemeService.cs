using Avalonia.Styling;
using MeuEditorDeVideo.Core.Abstractions;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Alterna o tema Fluent e a paleta própria (RF207). As paletas são dicionários por variante (Themes/Palette.axaml);
/// "Sistema" usa a variante padrão, que acompanha o tema do sistema operacional e muda junto com ele.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        if (Avalonia.Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
