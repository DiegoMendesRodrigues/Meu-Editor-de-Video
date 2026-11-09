using MeuEditorDeVideo.AudioEngine;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.RenderingEngine;

public static class RenderingEngineServiceCollectionExtensions
{
    /// <summary>Pré-visualização: compositor, saída de áudio e o motor (uma instância por tela de edição).</summary>
    public static IServiceCollection AddPreviewEngine(this IServiceCollection services)
    {
        services.AddAudioEngine();
        services.AddSingleton<ICompositor, CpuCompositor>();
        services.AddSingleton<IAudioOutputFactory, AudioOutputFactory>();
        services.AddTransient<PreviewEngine>();
        services.AddSingleton<MeuEditorDeVideo.Core.Export.IRenderEngine, RenderEngine>();
        return services;
    }
}
