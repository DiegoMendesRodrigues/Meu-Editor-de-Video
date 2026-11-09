using System.Diagnostics;
using System.IO;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>
/// Gera mídias sintéticas com o ffmpeg.exe de deps\ (uma vez por execução de testes).
/// A pasta contém acentos para validar caminhos UTF-8 no FFmpeg.
/// </summary>
public sealed class SampleMedia : IDisposable
{
    public SampleMedia()
    {
        Root = Path.Combine(Path.GetTempPath(), "med-media-tests", "Mídias de teste " + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Root);
        var seqDir = Path.Combine(Root, "sequência");
        Directory.CreateDirectory(seqDir);

        Run($"-f lavfi -i testsrc2=size=640x360:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 3 -ac 2 -c:v libx264 -pix_fmt yuv420p -c:a aac \"{Video}\"");
        Run($"-f lavfi -i testsrc2=size=640x360:rate=30 -t 1 -c:v libx264 -pix_fmt yuv420p \"{VideoNoAudio}\"");
        Run($"-display_rotation 90 -i \"{VideoNoAudio}\" -c copy \"{Rotated}\"");
        Run($"-f lavfi -i sine=frequency=220:sample_rate=44100 -t 2 \"{Audio}\"");
        Run($"-f lavfi -i testsrc2=size=320x240:rate=1 -frames:v 1 \"{Image}\"");
        // Fundo verde de estúdio com um "apresentador" (retângulo cor de pele) no centro, para o chroma key.
        Run($"-f lavfi -i color=c=0x00B140:size=320x180:rate=25 -vf drawbox=x=110:y=50:w=100:h=80:color=0xDCAA8C:t=fill -t 2 -c:v libx264 -pix_fmt yuv420p \"{GreenScreen}\"");
        Run($"-f lavfi -i testsrc2=size=160x120:rate=10 -frames:v 10 \"{AnimatedGif}\"");
        Run($"-f lavfi -i testsrc2=size=160x120:rate=10 -frames:v 1 \"{StillGif}\"");
        Run($"-f lavfi -i testsrc2=size=320x180:rate=10 -frames:v 12 \"{Path.Combine(seqDir, "quadro_%04d.png")}\"");
        SequenceFirstFrame = Path.Combine(seqDir, "quadro_0005.png");

        // Arquivo danificado: só o começo de um MP4 (sem o índice "moov").
        var bytes = File.ReadAllBytes(Video);
        File.WriteAllBytes(Truncated, bytes[..Math.Min(4096, bytes.Length / 4)]);
        File.WriteAllBytes(Garbage, Enumerable.Range(0, 20_000).Select(i => (byte)(i * 31 % 251)).ToArray());
        File.WriteAllText(TextFile, "não é mídia");
    }

    public string Root { get; }
    public string Video => Path.Combine(Root, "vídeo com áudio.mp4");
    public string VideoNoAudio => Path.Combine(Root, "sem_audio.mp4");
    public string Rotated => Path.Combine(Root, "girado.mp4");
    public string Audio => Path.Combine(Root, "som.wav");
    public string Image => Path.Combine(Root, "imagem.png");
    public string GreenScreen => Path.Combine(Root, "fundo verde.mp4");
    public string AnimatedGif => Path.Combine(Root, "animado.gif");
    public string StillGif => Path.Combine(Root, "estatico.gif");
    public string Truncated => Path.Combine(Root, "cortado.mp4");
    public string Garbage => Path.Combine(Root, "lixo.mp4");
    public string TextFile => Path.Combine(Root, "texto.xyz");
    public string SequenceFirstFrame { get; }

    /// <summary>Executa o ffmpeg.exe (utilitário para testes que precisam converter arquivos).</summary>
    public static void Ffmpeg(string args) => Run(args);

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

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition(Name)]
public sealed class SampleMediaCollection : ICollectionFixture<SampleMedia>
{
    public const string Name = "Mídias de exemplo";
}
