// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using FFmpeg.AutoGen;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Sistema operacional alvo das escolhas específicas de plataforma do FFmpeg (parâmetro para permitir testes em qualquer SO).</summary>
public enum MediaOs
{
    Windows,
    Linux,
    MacOs,
}

/// <summary>
/// Escolhas do FFmpeg que dependem do sistema operacional: biblioteca-marcador, executável de linha de comando,
/// subpastas de busca e dispositivos de decodificação por hardware. Funções puras (o SO é um parâmetro).
/// </summary>
public static class FFmpegPlatform
{
    /// <summary>Sistema operacional em execução.</summary>
    public static MediaOs Current { get; } = OperatingSystem.IsWindows() ? MediaOs.Windows
        : OperatingSystem.IsMacOS() ? MediaOs.MacOs
        : MediaOs.Linux;

    /// <summary>Biblioteca usada para validar a pasta (versão principal do avcodec que o binding espera).</summary>
    public static string MarkerLibrary(MediaOs os) => os switch
    {
        MediaOs.Windows => "avcodec-63.dll",
        MediaOs.MacOs => "libavcodec.63.dylib",
        _ => "libavcodec.so.63",
    };

    /// <summary>Nome do executável <c>ffmpeg</c> de linha de comando.</summary>
    public static string CliName(MediaOs os) => os == MediaOs.Windows ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>
    /// Subpastas de uma raiz de FFmpeg (ex.: <c>deps/ffmpeg</c>) onde procurar as bibliotecas, em ordem.
    /// No Windows as DLLs ficam em <c>bin</c>; nas builds compartilhadas de Linux/macOS, em <c>lib</c>.
    /// </summary>
    public static IReadOnlyList<string> LibrarySubfolders(MediaOs os) => os == MediaOs.Windows ? ["bin"] : ["bin", "lib"];

    /// <summary>Dispositivos de decodificação por hardware a tentar, em ordem de preferência.</summary>
    public static IReadOnlyList<AVHWDeviceType> HardwareDeviceCandidates(MediaOs os) => os switch
    {
        MediaOs.Windows => [AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA],
        MediaOs.MacOs => [AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX],
        _ => [AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA],
    };

    /// <summary>Formato de pixel dos quadros que ficam na GPU para o tipo de dispositivo.</summary>
    public static AVPixelFormat HardwarePixelFormat(AVHWDeviceType type) => type switch
    {
        AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_D3D11,
        AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
        AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
        AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
        _ => AVPixelFormat.AV_PIX_FMT_NONE,
    };

    /// <summary>
    /// Procura o executável <c>ffmpeg</c> na pasta das bibliotecas, nela mesma e na pasta-mãe (builds compartilhadas
    /// de Linux/macOS têm as bibliotecas em <c>lib</c> e o executável em <c>bin</c>).
    /// </summary>
    public static string? FindCli(string? libraryDirectory, MediaOs os)
    {
        if (libraryDirectory is null) return null;
        var name = CliName(os);
        var roots = new List<string> { libraryDirectory };
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(libraryDirectory)) is { Length: > 0 } parent) roots.Add(parent);
        foreach (var root in roots)
            foreach (var sub in new[] { "", "bin" })
            {
                var path = Path.Combine(root, sub, name);
                if (File.Exists(path)) return path;
            }
        return null;
    }
}
