using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.Core.Abstractions;

/// <summary>Qualidade padrão da pré-visualização (RF210). Automática escolhe pelo hardware e pela resolução do projeto (§12.3).</summary>
public enum PreviewQualityMode
{
    Auto,
    Full,
    Half,
    Quarter,
}

public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>Configurações persistidas do aplicativo (§3.15). Novos campos devem ter valor padrão.</summary>
public sealed record AppSettings
{
    /// <summary>Idioma da interface (<see cref="SupportedLanguages"/>); guardado no registro (Language).</summary>
    public string Language { get; init; } = "pt-BR";

    /// <summary>Nome completo do usuário; guardado no registro (Name).</summary>
    public string UserName { get; init; } = "";

    /// <summary>Verificar mensalmente se há novas versões; guardado no registro (CheckForNewVersions).</summary>
    public bool CheckForNewVersions { get; init; } = true;

    /// <summary>Data e hora da última verificação de novas versões; guardada no registro (LastCheck).</summary>
    public DateTimeOffset? LastUpdateCheck { get; init; }
    public AppTheme Theme { get; init; } = AppTheme.Dark;
    public bool OpenLastProjectOnStartup { get; init; }
    public bool UseGpuAcceleration { get; init; } = true;
    /// <summary>Limite do cache em disco (miniaturas, formas de onda, proxies e máscaras de IA), em GB (RF209).</summary>
    public int PreviewCacheGigabytes { get; init; } = 4;

    /// <summary>Qualidade padrão da pré-visualização (RF210).</summary>
    public PreviewQualityMode PreviewQuality { get; init; } = PreviewQualityMode.Auto;

    /// <summary>Memória para quadros prontos da pré-visualização, em MB; 0 = automática pelo hardware (RNF013).</summary>
    public int PreviewMemoryMegabytes { get; init; }

    /// <summary>Atalhos alterados pelo usuário: ação → combinação (ex.: "Ctrl+Shift+S"); vazio = sem atalho (RF212).</summary>
    public IReadOnlyDictionary<string, string> Shortcuts { get; init; } = new Dictionary<string, string>();

    /// <summary>A pré-visualização usa proxies 540p quando existirem.</summary>
    public bool UseProxies { get; init; } = true;

    /// <summary>Gera proxies automaticamente para vídeos acima de 1080p ao importar/abrir.</summary>
    public bool AutoGenerateProxies { get; init; } = true;
    public bool BackgroundRendering { get; init; } = true;

    /// <summary>Dispositivo para os modelos de IA (RF167). GPU (DirectML) volta para CPU automaticamente se falhar.</summary>
    public AiDevice AiDevice { get; init; } = AiDevice.Gpu;
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

/// <summary>Idiomas da interface: código (gravado no registro) e nome no próprio idioma.</summary>
public static class SupportedLanguages
{
    public static readonly IReadOnlyList<(string Code, string NativeName)> All =
    [
        ("de", "Deutsch"),
        ("es", "Español"),
        ("fr", "Français"),
        ("en", "English"),
        ("pt-BR", "Português (Brasil)"),
        ("pt-PT", "Português (Portugal)"),
    ];

    public static bool IsSupported(string? code) => code is not null && All.Any(l => l.Code == code);

    /// <summary>Idioma suportado mais próximo de uma cultura do Windows (ex.: "de-AT" → "de", "pt-AO" → "pt-PT"); sem correspondência, inglês.</summary>
    public static string FromCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return "en";
        if (cultureName.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        var neutral = cultureName.Split('-')[0].ToLowerInvariant();
        return neutral switch
        {
            "pt" => "pt-PT",
            "de" or "es" or "fr" or "en" => neutral,
            _ => "en",
        };
    }
}
