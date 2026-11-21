using System.Reflection;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Componente de terceiros exibido em "Sobre".</summary>
public sealed record AboutComponent(string Name, string License, string Use);

/// <summary>Informações do aplicativo (RF215): versão, desenvolvedor, licença, componentes e modelos de IA.</summary>
public sealed class AboutViewModel(IModelRegistry? models = null)
{
    public const string Developer = "Diego Mendes Rodrigues";

    /// <summary>Perfil do desenvolvedor no LinkedIn (aberto no navegador padrão; o programa não acessa a rede).</summary>
    public const string DeveloperLinkedIn = "https://www.linkedin.com/in/diegomendesrodrigues/";

    public System.Windows.Input.ICommand OpenLinkedInCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DeveloperLinkedIn) { UseShellExecute = true }));

    /// <summary>Site do programa (abre no navegador padrão, no idioma da interface).</summary>
    public const string Site = "https://meueditordevideos.aplicativoia.online/";

    /// <summary>Repositório do código-fonte no GitHub.</summary>
    public const string GitHub = "https://github.com/DiegoMendesRodrigues/Meu-Editor-de-Video";

    /// <summary>Valor do parâmetro <c>lang</c> do site para o idioma da interface.</summary>
    public static string SiteLanguage(string uiLanguage) => uiLanguage switch
    {
        "de" => "de",
        "es" => "es",
        "fr" => "fr-FR",
        "en" => "en",
        "pt-PT" => "pt",
        _ => "pt-BR",
    };

    public static string WebsiteUrlFor(string uiLanguage) => $"{Site}?lang={SiteLanguage(uiLanguage)}";

    /// <summary>Página de suporte técnico (Ajuda › Suporte técnico).</summary>
    public static string SupportUrlFor(string uiLanguage) => $"{Site}atendimento.php?lang={SiteLanguage(uiLanguage)}";

    public string WebsiteUrl => WebsiteUrlFor(Localization.Translator.Language);

    public string GitHubUrl => GitHub;

    public System.Windows.Input.ICommand OpenWebsiteCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        OpenInBrowser(WebsiteUrlFor(Localization.Translator.Language)));

    public System.Windows.Input.ICommand OpenGitHubCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() => OpenInBrowser(GitHub));

    /// <summary>Abre o endereço no navegador padrão (o programa não acessa a rede).</summary>
    public static void OpenInBrowser(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

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
        new(OperatingSystem.IsWindows() ? "ONNX Runtime com DirectML" : "ONNX Runtime", "MIT", "Execução local dos modelos de IA (CPU/GPU)"),
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
