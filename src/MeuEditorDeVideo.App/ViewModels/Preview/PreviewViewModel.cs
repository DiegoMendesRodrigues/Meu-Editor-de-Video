using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.ViewModels.Preview;

/// <summary>Resolução da pré-visualização em relação ao projeto (§6.5 — redução durante a edição).</summary>
public enum PreviewQuality
{
    Full = 1,
    Half = 2,
    Quarter = 4,
}

public sealed record PreviewQualityOption(PreviewQuality Value, string Label);

/// <summary>
/// Monitor (painel B): mostra o quadro da posição do cursor, reproduz com áudio sincronizado,
/// captura quadros e alimenta a tela cheia. O relógio da reprodução vem do <see cref="PreviewEngine"/>.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject, IDisposable
{
    private readonly PreviewEngine _engine;
    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly ILogger<PreviewViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private ProjectSession? _session;
    private PreviewFrame? _latest;
    private int _presentQueued;
    private bool _syncingPlayhead;
    private static string? s_lastCaptureDirectory;

    public PreviewViewModel(PreviewEngine engine, IProjectService projects, IDialogService dialogs, ISettingsService settings,
        ILogger<PreviewViewModel> logger, TimelineViewModel timeline)
    {
        _engine = engine;
        _projects = projects;
        _dialogs = dialogs;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Timeline = timeline;

        _engine.SetHardwareDecoding(settings.Current.UseGpuAcceleration);
        _engine.FrameReady += OnFrameReady;
        _engine.PlaybackEnded += OnPlaybackEnded;
        _projects.SessionChanged += OnSessionChanged;
        Timeline.PropertyChanged += OnTimelinePropertyChanged;
        Timeline.PlayPauseRequested += OnPlayPauseRequested;
        Attach();
    }

    public TimelineViewModel Timeline { get; }

    public IReadOnlyList<PreviewQualityOption> Qualities { get; } =
    [
        new(PreviewQuality.Full, "Completa"),
        new(PreviewQuality.Half, "1/2"),
        new(PreviewQuality.Quarter, "1/4"),
    ];

    /// <summary>Quadro exibido no monitor e na tela cheia.</summary>
    [ObservableProperty]
    public partial WriteableBitmap? Image { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial PreviewQualityOption Quality { get; set; } = null!;

    /// <summary>Volume do monitor (0–100).</summary>
    [ObservableProperty]
    public partial double Volume { get; set; } = 80;

    /// <summary>Resolução e tempo de renderização do último quadro (diagnóstico de desempenho).</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public bool HasContent => Timeline.Duration > MediaTime.Zero;

    public string PlayGlyph => IsPlaying ? "" : "";

    public string PlayToolTip => IsPlaying ? "Pausar (Espaço)" : "Reproduzir (Espaço)";

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(PlayToolTip));
    }

    partial void OnQualityChanged(PreviewQualityOption value) => ApplyPreviewSize();

    partial void OnVolumeChanged(double value) => _engine.Volume = (float)(Math.Clamp(value, 0, 100) / 100);

    /// <summary>Comparação antes/depois dos efeitos e da correção de cor (RF101): metade esquerda sem efeitos.</summary>
    [ObservableProperty]
    public partial bool CompareMode { get; set; }

    partial void OnCompareModeChanged(bool value) => _engine.CompareMode = value;

    // ---------------------------------------------------------------- sessão

    private void OnSessionChanged(object? sender, EventArgs e) => _dispatcher.Invoke(Attach);

    private void OnHistoryChanged(object? sender, EventArgs e) => PushSnapshot();

    private void Attach()
    {
        if (!ReferenceEquals(_session, _projects.Current))
        {
            if (IsPlaying) Pause();
            if (_session is not null) _session.History.Changed -= OnHistoryChanged;
            _session = _projects.Current;
            if (_session is not null) _session.History.Changed += OnHistoryChanged;
            Quality ??= Qualities[1];
            Volume = Volume;
        }
        ApplyPreviewSize();
        PushSnapshot();
    }

    private void PushSnapshot()
    {
        var s = _session;
        if (s?.Project.Sequences.FirstOrDefault() is not { } seq) return;
        _engine.SetSnapshot(new PreviewSnapshot(s.Project, seq, s.Media.AvailablePaths));
        OnPropertyChanged(nameof(HasContent));
        if (!IsPlaying) _engine.Seek(Timeline.Playhead);
        PlayPauseCommand.NotifyCanExecuteChanged();
        CaptureFrameCommand.NotifyCanExecuteChanged();
    }

    private void ApplyPreviewSize()
    {
        var settings = _session?.Project.Settings;
        if (settings is null || Quality is null) return;
        var div = (int)Quality.Value;
        // Tamanhos pares para conversões de cor; limite inferior para não ficar ilegível.
        var w = Math.Max(160, settings.Width / div / 2 * 2);
        var h = Math.Max(90, settings.Height / div / 2 * 2);
        _engine.SetPreviewSize(w, h);
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimelineViewModel.Playhead) || _syncingPlayhead) return;
        _engine.Seek(Timeline.Playhead); // durante a reprodução, recomeça do novo ponto
    }

    // ---------------------------------------------------------------- quadros

    /// <summary>Thread de renderização → interface: guarda o último quadro e agenda uma única apresentação.</summary>
    private void OnFrameReady(object? sender, PreviewFrame frame)
    {
        Volatile.Write(ref _latest, frame);
        if (Interlocked.Exchange(ref _presentQueued, 1) == 0)
            _dispatcher.BeginInvoke(DispatcherPriority.Render, Present);
    }

    private void Present()
    {
        Interlocked.Exchange(ref _presentQueued, 0);
        if (Volatile.Read(ref _latest) is not { } frame) return;
        var img = frame.Image;
        if (Image is null || Image.PixelWidth != img.Width || Image.PixelHeight != img.Height)
            Image = new WriteableBitmap(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null);
        Image.WritePixels(new Int32Rect(0, 0, img.Width, img.Height), img.Pixels, img.Stride, 0);
        Status = string.Create(CultureInfo.GetCultureInfo("pt-BR"),
            $"{Quality?.Label} · {img.Width} × {img.Height} · {frame.RenderTime.TotalMilliseconds:0} ms");
    }

    // ---------------------------------------------------------------- reprodução

    private bool CanPlay() => _session is not null && HasContent;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void PlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    private void OnPlayPauseRequested(object? sender, EventArgs e)
    {
        if (PlayPauseCommand.CanExecute(null)) PlayPause();
    }

    private void Play()
    {
        var from = Timeline.Playhead >= Timeline.Duration ? MediaTime.Zero : Timeline.Playhead;
        _engine.Play(from);
        IsPlaying = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Pause()
    {
        CompositionTarget.Rendering -= OnRendering;
        var at = _engine.Pause();
        IsPlaying = false;
        SyncPlayhead(at);
    }

    /// <summary>A cada quadro da interface, o cursor acompanha o relógio do áudio.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_engine.IsPlaying) return;
        SyncPlayhead(_engine.Position);
    }

    private void SyncPlayhead(MediaTime time)
    {
        _syncingPlayhead = true;
        try
        {
            Timeline.SetPlayhead(time);
        }
        finally
        {
            _syncingPlayhead = false;
        }
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        CompositionTarget.Rendering -= OnRendering;
        IsPlaying = false;
        SyncPlayhead(Timeline.Duration);
    });

    // ---------------------------------------------------------------- captura

    /// <summary>Salva o quadro do cursor em PNG na resolução total do projeto.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task CaptureFrame()
    {
        if (_session is not { } s) return;
        if (IsPlaying) Pause();
        var time = Timeline.Playhead;
        var name = $"{s.Project.Name} {Timecode.Format(time, s.Project.Settings.FrameRate).Replace(':', '-')}.png";
        var dir = s_lastCaptureDirectory ?? (s.FilePath is { } fp ? Path.GetDirectoryName(fp) : null);
        var path = _dialogs.PickSaveFile("Capturar quadro", "Imagem PNG (*.png)|*.png", name, dir);
        if (path is null) return;
        s_lastCaptureDirectory = Path.GetDirectoryName(path);

        try
        {
            var image = await _engine.CaptureAsync(time, s.Project.Settings.Width, s.Project.Settings.Height);
            if (image is null) return;
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var file = File.Create(path);
            encoder.Save(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Falha ao capturar quadro.");
            _dialogs.ShowInfo("Não foi possível salvar a imagem", ex.Message);
        }
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        _engine.FrameReady -= OnFrameReady;
        _engine.PlaybackEnded -= OnPlaybackEnded;
        _projects.SessionChanged -= OnSessionChanged;
        Timeline.PropertyChanged -= OnTimelinePropertyChanged;
        Timeline.PlayPauseRequested -= OnPlayPauseRequested;
        if (_session is not null) _session.History.Changed -= OnHistoryChanged;
        _engine.Dispose();
    }
}
