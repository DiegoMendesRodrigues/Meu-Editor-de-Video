using System.ComponentModel;
using System.Globalization;
using System.IO;
using MeuEditorDeVideo.Presentation.Media;
using MeuEditorDeVideo.Presentation.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using MeuEditorDeVideo.Application.Performance;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.RenderingEngine;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.Presentation.ViewModels.Preview;

/// <summary>Resolução da pré-visualização em relação ao projeto (§6.5 — redução durante a edição).</summary>
public enum PreviewQuality
{
    Full = 1,
    Half = 2,
    Quarter = 4,
}

/// <summary>Opção do seletor de qualidade; <paramref name="IsAuto"/> = escolhida pelo hardware e ajustada durante a reprodução (§12.3).</summary>
public sealed record PreviewQualityOption(PreviewQuality Value, string Label, bool IsAuto = false)
{
    /// <summary>Rótulo no idioma da interface.</summary>
    public string Label { get; init; } = MeuEditorDeVideo.Presentation.Localization.Translator.T(Label);

    /// <summary>Texto lido por leitores de tela.</summary>
    public override string ToString() => Label;
}

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
    private readonly IUiDispatcher _dispatcher;
    private readonly IProxyService _proxies;
    private readonly ISettingsService _settings;
    private readonly IHardwareInfoService? _hardware;
    private ProjectSession? _session;
    private PerformanceProfile? _profile;

    /// <summary>Divisor recomendado pelo hardware para o projeto atual e divisor após as reduções automáticas.</summary>
    private int _autoDivisor = 2, _adaptiveDivisor = 2;
    private readonly Queue<double> _renderTimes = new();
    private PreviewFrame? _latest;
    private int _presentQueued;
    private bool _syncingPlayhead;
    private static string? s_lastCaptureDirectory;

    public PreviewViewModel(PreviewEngine engine, IProjectService projects, IDialogService dialogs, ISettingsService settings,
        IProxyService proxies, ILogger<PreviewViewModel> logger, TimelineViewModel timeline, IHardwareInfoService? hardware = null)
    {
        _hardware = hardware;
        _proxies = proxies;
        _settings = settings;
        UseProxies = settings.Current.UseProxies;
        _proxies.StatusChanged += OnProxyStatusChanged;
        _engine = engine;
        _projects = projects;
        _dialogs = dialogs;
        _logger = logger;
        _dispatcher = UiThread.Dispatcher;
        Timeline = timeline;

        _engine.SetHardwareDecoding(settings.Current.UseGpuAcceleration);
        _settings.Changed += OnSettingsChanged;
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
        new(PreviewQuality.Half, "Automática", IsAuto: true),
        new(PreviewQuality.Full, "Completa"),
        new(PreviewQuality.Half, "1/2"),
        new(PreviewQuality.Quarter, "1/4"),
    ];

    /// <summary>Quadro exibido no monitor e na tela cheia.</summary>
    [ObservableProperty]
    public partial DisplayFrame? Image { get; set; }

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

    partial void OnQualityChanged(PreviewQualityOption value)
    {
        // Escolher "Automática" de novo recomeça das recomendações do hardware.
        _adaptiveDivisor = _autoDivisor;
        _renderTimes.Clear();
        AutoNotice = "";
        ApplyPreviewSize();
    }

    /// <summary>Aviso quando a qualidade automática foi reduzida porque o computador não acompanhou a reprodução.</summary>
    [ObservableProperty]
    public partial string AutoNotice { get; set; } = "";

    /// <summary>Divisor da resolução em uso (1, 2 ou 4).</summary>
    public int EffectiveDivisor => Quality is { IsAuto: true } ? Math.Max(_autoDivisor, _adaptiveDivisor) : (int)(Quality?.Value ?? PreviewQuality.Half);

    private static string DivisorLabel(int div) => div switch { 1 => "Completa", 2 => "1/2", _ => "1/4" };

    private static int Divisor(PreviewQualityMode mode) => mode switch
    {
        PreviewQualityMode.Full => 1,
        PreviewQualityMode.Quarter => 4,
        _ => 2,
    };

    private PreviewQualityOption OptionFor(PreviewQualityMode mode) => mode switch
    {
        PreviewQualityMode.Auto => Qualities[0],
        PreviewQualityMode.Full => Qualities[1],
        PreviewQualityMode.Quarter => Qualities[3],
        _ => Qualities[2],
    };

    /// <summary>Recalcula a recomendação (§12.3) para o projeto e aplica a memória dos quadros prontos (RNF013).</summary>
    private async Task EvaluateHardwareAsync()
    {
        var settings = _session?.Project.Settings;
        if (_hardware is null || settings is null) return;
        try
        {
            // A primeira detecção testa os codificadores: fora da thread da interface.
            var hardware = _hardware;
            var info = await Task.Run(() => hardware.Get());
            _profile = PerformanceAdvisor.Evaluate(info, settings.Width, settings.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível avaliar o hardware para a pré-visualização.");
            return;
        }
        _autoDivisor = Divisor(_profile.Preview);
        _adaptiveDivisor = _autoDivisor;
        _renderTimes.Clear();
        _engine.CacheBudgetBytes = (long)PerformanceAdvisor.ResolvePreviewMemory(_settings.Current.PreviewMemoryMegabytes, _profile) << 20;
        if (Quality is { IsAuto: true }) ApplyPreviewSize();
    }

    private void OnSettingsChanged(object? sender, AppSettings value) => _dispatcher.Post(() =>
    {
        _engine.SetHardwareDecoding(value.UseGpuAcceleration);
        if (_profile is not null)
            _engine.CacheBudgetBytes = (long)PerformanceAdvisor.ResolvePreviewMemory(value.PreviewMemoryMegabytes, _profile) << 20;
        if (_lastQualitySetting != value.PreviewQuality)
        {
            _lastQualitySetting = value.PreviewQuality;
            Quality = OptionFor(value.PreviewQuality);
        }
    });

    private PreviewQualityMode _lastQualitySetting;

    /// <summary>
    /// Qualidade automática (§12.3): se a renderização média dos últimos quadros passar de 1,5 × a duração do quadro
    /// durante a reprodução, a resolução cai um nível (até 1/4). Não volta a subir sozinha, para não oscilar.
    /// </summary>
    private void TrackRenderTime(PreviewFrame frame)
    {
        if (!IsPlaying || Quality is not { IsAuto: true } || _session is null || EffectiveDivisor >= 4) return;
        _renderTimes.Enqueue(frame.RenderTime.TotalMilliseconds);
        if (_renderTimes.Count > 24) _renderTimes.Dequeue();
        if (_renderTimes.Count < 24) return;
        var budget = 1000 * _session.Project.Settings.FrameRate.FrameDuration.TotalSeconds;
        if (_renderTimes.Average() <= 1.5 * budget) return;
        _adaptiveDivisor = EffectiveDivisor * 2;
        _renderTimes.Clear();
        AutoNotice = $"Qualidade reduzida automaticamente para {DivisorLabel(EffectiveDivisor)}: o computador não acompanhou a reprodução.";
        ApplyPreviewSize();
    }

    partial void OnVolumeChanged(double value) => _engine.Volume = (float)(Math.Clamp(value, 0, 100) / 100);

    /// <summary>Níveis de áudio no instante ouvido (medidores do mixer).</summary>
    public MeuEditorDeVideo.AudioEngine.MixerLevels Levels => _engine.CurrentLevels;

    /// <summary>Usa proxies 540p prontos nos quadros da pré-visualização (a exportação usa sempre o original).</summary>
    [ObservableProperty]
    public partial bool UseProxies { get; set; }

    partial void OnUseProxiesChanged(bool value)
    {
        PushSnapshot();
        if (_settings.Current.UseProxies != value) _ = _settings.SaveAsync(_settings.Current with { UseProxies = value });
    }

    private void OnProxyStatusChanged(object? sender, ProxyStatus status)
    {
        if (status.State is ProxyState.Ready or ProxyState.None) _dispatcher.Post(PushSnapshot);
    }

    /// <summary>Mídias com proxy pronto (quando o uso de proxies está ligado).</summary>
    private Dictionary<Guid, string> ProxyPaths(ProjectSession s)
    {
        var result = new Dictionary<Guid, string>();
        if (!UseProxies) return result;
        foreach (var (id, path) in s.Media.AvailablePaths)
            if (s.Project.FindMedia(id) is { } asset && _proxies.ReadyPath(asset, path) is { } proxy)
                result[id] = proxy;
        return result;
    }

    /// <summary>Comparação antes/depois dos efeitos e da correção de cor (RF101): metade esquerda sem efeitos.</summary>
    [ObservableProperty]
    public partial bool CompareMode { get; set; }

    partial void OnCompareModeChanged(bool value) => _engine.CompareMode = value;

    /// <summary>Posição da linha da comparação (0 a 1); o usuário arrasta a linha no monitor.</summary>
    [ObservableProperty]
    public partial double CompareSplit { get; set; } = 0.5;

    partial void OnCompareSplitChanged(double value) => _engine.CompareSplit = value;

    // ---------------------------------------------------------------- sessão

    private void OnSessionChanged(object? sender, EventArgs e) => _dispatcher.Invoke(Attach);

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        // O tamanho do projeto pode ter mudado (Aplicar recorte, Reenquadrar, desfazer): o monitor acompanha.
        ApplyPreviewSize();
        PushSnapshot();
    }

    private void Attach()
    {
        if (!ReferenceEquals(_session, _projects.Current))
        {
            if (IsPlaying) Pause();
            if (_session is not null) _session.History.Changed -= OnHistoryChanged;
            _session = _projects.Current;
            if (_session is not null) _session.History.Changed += OnHistoryChanged;
            _ = EvaluateHardwareAsync();
            if (Quality is null)
            {
                _lastQualitySetting = _settings.Current.PreviewQuality;
                Quality = OptionFor(_lastQualitySetting);
            }
            Volume = Volume;
        }
        ApplyPreviewSize();
        PushSnapshot();
    }

    private void PushSnapshot()
    {
        var s = _session;
        if (s?.Project.Sequences.FirstOrDefault() is not { } seq) return;
        _engine.SetSnapshot(new PreviewSnapshot(s.Project, seq, s.Media.AvailablePaths) { ProxyPaths = ProxyPaths(s) });
        OnPropertyChanged(nameof(HasContent));
        if (!IsPlaying) _engine.Seek(Timeline.Playhead);
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CaptureFrameCommand.NotifyCanExecuteChanged();
    }

    private void ApplyPreviewSize()
    {
        var settings = _session?.Project.Settings;
        if (settings is null || Quality is null) return;
        var div = EffectiveDivisor;
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
            _dispatcher.Post(Present, UiPriority.Render);
    }

    private void Present()
    {
        Interlocked.Exchange(ref _presentQueued, 0);
        if (Volatile.Read(ref _latest) is not { } frame) return;
        var img = frame.Image;
        Image = new DisplayFrame(img);
        var label = Quality is { IsAuto: true } ? $"{MeuEditorDeVideo.Presentation.Localization.Translator.T("Auto")} {MeuEditorDeVideo.Presentation.Localization.Translator.T(DivisorLabel(EffectiveDivisor))}" : Quality?.Label;
        Status = string.Create(CultureInfo.GetCultureInfo("pt-BR"),
            $"{label} · {img.Width} × {img.Height} · {frame.RenderTime.TotalMilliseconds:0} ms");
        TrackRenderTime(frame);
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

    /// <summary>Parar: interrompe a reprodução e volta o cursor ao ponto onde ela começou (ou ao início).</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Stop()
    {
        if (IsPlaying) Pause();
        Timeline.SetPlayhead(_playStart ?? (LoopPlayback ? LoopRange().Start : MediaTime.Zero));
        _playStart = null;
    }

    /// <summary>Reprodução em loop: repete o trecho dos clipes selecionados ou, sem seleção, a sequência inteira.</summary>
    [ObservableProperty]
    public partial bool LoopPlayback { get; set; }

    private MediaTime? _playStart;

    private (MediaTime Start, MediaTime End) LoopRange() => Timeline.SelectionRange() ?? (MediaTime.Zero, Timeline.Duration);

    private void Play()
    {
        var from = Timeline.Playhead >= Timeline.Duration ? MediaTime.Zero : Timeline.Playhead;
        if (LoopPlayback)
        {
            var (start, end) = LoopRange();
            if (from < start || from >= end) from = start;
        }
        _playStart = from;
        _engine.Play(from);
        IsPlaying = true;
        _dispatcher.Rendering += OnRendering;
    }

    private void Pause()
    {
        _dispatcher.Rendering -= OnRendering;
        var at = _engine.Pause();
        IsPlaying = false;
        SyncPlayhead(at);
    }

    /// <summary>A cada quadro da interface, o cursor acompanha o relógio do áudio.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_engine.IsPlaying) return;
        var position = _engine.Position;
        if (LoopPlayback && LoopRange() is var (start, end) && end > start && position >= end)
        {
            RestartLoop(start);
            return;
        }
        SyncPlayhead(position);
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

    /// <summary>Volta ao início do trecho em loop sem parar a reprodução.</summary>
    private void RestartLoop(MediaTime start)
    {
        _engine.Pause();
        _engine.Play(start);
        SyncPlayhead(start);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        if (LoopPlayback && IsPlaying && LoopRange() is var (start, end) && end > start)
        {
            RestartLoop(start);
            return;
        }
        _dispatcher.Rendering -= OnRendering;
        IsPlaying = false;
        SyncPlayhead(Timeline.Duration);
    });

    // ---------------------------------------------------------------- captura

    /// <summary>Salva o quadro do cursor em PNG ou JPEG na resolução total do projeto.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task CaptureFrame()
    {
        if (_session is not { } s) return;
        if (IsPlaying) Pause();
        var time = Timeline.Playhead;
        var name = $"{s.Project.Name} {Timecode.Format(time, s.Project.Settings.FrameRate).Replace(':', '-')}.png";
        var dir = s_lastCaptureDirectory ?? (s.FilePath is { } fp ? Path.GetDirectoryName(fp) : null);
        var path = _dialogs.PickSaveFile("Capturar quadro", "Imagem PNG (*.png)|*.png|Imagem JPEG (*.jpg)|*.jpg", name, dir);
        if (path is null) return;
        s_lastCaptureDirectory = Path.GetDirectoryName(path);

        try
        {
            var image = await _engine.CaptureAsync(time, s.Project.Settings.Width, s.Project.Settings.Height);
            if (image is null) return;
            await Task.Run(() => ImageFiles.Save(image, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Falha ao capturar quadro.");
            _dialogs.ShowInfo("Não foi possível salvar a imagem", ex.Message);
        }
    }

    public void Dispose()
    {
        _dispatcher.Rendering -= OnRendering;
        _engine.FrameReady -= OnFrameReady;
        _engine.PlaybackEnded -= OnPlaybackEnded;
        _proxies.StatusChanged -= OnProxyStatusChanged;
        _projects.SessionChanged -= OnSessionChanged;
        _settings.Changed -= OnSettingsChanged;
        Timeline.PropertyChanged -= OnTimelinePropertyChanged;
        Timeline.PlayPauseRequested -= OnPlayPauseRequested;
        if (_session is not null) _session.History.Changed -= OnHistoryChanged;
        _engine.Dispose();
    }
}
