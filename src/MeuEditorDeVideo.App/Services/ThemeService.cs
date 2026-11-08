using System.Windows;
using MeuEditorDeVideo.Core.Abstractions;
using Microsoft.Win32;

namespace MeuEditorDeVideo.App.Services;

public interface IThemeService
{
    AppTheme Current { get; }

    void Apply(AppTheme theme);
}

/// <summary>
/// Alterna o tema Fluent nativo do WPF e a paleta própria do aplicativo (RF207).
/// A paleta fica no índice 0 dos dicionários mesclados de App.xaml.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsSystemDark(),
        };

        var app = System.Windows.Application.Current;
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        // O WPF insere o dicionário Fluent nos MergedDictionaries; localizar a paleta pelo nome, nunca pelo índice.
        var dictionaries = app.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri(dark ? "Themes/Palette.Dark.xaml" : "Themes/Palette.Light.xaml", UriKind.Relative),
        };
        var current = dictionaries.FirstOrDefault(IsPalette);
        if (current is null)
            dictionaries.Add(palette);
        else
            dictionaries[dictionaries.IndexOf(current)] = palette;
    }

    private static bool IsPalette(ResourceDictionary d) =>
        d.Source?.OriginalString.Contains("Themes/Palette.", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }
}
