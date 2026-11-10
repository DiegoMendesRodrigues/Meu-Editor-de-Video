using System.Reflection;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Componente de terceiros exibido em "Sobre".</summary>
public sealed record AboutComponent(string Name, string License, string Use);

/// <summary>Informações do aplicativo (RF215): versão, desenvolvedor, licença, componentes e modelos de IA.</summary>
public sealed class AboutViewModel(IModelRegistry? models = null)
{
    public const string Developer = "Diego Mendes Rodrigues";

    public string Product => "Meu Editor de Vídeo";

    public string Version
    {
        get
        {
            var info = typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString() ?? "";
            var plus = info.IndexOf('+');
            return "Versão " + (plus > 0 ? info[..plus] : info);
        }
    }

    public string DeveloperText => "Desenvolvedor: " + Developer;

    public string License => "Software livre licenciado sob a GPL-3.0-or-later (GNU General Public License, versão 3 ou posterior). " +
        "Você pode usar, estudar, modificar e redistribuir o programa nos termos dessa licença; o programa é distribuído sem garantia.";

    public string Privacy => "Vídeos, áudios, imagens e projetos são processados somente neste computador. A internet só é usada " +
        "para baixar modelos de IA quando você pede, em Ferramentas › Modelos de IA › Baixar modelos.";

    public string Runtime => $".NET {Environment.Version} · Windows {Environment.OSVersion.Version}";

    public IReadOnlyList<AboutComponent> Components { get; } =
    [
        new("FFmpeg 9.0 (x264, x265, libvpx, SVT-AV1, libaom, LAME, Opus, Vorbis)", "GPL-3.0-or-later", "Leitura e exportação de vídeo e áudio"),
        new("FFmpeg.AutoGen", "LGPL-3.0", "Acesso ao FFmpeg pelo .NET"),
        new("OpenCV / OpenCvSharp4", "Apache-2.0", "Rastreamento, estabilização e máscaras"),
        new("ONNX Runtime com DirectML", "MIT", "Execução local dos modelos de IA (CPU/GPU)"),
        new("Whisper.net (whisper.cpp)", "MIT", "Reconhecimento de fala para legendas"),
        new("NAudio", "MIT", "Saída de áudio da pré-visualização"),
        new("Svg.Skia / SkiaSharp / HarfBuzzSharp", "MIT", "Gráficos vetoriais SVG"),
        new("CommunityToolkit.Mvvm", "MIT", "Estrutura da interface"),
        new("Microsoft.Extensions (Hosting, DI, Logging)", "MIT", "Infraestrutura do aplicativo"),
        new("Serilog", "Apache-2.0", "Registro técnico local"),
    ];

    /// <summary>Modelos de IA presentes (incluídos e instalados), com licença.</summary>
    public IReadOnlyList<AboutComponent> Models =>
        models?.Installed.Select(m => new AboutComponent($"{m.Manifest.Name} {m.Manifest.Version}", m.Manifest.License,
            m.BuiltIn ? "Incluído no aplicativo" : "Instalado pelo usuário")).ToList() ?? [];

    public string Copyright => typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
}
