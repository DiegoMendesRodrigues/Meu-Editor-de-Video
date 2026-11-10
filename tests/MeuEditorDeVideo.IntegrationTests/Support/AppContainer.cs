using System.IO;
using MeuEditorDeVideo.Application;
using MeuEditorDeVideo.AIEngine;
using MeuEditorDeVideo.Infrastructure.Hosting;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.MediaEngine;
using MeuEditorDeVideo.ProjectEngine;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeuEditorDeVideo.IntegrationTests.Support;

/// <summary>
/// Container de DI com os serviços reais, montado como o aplicativo faz em <c>App.xaml.cs</c>
/// (<c>UseMeuEditorInfrastructure</c> + <c>AddProjectEngine</c>, <c>AddMediaEngine</c>, <c>AddPreviewEngine</c>,
/// <c>AddMeuEditorApplication</c>, <c>AddAiEngine</c>), sem a camada de janela (WPF) nem o instalador de modelos pela rede.
/// Os caminhos apontam para uma pasta temporária. Criar outro container com a mesma raiz simula reiniciar o aplicativo.
/// </summary>
internal sealed class AppContainer : IDisposable
{
    private readonly IHost _host;
    private readonly bool _apagarRaiz;

    /// <param name="apagarRaiz">Apaga a pasta temporária ao descartar (use false ao simular reinício com a mesma raiz).</param>
    public AppContainer(string root, bool apagarRaiz = true)
    {
        Root = root;
        _apagarRaiz = apagarRaiz;
        Paths = new AppPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"));
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseMeuEditorInfrastructure(Paths)
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public IServiceProvider Services => _host.Services;

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Pasta de projetos de teste dentro da raiz temporária.</summary>
    public string ProjectsDir => Path.Combine(Root, "projetos");

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddProjectEngine();
        services.AddMediaEngine();
        services.AddPreviewEngine();
        services.AddMeuEditorApplication();
        services.AddAiEngine();
    }

    public void Dispose()
    {
        _host.Dispose();
        Serilog.Log.CloseAndFlush();
        if (_apagarRaiz) TempFiles.Delete(Root);
    }
}
