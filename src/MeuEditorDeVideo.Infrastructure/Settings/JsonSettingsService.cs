using System.Text.Json;
using System.Text.Json.Serialization;
using MeuEditorDeVideo.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Infrastructure.Settings;

/// <summary>Persiste <see cref="AppSettings"/> em JSON com gravação atômica (arquivo temporário + substituição).</summary>
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

    public JsonSettingsService(IAppPaths paths, ILogger<JsonSettingsService> logger)
    {
        _paths = paths;
        _logger = logger;
        Current = Load();
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

            Current = settings;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, settings);
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
