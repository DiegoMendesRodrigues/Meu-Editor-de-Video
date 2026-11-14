using System.Text.Json;
using System.Text.Json.Serialization;
using MeuEditorDeVideo.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Infrastructure.Settings;

/// <summary>
/// Persiste <see cref="AppSettings"/> em JSON com gravação atômica (arquivo temporário + substituição). Nome, idioma, tema e
/// verificação de novas versões ficam no registro (<see cref="IUserPreferencesStore"/>), que prevalece sobre o JSON.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IUserPreferencesStore? _preferences;

    public JsonSettingsService(IAppPaths paths, ILogger<JsonSettingsService> logger)
        : this(paths, logger, null)
    {
    }

    /// <param name="preferences">Registro do Windows; nulo nos testes (só o JSON).</param>
    /// <param name="systemCulture">Cultura do Windows, para o idioma inicial (padrão: a da interface do sistema).</param>
    public JsonSettingsService(IAppPaths paths, ILogger<JsonSettingsService> logger, IUserPreferencesStore? preferences, string? systemCulture = null)
    {
        _paths = paths;
        _logger = logger;
        _preferences = preferences;
        Current = ApplyPreferences(Load(), systemCulture ?? System.Globalization.CultureInfo.InstalledUICulture.Name);
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);
            var tmp = _paths.SettingsFile + ".tmp";
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(fs, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_paths.SettingsFile))
                File.Replace(tmp, _paths.SettingsFile, _paths.SettingsFile + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(tmp, _paths.SettingsFile);

            SavePreferences(Current, settings);
            Current = settings;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, settings);
    }

    /// <summary>
    /// Lê o registro e completa o que faltar: idioma do Windows (se suportado; senão inglês), tema atual e verificação de
    /// versões "Sim" com a data e hora atuais em LastCheck.
    /// </summary>
    private AppSettings ApplyPreferences(AppSettings settings, string systemCulture)
    {
        if (_preferences is null) return settings;
        try
        {
            var p = _preferences.Read();
            var language = SupportedLanguages.IsSupported(p.Language) ? p.Language! : SupportedLanguages.FromCulture(systemCulture);
            if (p.Language != language) _preferences.WriteLanguage(language);

            var validTheme = Enum.TryParse<AppTheme>(p.ThemeColor, ignoreCase: true, out var t);
            var theme = validTheme ? t : settings.Theme;
            if (!validTheme) _preferences.WriteThemeColor(theme.ToString());

            var last = p.LastCheck;
            if (p.CheckForNewVersions is null)
            {
                _preferences.WriteCheckForNewVersions(true);
                last = DateTimeOffset.Now;
                _preferences.WriteLastCheck(last.Value);
            }

            return settings with
            {
                UserName = p.Name ?? settings.UserName,
                Language = language,
                Theme = theme,
                CheckForNewVersions = p.CheckForNewVersions ?? true,
                LastUpdateCheck = last,
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogWarning(ex, "Não foi possível ler as preferências do registro.");
            return settings;
        }
    }

    /// <summary>Grava no registro só as preferências que mudaram.</summary>
    private void SavePreferences(AppSettings before, AppSettings after)
    {
        if (_preferences is null) return;
        try
        {
            if (before.UserName != after.UserName) _preferences.WriteName(after.UserName);
            if (before.Language != after.Language) _preferences.WriteLanguage(after.Language);
            if (before.Theme != after.Theme) _preferences.WriteThemeColor(after.Theme.ToString());
            if (before.CheckForNewVersions != after.CheckForNewVersions) _preferences.WriteCheckForNewVersions(after.CheckForNewVersions);
            if (before.LastUpdateCheck != after.LastUpdateCheck && after.LastUpdateCheck is { } at) _preferences.WriteLastCheck(at);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogWarning(ex, "Não foi possível gravar as preferências no registro.");
        }
    }

    private AppSettings Load()
    {
        foreach (var candidate in new[] { _paths.SettingsFile, _paths.SettingsFile + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                using var fs = File.OpenRead(candidate);
                var loaded = JsonSerializer.Deserialize<AppSettings>(fs, JsonOptions);
                if (loaded is not null) return loaded;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Arquivo de configurações inválido ou inacessível: {File}", candidate);
            }
        }

        return new AppSettings();
    }
}
