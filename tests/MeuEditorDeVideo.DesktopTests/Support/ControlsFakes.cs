// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Hosting;
using MeuEditorDeVideo.Infrastructure.Paths;
using MeuEditorDeVideo.Presentation.Hosting;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels.Media;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeuEditorDeVideo.DesktopTests.Support;

/// <summary>
/// Linha do tempo real (serviços reais do aplicativo, sem janelas) com um projeto de exemplo: duas trilhas de vídeo e
/// duas de áudio, com "Praia.mp4" e "Floresta Amazônica.mp4". As pastas de dados ficam num diretório temporário.
/// </summary>
internal sealed class TimelineFixture : IDisposable
{
    private readonly IHost _host;
    private readonly string _root;

    public TimelineFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "Editor de Vídeo testes " + Guid.NewGuid().ToString("N")[..8]);
        var paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _host = Host.CreateDefaultBuilder()
            .UseMeuEditorInfrastructure(paths)
            .ConfigureServices(s =>
            {
                s.AddMeuEditor();
                s.AddSingleton<IDialogService, DialogService>();
                s.AddSingleton<IThemeService, ThemeService>();
            })
            .Build();

        Projects = Services.GetRequiredService<IProjectService>();
        Projects.CreateUntitled("Férias");
        Timeline = new TimelineViewModel(Projects, Services.GetRequiredService<IDialogService>(), Services.GetRequiredService<MediaLibraryViewModel>());
        Build();
    }

    public IServiceProvider Services => _host.Services;

    public IProjectService Projects { get; }

    public TimelineViewModel Timeline { get; }

    public Guid Beach { get; } = Guid.NewGuid();

    public Guid Forest { get; } = Guid.NewGuid();

    public Guid BeachClip { get; private set; }

    public Guid ForestClip { get; private set; }

    public Guid ForestAudioClip { get; private set; }

    public Guid VideoTrack => Timeline.Sequence!.Tracks.First(t => t.Kind == TrackKind.Video).Id;

    private static MediaAsset Asset(Guid id, string name, double seconds) => new()
    {
        Id = id,
        Kind = MediaKind.Video,
        Name = name,
        OriginalPath = Path.Combine("C:\\Vídeos", name),
        Duration = MediaTime.FromSecondsApprox(seconds),
        Width = 1920,
        Height = 1080,
        FrameRate = FrameRate.Fps30,
        Info = new MediaTechnicalInfo { AudioCodec = "aac", SampleRate = 48000, Channels = 2 },
    };

    private void Build()
    {
        Projects.Execute(new AddMediaAssetsCommand([Asset(Beach, "Praia.mp4", 12), Asset(Forest, "Floresta Amazônica.mp4", 20)]));
        var seq = Timeline.Sequence!;
        var video = seq.Tracks.First(t => t.Kind == TrackKind.Video);
        var audio = seq.Tracks.First(t => t.Kind == TrackKind.Audio);
        var first = new AddMediaToTimelineCommand(seq.Id, Beach, video.Id, audio.Id, MediaTime.Zero, MediaTime.FromSecondsApprox(5));
        var second = new AddMediaToTimelineCommand(seq.Id, Forest, video.Id, audio.Id, MediaTime.FromSecondsApprox(13), MediaTime.FromSecondsApprox(5));
        Projects.Execute(first);
        Projects.Execute(second);
        BeachClip = first.VideoClipId;
        ForestClip = second.VideoClipId;
        ForestAudioClip = second.AudioClipId;
        Projects.Execute(new AddMarkerCommand(seq.Id, MediaTime.FromSecondsApprox(6), "Pôr do sol"));

        // Miniatura (gradiente) e forma de onda (senoide) das duas mídias, como se a biblioteca já as tivesse lido.
        foreach (var id in new[] { Beach, Forest })
            Timeline.Library.Find(id)?.SetPreview(Gradient(160, 90, id == Beach ? 0 : 1), Sine(id == Beach ? 12 : 20));
        Timeline.ViewportWidth = 900;
        Timeline.SetZoom(60, 0);
    }

    /// <summary>Imagem BGRA com um degradê diagonal (tons de mar ou de mata).</summary>
    public static BgraImage Gradient(int width, int height, int palette)
    {
        var px = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var t = (x + y) / (double)(width + height);
                var (r, g, b) = palette == 0
                    ? (40 + 200 * t, 120 + 110 * t, 190 - 60 * t)
                    : (20 + 80 * t, 90 + 90 * (1 - t), 40 + 30 * t);
                px[i] = (byte)b;
                px[i + 1] = (byte)g;
                px[i + 2] = (byte)r;
                px[i + 3] = 255;
            }
        return new BgraImage(width, height, px);
    }

    /// <summary>Picos de uma senoide modulada (20 por segundo).</summary>
    public static WaveformData Sine(double seconds)
    {
        const int perSecond = 20;
        var count = (int)(seconds * perSecond);
        var min = new float[count];
        var max = new float[count];
        for (var i = 0; i < count; i++)
        {
            var amplitude = (float)(0.15 + 0.8 * Math.Abs(Math.Sin(i / 17.0)) * (0.6 + 0.4 * Math.Sin(i / 5.0)));
            max[i] = amplitude;
            min[i] = -amplitude * 0.9f;
        }
        return new WaveformData(perSecond, min, max, MediaTime.FromSecondsApprox(seconds));
    }

    public void Dispose()
    {
        Timeline.Dispose();
        _host.Dispose();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // Pasta temporária: o sistema limpa depois.
        }
    }
}
