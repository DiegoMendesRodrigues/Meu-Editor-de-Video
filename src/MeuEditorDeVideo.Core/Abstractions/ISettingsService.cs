namespace MeuEditorDeVideo.Core.Abstractions;

public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>Configurações persistidas do aplicativo (§3.15). Novos campos devem ter valor padrão.</summary>
public sealed record AppSettings
{
    public string Language { get; init; } = "pt-BR";
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public bool OpenLastProjectOnStartup { get; init; }
    public bool UseGpuAcceleration { get; init; } = true;
    public int PreviewCacheGigabytes { get; init; } = 4;
    public bool BackgroundRendering { get; init; } = true;
    public bool AutosaveEnabled { get; init; } = true;
    public int AutosaveIntervalMinutes { get; init; } = 5;
    public string? DefaultProjectsDirectory { get; init; }
    public string? DefaultExportDirectory { get; init; }
    public IReadOnlyList<string> RecentProjects { get; init; } = [];
}

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
