using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.AIEngine.Speech;
using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.AIEngine;

public static class AiEngineServiceCollectionExtensions
{
    /// <summary>
    /// Motor de IA local (§8): gerenciador de modelos, sessões ONNX (CPU/GPU) e os serviços de cada recurso.
    /// Tudo roda no computador do usuário; nenhum serviço acessa a rede (RF169).
    /// </summary>
    public static IServiceCollection AddAiEngine(this IServiceCollection services)
    {
        services.AddSingleton<IModelRegistry>(sp =>
            new ModelRegistry(sp.GetRequiredService<IAppPaths>().AiModels, sp.GetService<ILogger<ModelRegistry>>()));
        services.AddSingleton(sp => new OnnxSessionFactory(sp.GetService<ISettingsService>(), sp.GetService<ILogger<OnnxSessionFactory>>()));
        services.AddSingleton(sp => new MatteCache(Path.Combine(sp.GetRequiredService<IAppPaths>().Cache, "mattes")));
        services.AddSingleton<ISegmentationService, SegmentationService>();
        services.AddSingleton<IAiFrameProcessor, AiFrameProcessor>();
        services.AddSingleton<ISceneDetectionService, SceneDetectionService>();
        services.AddSingleton<IImageEnhancementService, ImageEnhancementService>();
        services.AddSingleton<IVoiceActivityService, VoiceActivityService>();
        services.AddSingleton<IVoiceEnhancementService, VoiceEnhancementService>();
        services.AddSingleton<ISpeechRecognitionService, WhisperTranscriber>();
        return services;
    }
}
