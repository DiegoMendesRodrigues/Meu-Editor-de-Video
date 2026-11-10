using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.ModelDownloads;

public static class ModelDownloadsServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="IModelDownloader"/>. A assinatura não expõe tipos de rede, para que o aplicativo não passe a
    /// depender de System.Net.Http (verificado pelo PrivacyTests).
    /// </summary>
    public static IServiceCollection AddModelDownloads(this IServiceCollection services, IReadOnlyList<string> allowedHosts) =>
        services.AddSingleton<IModelDownloader>(sp => new HttpModelDownloader(allowedHosts, sp.GetService<ILogger<HttpModelDownloader>>()));
}
