using MeuEditorDeVideo.Core.Media;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.MediaEngine;

public static class MediaEngineServiceCollectionExtensions
{
    public static IServiceCollection AddMediaEngine(this IServiceCollection services)
    {
        services.AddSingleton<MediaCache>();
        services.AddSingleton<IMediaProbe, MediaProbe>();
        services.AddSingleton<IThumbnailProvider, ThumbnailProvider>();
        services.AddSingleton<IWaveformProvider, WaveformProvider>();
        return services;
    }
}
