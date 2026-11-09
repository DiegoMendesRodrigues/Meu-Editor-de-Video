using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Infrastructure.Export;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace MeuEditorDeVideo.Infrastructure.Hosting;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registra caminhos, configurações e logs técnicos (RNF011). Logs são rotativos, locais,
    /// e não devem registrar conteúdo audiovisual — apenas eventos técnicos.
    /// </summary>
    public static IHostBuilder UseMeuEditorInfrastructure(this IHostBuilder builder, AppPaths paths)
    {
        paths.EnsureCreated();

        builder.UseSerilog((_, config) => config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.Logs, "meu-editor-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 20 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IAppPaths>(paths);
            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IExportTemplateStore, JsonExportTemplateStore>();
        });

        return builder;
    }
}
