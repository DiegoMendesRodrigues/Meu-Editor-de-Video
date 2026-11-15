using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Errors;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>
/// Localiza e carrega as bibliotecas nativas do FFmpeg (uma vez por processo).
/// Ordem de busca: variável MED_FFMPEG_DIR → pasta "ffmpeg" ao lado do executável → deps\ffmpeg\bin do repositório (desenvolvimento).
/// </summary>
public static unsafe class FFmpegRuntime
{
    /// <summary>DLL usada para validar a pasta (versão principal do avcodec que o binding espera).</summary>
    private const string MarkerDll = "avcodec-63.dll";

    private static readonly Lock Gate = new();
    private static bool _initialized;

    public static string? Directory { get; private set; }

    public static string? Version { get; private set; }

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;

            var dir = Locate() ?? throw new MedException(MedErrors.MediaEngineUnavailable(
                $"As bibliotecas do FFmpeg ({MarkerDll}) não foram encontradas."));

            try
            {
                ffmpeg.RootPath = dir;
                Version = ffmpeg.av_version_info();
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or BadImageFormatException)
            {
                throw new MedException(MedErrors.MediaEngineUnavailable(ex.Message) with { Cause = ex });
            }

            Directory = dir;
            _initialized = true;
        }
    }

    private static string? Locate()
    {
        static bool Valid(string? d) => d is not null && File.Exists(Path.Combine(d, MarkerDll));

        var env = Environment.GetEnvironmentVariable("MED_FFMPEG_DIR");
        if (Valid(env)) return env;

        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (Valid(local)) return local;

        // Desenvolvimento: só a pasta deps do repositório (a que tem a solução). Sem essa exigência, um programa instalado
        // aceitaria DLLs plantadas em pastas acima dele que qualquer usuário pode criar (ex.: C:\deps\ffmpeg\bin).
        var repository = RepositoryRoot();
        var candidate = repository is null ? null : Path.Combine(repository, "deps", "ffmpeg", "bin");
        return Valid(candidate) ? candidate : null;
    }

    /// <summary>Raiz do repositório (pasta com MeuEditorDeVideo.sln) acima do executável, ou nulo no programa instalado.</summary>
    private static string? RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MeuEditorDeVideo.sln"))) return dir.FullName;
        return null;
    }
}
