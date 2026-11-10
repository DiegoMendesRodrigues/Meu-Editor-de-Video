using System.Diagnostics;
using System.IO;

namespace MeuEditorDeVideo.IntegrationTests.Support;

/// <summary>
/// Mídias sintéticas geradas uma vez com o <c>ffmpeg.exe</c> de <c>deps\ffmpeg\bin</c> (mesma técnica de MediaTests/SampleMedia).
/// A pasta tem acentos para validar caminhos UTF-8 no FFmpeg.
/// </summary>
public sealed class IntegrationMedia : IDisposable
{
    public IntegrationMedia()
    {
        Root = TempFiles.NewRoot("mídias de integração");
        Run($"-f lavfi -i testsrc2=size=640x360:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 3 -ac 2 -c:v libx264 -pix_fmt yuv420p -c:a aac \"{Video}\"");
        Run($"-f lavfi -i testsrc2=size=640x360:rate=30 -t 1 -c:v libx264 -pix_fmt yuv420p \"{VideoSemAudio}\"");
        Run($"-f lavfi -i sine=frequency=220:sample_rate=44100 -t 2 \"{Audio}\"");
        Run($"-f lavfi -i testsrc2=size=320x240:rate=1 -frames:v 1 \"{Image}\"");

        // Arquivo danificado: só o começo de um MP4 (sem o índice "moov").
        var bytes = File.ReadAllBytes(Video);
        File.WriteAllBytes(Truncated, bytes[..Math.Min(4096, bytes.Length / 4)]);
    }

    public string Root { get; }

    /// <summary>3 s, 640x360, 25 fps, H.264 + AAC.</summary>
    public string Video => Path.Combine(Root, "vídeo com áudio.mp4");

    /// <summary>1 s, 640x360, 30 fps, sem áudio.</summary>
    public string VideoSemAudio => Path.Combine(Root, "sem_audio.mp4");

    /// <summary>2 s de áudio (WAV).</summary>
    public string Audio => Path.Combine(Root, "som.wav");

    /// <summary>Imagem PNG 320x240.</summary>
    public string Image => Path.Combine(Root, "imagem.png");

    /// <summary>Vídeo truncado (sem o índice do MP4).</summary>
    public string Truncated => Path.Combine(Root, "cortado.mp4");

    public static string FFmpegExe
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "deps", "ffmpeg", "bin", "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            throw new InvalidOperationException("ffmpeg.exe não encontrado. Execute scripts\\fetch-deps.ps1.");
        }
    }

    private static void Run(string args)
    {
        var psi = new ProcessStartInfo(FFmpegExe, "-hide_banner -loglevel error -y " + args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg falhou ({args}): {err}");
    }

    public void Dispose() => TempFiles.Delete(Root);
}

[CollectionDefinition(Name)]
public sealed class IntegrationMediaCollection : ICollectionFixture<IntegrationMedia>
{
    public const string Name = "Mídias de integração";
}
