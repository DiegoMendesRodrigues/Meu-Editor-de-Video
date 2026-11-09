using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.TimelineEngine;

namespace MeuEditorDeVideo.App.ViewModels.Timeline;

/// <summary>Cabeçalho de trilha (nome, ocultar, silenciar, bloquear) — RF038–RF043.</summary>
public sealed partial class TrackHeaderViewModel(TimelineViewModel owner, Track track, double height) : ObservableObject
{
    public Track Track { get; } = track;
    public Guid Id => Track.Id;
    public string Name => Track.Name;
    public double Height { get; } = height;
    public bool IsLocked => Track.IsLocked;
    public bool IsHidden => Track.IsHidden;
    public bool IsMuted => Track.IsMuted;
    public bool IsAudio => Track.Kind == TrackKind.Audio;
    public bool CanMute => Track.Kind is TrackKind.Audio or TrackKind.Video;
    public bool CanHide => Track.Kind != TrackKind.Audio;

    public string KindGlyph => Track.Kind switch
    {
        TrackKind.Video => "",
        TrackKind.Audio => "",
        TrackKind.Image => "",
        TrackKind.Text => "",
        TrackKind.Subtitle => "",
        _ => "",
    };

    public string HideGlyph => IsHidden ? "" : "";
    public string MuteGlyph => IsMuted ? "" : "";
    public string LockGlyph => IsLocked ? "" : "";

    public string HideToolTip => IsHidden ? $"Exibir trilha \"{Name}\"" : $"Ocultar trilha \"{Name}\"";
    public string MuteToolTip => IsMuted ? $"Ativar som da trilha \"{Name}\"" : $"Silenciar trilha \"{Name}\"";
    public string LockToolTip => IsLocked ? $"Desbloquear trilha \"{Name}\"" : $"Bloquear trilha \"{Name}\"";

    [RelayCommand] private void ToggleLock() => owner.Execute(UpdateTrackCommand.SetLocked(owner.SequenceId, Id, !IsLocked));
    [RelayCommand] private void ToggleHidden() => owner.Execute(UpdateTrackCommand.SetHidden(owner.SequenceId, Id, !IsHidden));
    [RelayCommand] private void ToggleMuted() => owner.Execute(UpdateTrackCommand.SetMuted(owner.SequenceId, Id, !IsMuted));
    [RelayCommand] private void Rename() => owner.RenameTrack(this);
    [RelayCommand] private void Remove() => owner.RemoveTrack(this);
}

/// <summary>
/// Estado e comandos da linha do tempo (RF037–RF064). O desenho e o mouse ficam no TimelineControl,
/// que chama os métodos públicos daqui; toda alteração passa por comandos do histórico.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject, IDisposable
{
    public const double MinPixelsPerSecond = 2;
    public const double MaxPixelsPerSecond = 1200;
    public const double RulerHeight = 28;

    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private ProjectSession? _session;
    private List<ClipboardItem> _clipboard = [];
    private readonly HashSet<Guid> _selection = [];

    public TimelineViewModel(IProjectService projects, IDialogService dialogs, MediaLibraryViewModel library)
    {
        _projects = projects;
        _dialogs = dialogs;
        Library = library;
        Library.PreviewsChanged += OnPreviewsChanged;
        Library.PropertyChanged += OnLibraryPropertyChanged;
        _projects.SessionChanged += OnSessionChanged;
        Attach();
    }

    public MediaLibraryViewModel Library { get; }

    /// <summary>Espaço na linha do tempo: o monitor alterna reproduzir/pausar.</summary>
    public event EventHandler? PlayPauseRequested;

    public void RequestPlayPause() => PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Redesenho necessário (estado, seleção, cursor, zoom ou miniaturas mudaram).</summary>
    public event EventHandler? Invalidated;

    public ObservableCollection<TrackHeaderViewModel> Tracks { get; } = [];

    public Project? Project => _session?.Project;
    public Sequence? Sequence => _session?.Project.Sequences.FirstOrDefault();
    public Guid SequenceId => Sequence?.Id ?? Guid.Empty;
    public FrameRate FrameRate => _session?.Project.Settings.FrameRate ?? FrameRate.Fps30;
    public bool HasSession => _session is not null;
    public IReadOnlySet<Guid> Selection => _selection;

    /// <summary>Caminhos resolvidos das mídias do projeto aberto (para análises de áudio).</summary>
    public IReadOnlyDictionary<Guid, string> MediaPaths => _session?.Media.AvailablePaths ?? new Dictionary<Guid, string>();
    public bool HasSelection => _selection.Count > 0;

    /// <summary>Zoom: pixels por segundo (RF057).</summary>
    [ObservableProperty]
    public partial double PixelsPerSecond { get; set; } = 40;

    /// <summary>Deslocamento horizontal em pixels.</summary>
    [ObservableProperty]
    public partial double HorizontalOffset { get; set; }

    /// <summary>Largura visível da área de clipes (informada pelo controle).</summary>
    [ObservableProperty]
    public partial double ViewportWidth { get; set; } = 800;

    [ObservableProperty]
    public partial MediaTime Playhead { get; set; }

    /// <summary>Ímã de alinhamento (RF054).</summary>
    [ObservableProperty]
    public partial bool SnapEnabled { get; set; } = true;

    /// <summary>Edição ripple: excluir/aparar desloca os clipes seguintes (RF048, RF050).</summary>
    [ObservableProperty]
    public partial bool RippleEnabled { get; set; }

    [ObservableProperty]
    public partial string TimeDisplay { get; set; } = "00:00:00:00 / 00:00:00:00";

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "";

    public double TracksHeight => Tracks.Sum(t => t.Height);

    public MediaTime Duration => Sequence?.Duration ?? MediaTime.Zero;

    /// <summary>Largura total rolável (duração + folga de 30 s ou uma tela).</summary>
    public double ContentWidth => Math.Max(ViewportWidth, (Duration.TotalSeconds + 30) * PixelsPerSecond);

    public double MaxHorizontalOffset => Math.Max(0, ContentWidth - ViewportWidth);

    /// <summary>Valor do controle deslizante de zoom (0–100, escala logarítmica).</summary>
    public double ZoomSlider
    {
        get => 100 * Math.Log(PixelsPerSecond / MinPixelsPerSecond) / Math.Log(MaxPixelsPerSecond / MinPixelsPerSecond);
        set => SetZoom(MinPixelsPerSecond * Math.Pow(MaxPixelsPerSecond / MinPixelsPerSecond, Math.Clamp(value, 0, 100) / 100), null);
    }

    public string ZoomLabel => PixelsPerSecond >= 10
        ? string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"{PixelsPerSecond:0} px/s")
        : string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"{PixelsPerSecond:0.0} px/s");

    // ---------- Conversões ----------

    public double TimeToX(MediaTime t) => t.TotalSeconds * PixelsPerSecond - HorizontalOffset;

    public MediaTime XToTime(double x) => MediaTime.FromSecondsApprox(Math.Max(0, (x + HorizontalOffset) / PixelsPerSecond));

    public MediaTime PixelsToTime(double px) => MediaTime.FromSecondsApprox(Math.Max(0, px) / PixelsPerSecond);

    public static double HeightOf(TrackKind kind) => kind switch
    {
        TrackKind.Video or TrackKind.Image => 64,
        TrackKind.Audio => 52,
        _ => 36,
    };

    /// <summary>Trilha na coordenada vertical da área de trilhas (0 = topo da primeira trilha).</summary>
    public TrackHeaderViewModel? TrackAt(double y)
    {
        double top = 0;
        foreach (var t in Tracks)
        {
            if (y >= top && y < top + t.Height) return t;
            top += t.Height;
        }
        return null;
    }

    public double TrackTop(Guid trackId)
    {
        double top = 0;
        foreach (var t in Tracks)
        {
            if (t.Id == trackId) return top;
            top += t.Height;
        }
        return -1;
    }

    public SnapEngine CreateSnap(IReadOnlySet<Guid>? exclude = null) =>
        new(Sequence ?? new Sequence { Id = Guid.Empty, Name = "" }, FrameRate, Playhead, exclude);

    /// <summary>Tolerância do ímã: 8 pixels convertidos em tempo.</summary>
    public MediaTime SnapTolerance => PixelsToTime(8);

    // ---------- Sessão ----------

    private void OnSessionChanged(object? sender, EventArgs e) => Attach();

    private void OnHistoryChanged(object? sender, EventArgs e) => Sync();

    private void OnPreviewsChanged(object? sender, EventArgs e) => Invalidate();

    private void OnLibraryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaLibraryViewModel.SelectedItem)) AddSelectedMediaCommand.NotifyCanExecuteChanged();
    }

    private void Attach()
    {
        if (!ReferenceEquals(_session, _projects.Current))
        {
            if (_session is not null) _session.History.Changed -= OnHistoryChanged;
            _session = _projects.Current;
            if (_session is not null) _session.History.Changed += OnHistoryChanged;
            _selection.Clear();
            _clipboard = [];
            Playhead = MediaTime.Zero;
            HorizontalOffset = 0;
        }
        Sync();
    }

    private void Sync()
    {
        var seq = Sequence;
        var tracks = seq?.Tracks ?? [];
        if (Tracks.Count != tracks.Count || !Tracks.Select(t => t.Track).SequenceEqual(tracks, ReferenceEqualityComparer.Instance))
        {
            Tracks.Clear();
            foreach (var t in tracks) Tracks.Add(new TrackHeaderViewModel(this, t, HeightOf(t.Kind)));
            OnPropertyChanged(nameof(TracksHeight));
        }

        // Descarta da seleção clipes que deixaram de existir (desfazer, exclusão).
        if (seq is not null)
        {
            var existing = seq.Tracks.SelectMany(t => t.Clips).Select(c => c.Id).ToHashSet();
            _selection.IntersectWith(existing);
        }
        else
        {
            _selection.Clear();
        }

        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(Duration));
        UpdateTexts();
        NotifyCommands();
        Invalidate();
    }

    public void Invalidate()
    {
        OnPropertyChanged(nameof(ContentWidth));
        OnPropertyChanged(nameof(MaxHorizontalOffset));
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTexts()
    {
        TimeDisplay = $"{Timecode.Format(Playhead, FrameRate)} / {Timecode.Format(Duration, FrameRate)}";
        SelectionSummary = _selection.Count switch
        {
            0 => "",
            1 => "1 clipe selecionado",
            var n => $"{n} clipes selecionados",
        };
    }

    partial void OnPlayheadChanged(MediaTime value)
    {
        UpdateTexts();
        Invalidate();
    }

    partial void OnPixelsPerSecondChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomSlider));
        OnPropertyChanged(nameof(ZoomLabel));
        Invalidate();
    }

    partial void OnHorizontalOffsetChanged(double value) => Invalidated?.Invoke(this, EventArgs.Empty);

    partial void OnViewportWidthChanged(double value) => Invalidate();

    private void NotifyCommands()
    {
        foreach (var c in new IRelayCommand[]
        {
            SplitCommand, DeleteCommand, RippleDeleteCommand, CopyCommand, CutCommand, PasteCommand, DuplicateCommand,
            AddMarkerCommand, GroupCommand, UngroupCommand, LinkCommand, UnlinkCommand, SetSpeedCommand,
            AddSelectedMediaCommand, AddTrackCommand,
        })
        {
            c.NotifyCanExecuteChanged();
        }
    }

    // ---------- Execução ----------

    /// <summary>Executa um comando no histórico; recusas viram aviso amigável.</summary>
    public bool Execute(IEditCommand command)
    {
        if (_session is null) return false;
        try
        {
            _projects.Execute(command);
            return true;
        }
        catch (EditRejectedException ex)
        {
            _dialogs.ShowInfo("Não foi possível concluir", ex.Message);
            return false;
        }
    }

    // ---------- Seleção ----------

    public void Select(Guid clipId, bool additive)
    {
        if (Sequence is not { } seq) return;
        var group = TimelineOps.Expand(seq, [clipId]);
        if (additive && _selection.Contains(clipId))
        {
            _selection.ExceptWith(group);
        }
        else
        {
            if (!additive) _selection.Clear();
            _selection.UnionWith(group);
        }
        SelectionChangedInternal();
    }

    public void SelectOnlyIfNotSelected(Guid clipId)
    {
        if (!_selection.Contains(clipId)) Select(clipId, additive: false);
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        SelectionChangedInternal();
    }

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void SelectAll()
    {
        if (Sequence is not { } seq) return;
        _selection.UnionWith(seq.Tracks.Where(t => !t.IsLocked).SelectMany(t => t.Clips).Select(c => c.Id));
        SelectionChangedInternal();
    }

    private void SelectionChangedInternal()
    {
        OnPropertyChanged(nameof(HasSelection));
        UpdateTexts();
        NotifyCommands();
        Invalidate();
    }

    // ---------- Edição pelo mouse (chamado pelo controle) ----------

    public void MoveClips(IReadOnlyList<ClipMove> moves)
    {
        if (moves.Count > 0) Execute(new MoveClipsCommand(SequenceId, moves));
    }

    public void TrimClip(Guid clipId, ClipEdge edge, MediaTime time) =>
        Execute(new TrimClipCommand(SequenceId, clipId, edge, time, RippleEnabled));

    /// <summary>Solta uma mídia da biblioteca na trilha/tempo indicados (RF044).</summary>
    public void DropMedia(Guid assetId, Guid? trackId, MediaTime at)
    {
        if (Project?.FindMedia(assetId) is not { } asset || Sequence is not { } seq) return;
        var target = trackId is { } id ? seq.Tracks.Find(t => t.Id == id) : null;

        Guid? video = null, audio = null;
        if (asset.HasVideo)
            video = target is { Kind: TrackKind.Video or TrackKind.Image, IsLocked: false } ? target.Id : FirstFree(seq, TrackKind.Video, at, asset);
        if (asset.HasAudio)
            audio = target is { Kind: TrackKind.Audio, IsLocked: false } ? target.Id : FirstFree(seq, TrackKind.Audio, at, asset);

        // Soltou vídeo numa trilha de áudio: só o áudio vai para lá; o vídeo procura a trilha de vídeo livre.
        var command = new AddMediaToTimelineCommand(SequenceId, assetId, video, audio, at, MediaTime.FromSeconds(5));
        if (Execute(command))
        {
            _selection.Clear();
            if (video is not null) _selection.Add(command.VideoClipId);
            if (audio is not null) _selection.Add(command.AudioClipId);
            SelectionChangedInternal();
        }
    }

    /// <summary>Primeira trilha desbloqueada do tipo com espaço livre; senão a primeira desbloqueada.</summary>
    private static Guid? FirstFree(Sequence seq, TrackKind kind, MediaTime at, MediaAsset asset)
    {
        var candidates = seq.Tracks.Where(t => !t.IsLocked && TimelineOps.Accepts(t.Kind, asset)
                                               && (t.Kind == kind || (kind == TrackKind.Video && t.Kind == TrackKind.Image))).ToList();
        var end = at + (asset.Kind == MediaKind.Image ? MediaTime.FromSeconds(5) : asset.Duration);
        return (candidates.FirstOrDefault(t => !t.Clips.Any(c => c.Start < end && c.End > at)) ?? candidates.FirstOrDefault())?.Id;
    }

    // ---------- Comandos ----------

    private bool CanEditSelection() => HasSession && _selection.Count > 0;

    /// <summary>Dividir no cursor (RF049): a seleção, ou tudo que cruza o cursor se nada estiver selecionado.</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void Split()
    {
        var ids = _selection.Count > 0 && Sequence is { } seq
            && seq.Tracks.SelectMany(t => t.Clips).Any(c => _selection.Contains(c.Id) && c.Start < Playhead && c.End > Playhead)
            ? _selection.ToList()
            : null;
        Execute(new SplitClipsCommand(SequenceId, Playhead, ids));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Delete() => Execute(new RemoveClipsCommand(SequenceId, [.. _selection], RippleEnabled));

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void RippleDelete() => Execute(new RemoveClipsCommand(SequenceId, [.. _selection], Ripple: true));

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Copy()
    {
        if (Sequence is not { } seq) return;
        var ids = TimelineOps.Expand(seq, _selection);
        _clipboard = [.. seq.Tracks.SelectMany(t => t.Clips.Where(c => ids.Contains(c.Id)).Select(c => new ClipboardItem(t.Id, c)))];
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Cut()
    {
        Copy();
        Execute(new RemoveClipsCommand(SequenceId, [.. _selection], RippleEnabled));
    }

    private bool CanPaste() => HasSession && _clipboard.Count > 0;

    /// <summary>Cola no cursor (RF046).</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        var command = PasteClipsCommand.Create(SequenceId, _clipboard, Playhead);
        if (Execute(command)) SelectOnly(command.NewClipIds);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Duplicate()
    {
        if (Sequence is not { } seq) return;
        var command = PasteClipsCommand.Duplicate(seq, _selection);
        if (Execute(command)) SelectOnly(command.NewClipIds);
    }

    private void SelectOnly(IEnumerable<Guid> ids)
    {
        _selection.Clear();
        _selection.UnionWith(ids);
        SelectionChangedInternal();
    }

    [RelayCommand(CanExecute = nameof(HasSession))]
    private void AddMarker()
    {
        var n = (Sequence?.Markers.Count ?? 0) + 1;
        Execute(new AddMarkerCommand(SequenceId, Playhead, $"Marcador {n}"));
    }

    public void MoveMarker(Guid markerId, MediaTime time) => Execute(new UpdateMarkerCommand(SequenceId, markerId, Time: time));

    public void RenameMarker(Guid markerId)
    {
        var marker = Sequence?.Markers.Find(m => m.Id == markerId);
        if (marker is null) return;
        var name = _dialogs.Prompt("Renomear marcador", "Nome do marcador", marker.Name);
        if (name is not null) Execute(new UpdateMarkerCommand(SequenceId, markerId, Name: name));
    }

    public void RemoveMarker(Guid markerId) => Execute(new RemoveMarkerCommand(SequenceId, markerId));

    private bool CanGroup() => HasSession && _selection.Count > 1;

    [RelayCommand(CanExecute = nameof(CanGroup))]
    private void Group() => Execute(new GroupClipsCommand(SequenceId, [.. _selection], Group: true));

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Ungroup() => Execute(new GroupClipsCommand(SequenceId, [.. _selection], Group: false));

    [RelayCommand(CanExecute = nameof(CanGroup))]
    private void Link() => Execute(new LinkClipsCommand(SequenceId, [.. _selection], Link: true));

    /// <summary>Separa áudio e vídeo (RF064): a seleção passa a ser só o clipe clicado.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Unlink() => Execute(new LinkClipsCommand(SequenceId, [.. _selection], Link: false));

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void SetSpeed()
    {
        if (Sequence is not { } seq) return;
        var clip = seq.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => _selection.Contains(c.Id));
        if (clip is null) return;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var input = _dialogs.Prompt("Velocidade do clipe", "Velocidade (0,1 a 16; 1 = normal)", clip.Speed.Value.ToString("0.##", pt));
        if (input is null) return;
        if (!double.TryParse(input.Replace("×", "").Replace("x", "").Trim(), NumberStyles.Float, pt, out var value) || value <= 0)
        {
            _dialogs.ShowInfo("Velocidade inválida", "Informe um número entre 0,1 e 16, por exemplo 0,5 ou 2.");
            return;
        }
        var speed = new Rational((long)Math.Round(value * 1000), 1000);
        Execute(new SetClipSpeedCommand(SequenceId, clip.Id, speed));
    }

    [RelayCommand(CanExecute = nameof(CanAddSelectedMedia))]
    private void AddSelectedMedia()
    {
        if (Library.SelectedItem is not { IsMissing: false } item) return;
        // Adiciona no fim da sequência (RF044, botão "Adicionar à linha do tempo").
        DropMedia(item.Id, null, Duration);
        Playhead = Duration;
    }

    private bool CanAddSelectedMedia() => HasSession && Library.SelectedItem is { IsMissing: false };

    /// <summary>Adiciona trilha do tipo informado (RF037).</summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private void AddTrack(TrackKind kind)
    {
        if (Sequence is not { } seq) return;
        var baseName = kind switch
        {
            TrackKind.Video => "Vídeo",
            TrackKind.Audio => "Áudio",
            TrackKind.Image => "Imagem",
            TrackKind.Text => "Texto",
            TrackKind.Subtitle => "Legenda",
            _ => "Efeitos",
        };
        var n = seq.Tracks.Count(t => t.Kind == kind) + 1;
        while (seq.Tracks.Any(t => t.Name == $"{baseName} {n}")) n++;
        // Trilhas visuais entram acima das de áudio; áudio entra no fim.
        int? index = kind == TrackKind.Audio ? null : seq.Tracks.FindLastIndex(t => t.Kind != TrackKind.Audio) + 1;
        Execute(new AddTrackCommand(SequenceId, kind, $"{baseName} {n}", index));
    }

    public void RenameTrack(TrackHeaderViewModel track)
    {
        var name = _dialogs.Prompt("Renomear trilha", "Nome da trilha", track.Name);
        if (name is not null) Execute(UpdateTrackCommand.Rename(SequenceId, track.Id, name));
    }

    public void RemoveTrack(TrackHeaderViewModel track)
    {
        if (track.Track.Clips.Count > 0 &&
            !_dialogs.Confirm("Remover trilha", $"A trilha \"{track.Name}\" tem {track.Track.Clips.Count} clipe(s). Remover mesmo assim?"))
            return;
        Execute(new RemoveTrackCommand(SequenceId, track.Id));
    }

    // ---------- Navegação ----------

    [RelayCommand] private void StepBack() => SetPlayhead(Playhead - FrameRate.FrameDuration);
    [RelayCommand] private void StepForward() => SetPlayhead(Playhead + FrameRate.FrameDuration);
    [RelayCommand] private void StepBackSecond() => SetPlayhead(Playhead - MediaTime.FromSeconds(1));
    [RelayCommand] private void StepForwardSecond() => SetPlayhead(Playhead + MediaTime.FromSeconds(1));
    [RelayCommand] private void GoToStart() => SetPlayhead(MediaTime.Zero);
    [RelayCommand] private void GoToEnd() => SetPlayhead(Duration);

    /// <summary>Pontos de edição: bordas de clipes e marcadores (RF058).</summary>
    private IEnumerable<MediaTime> EditPoints() =>
        Sequence is { } seq
            ? seq.Tracks.SelectMany(t => t.Clips).SelectMany(c => new[] { c.Start, c.End }).Concat(seq.Markers.Select(m => m.Time)).Append(MediaTime.Zero)
            : [];

    [RelayCommand]
    private void PreviousEditPoint() =>
        SetPlayhead(EditPoints().Where(t => t < Playhead).DefaultIfEmpty(MediaTime.Zero).Max());

    [RelayCommand]
    private void NextEditPoint()
    {
        var next = EditPoints().Where(t => t > Playhead).DefaultIfEmpty(Playhead).Min();
        SetPlayhead(next);
    }

    public void SetPlayhead(MediaTime time, bool keepVisible = true)
    {
        var t = FrameRate.SnapToFrame(MediaTime.Max(MediaTime.Zero, time));
        Playhead = t;
        if (!keepVisible) return;
        var x = TimeToX(t);
        if (x < 0) HorizontalOffset = Math.Max(0, HorizontalOffset + x - 40);
        else if (x > ViewportWidth - 20) HorizontalOffset = Math.Max(0, HorizontalOffset + x - ViewportWidth + 80);
    }

    // ---------- Zoom ----------

    /// <summary>Muda o zoom mantendo fixo o tempo sob <paramref name="anchorX"/> (ou o cursor).</summary>
    public void SetZoom(double pps, double? anchorX)
    {
        pps = Math.Clamp(pps, MinPixelsPerSecond, MaxPixelsPerSecond);
        if (Math.Abs(pps - PixelsPerSecond) < 1e-9) return;
        var ax = anchorX ?? Math.Clamp(TimeToX(Playhead), 0, ViewportWidth);
        var anchorSeconds = (ax + HorizontalOffset) / PixelsPerSecond;
        PixelsPerSecond = pps;
        HorizontalOffset = Math.Clamp(anchorSeconds * pps - ax, 0, MaxHorizontalOffset);
    }

    [RelayCommand] private void ZoomIn() => SetZoom(PixelsPerSecond * 1.5, null);
    [RelayCommand] private void ZoomOut() => SetZoom(PixelsPerSecond / 1.5, null);

    /// <summary>Ajusta o zoom para caber a sequência inteira.</summary>
    [RelayCommand]
    private void ZoomToFit()
    {
        var seconds = Math.Max(5, Duration.TotalSeconds);
        PixelsPerSecond = Math.Clamp((ViewportWidth - 40) / seconds, MinPixelsPerSecond, MaxPixelsPerSecond);
        HorizontalOffset = 0;
    }

    [RelayCommand] private void ToggleSnap() => SnapEnabled = !SnapEnabled;

    partial void OnSnapEnabledChanged(bool value) => Invalidate();

    public void Dispose()
    {
        Library.PreviewsChanged -= OnPreviewsChanged;
        Library.PropertyChanged -= OnLibraryPropertyChanged;
        _projects.SessionChanged -= OnSessionChanged;
        if (_session is not null) _session.History.Changed -= OnHistoryChanged;
        _session = null;
    }
}
