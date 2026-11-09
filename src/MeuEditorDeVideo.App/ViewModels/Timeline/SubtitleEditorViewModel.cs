using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.ViewModels.Timeline;

/// <summary>Linha do editor de legendas: início, fim e texto editáveis (cada alteração vira um comando desfazível).</summary>
public sealed partial class SubtitleRowViewModel : ObservableObject
{
    private readonly SubtitleEditorViewModel _owner;
    private bool _updating;

    public SubtitleRowViewModel(SubtitleEditorViewModel owner, Clip clip, int number)
    {
        _owner = owner;
        Load(clip, number);
    }

    public Guid ClipId { get; private set; }
    public Clip Clip { get; private set; } = null!;

    [ObservableProperty]
    public partial int Number { get; set; }

    [ObservableProperty]
    public partial string StartText { get; set; } = "";

    [ObservableProperty]
    public partial string EndText { get; set; } = "";

    [ObservableProperty]
    public partial string DurationText { get; set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Destaque da legenda que está no cursor de reprodução.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public void Load(Clip clip, int number)
    {
        _updating = true;
        try
        {
            ClipId = clip.Id;
            Clip = clip;
            Number = number;
            StartText = _owner.Format(clip.Start);
            EndText = _owner.Format(clip.End);
            DurationText = clip.Duration.TotalSeconds.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")) + " s";
            if ((Text ?? "").Replace("\r\n", "\n") != clip.Text) Text = clip.Text ?? "";
        }
        finally
        {
            _updating = false;
        }
    }

    partial void OnTextChanged(string value)
    {
        if (!_updating) _owner.SetText(this, value);
    }

    /// <summary>Confirma início/fim digitados (ao sair do campo ou com Enter).</summary>
    public void CommitTimes() => _owner.SetRange(this, StartText, EndText);
}

/// <summary>
/// Editor de legendas (RF143): lista as legendas de uma trilha com tempos e texto editáveis, adiciona/remove,
/// desloca todas as legendas, navega até cada uma e importa/exporta SRT/VTT. Sincronizado com a linha do tempo.
/// </summary>
public sealed partial class SubtitleEditorViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogs;
    private bool _refreshing;

    public SubtitleEditorViewModel(TimelineViewModel timeline, TextLibraryViewModel library, IDialogService dialogs)
    {
        Timeline = timeline;
        Library = library;
        _dialogs = dialogs;
        Timeline.Invalidated += OnInvalidated;
        Timeline.PropertyChanged += OnTimelinePropertyChanged;
        Refresh();
        SelectedTrack ??= Tracks.FirstOrDefault();
    }

    public TimelineViewModel Timeline { get; }
    public TextLibraryViewModel Library { get; }

    public ObservableCollection<Track> Tracks { get; } = [];
    public ObservableCollection<SubtitleRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(ShiftCommand))]
    [NotifyPropertyChangedFor(nameof(HasTrack))]
    public partial Track? SelectedTrack { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(GoToCommand))]
    public partial SubtitleRowViewModel? SelectedRow { get; set; }

    /// <summary>Deslocamento para "Deslocar todas" (segundos; negativo adianta).</summary>
    [ObservableProperty]
    public partial double ShiftSeconds { get; set; } = 0.5;

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    public bool HasTrack => SelectedTrack is not null;

    public string Format(MediaTime t) => Timecode.Format(t, Timeline.FrameRate);

    private void OnInvalidated(object? sender, EventArgs e) => Refresh();

    private void OnTimelinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.Playhead)) MarkCurrent();
    }

    partial void OnSelectedTrackChanged(Track? value)
    {
        if (!_refreshing) LoadRows();
    }

    partial void OnSelectedRowChanged(SubtitleRowViewModel? value)
    {
        if (value is not null && !_refreshing) Timeline.Select(value.ClipId, additive: false);
    }

    private void Refresh()
    {
        if (Timeline.Sequence is not { } seq) return;
        _refreshing = true;
        try
        {
            var tracks = seq.Tracks.Where(t => t.Kind == TrackKind.Subtitle).ToList();
            var selectedId = SelectedTrack?.Id;
            Tracks.Clear();
            foreach (var t in tracks) Tracks.Add(t);
            SelectedTrack = tracks.Find(t => t.Id == selectedId) ?? tracks.FirstOrDefault();
        }
        finally
        {
            _refreshing = false;
        }
        LoadRows();
    }

    private void LoadRows()
    {
        var clips = SelectedTrack?.Clips.Where(c => c.IsText).OrderBy(c => c.Start).ToList() ?? [];
        var selectedId = SelectedRow?.ClipId;
        _refreshing = true;
        try
        {
            // Atualiza no lugar quando a lista é a mesma (não perde o foco de quem está digitando).
            if (Rows.Select(r => r.ClipId).SequenceEqual(clips.Select(c => c.Id)))
            {
                for (var i = 0; i < clips.Count; i++) Rows[i].Load(clips[i], i + 1);
            }
            else
            {
                Rows.Clear();
                for (var i = 0; i < clips.Count; i++) Rows.Add(new SubtitleRowViewModel(this, clips[i], i + 1));
            }
            SelectedRow = Rows.FirstOrDefault(r => r.ClipId == selectedId);
        }
        finally
        {
            _refreshing = false;
        }
        Summary = clips.Count == 0
            ? (SelectedTrack is null ? "Nenhuma trilha de legendas. Importe um arquivo SRT/VTT ou crie uma trilha." : "Trilha sem legendas. Use Adicionar.")
            : $"{clips.Count} legenda(s) · de {Format(clips[0].Start)} a {Format(clips[^1].End)}";
        MarkCurrent();
        RemoveCommand.NotifyCanExecuteChanged();
        GoToCommand.NotifyCanExecuteChanged();
    }

    private void MarkCurrent()
    {
        var t = Timeline.Playhead;
        foreach (var r in Rows) r.IsCurrent = r.Clip.Start <= t && t < r.Clip.End;
    }

    // ---------------------------------------------------------------- edição

    internal void SetText(SubtitleRowViewModel row, string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (normalized.Trim().Length == 0) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, row.ClipId, c => c with { Text = normalized }, "Editar legenda",
            $"{row.ClipId}:subtitle:text"));
    }

    internal void SetRange(SubtitleRowViewModel row, string startText, string endText)
    {
        var fr = Timeline.FrameRate;
        if (!Timecode.TryParse(startText, fr, out var start) || !Timecode.TryParse(endText, fr, out var end))
        {
            _dialogs.ShowInfo("Editor de legendas", "Use o formato HH:MM:SS:QQ (horas, minutos, segundos e quadro).");
            row.Load(row.Clip, row.Number);
            return;
        }
        if (start == row.Clip.Start && end == row.Clip.End) return;
        if (!Timeline.Execute(new SetClipRangeCommand(Timeline.SequenceId, row.ClipId, start, end))) row.Load(row.Clip, row.Number);
    }

    private bool CanAdd() => SelectedTrack is not null;

    /// <summary>Nova legenda no cursor (2 s, encurtada até a próxima legenda se preciso).</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (SelectedTrack is not { } track || Timeline.Project is not { } project) return;
        var fr = project.Settings.FrameRate;
        var start = fr.SnapToFrame(Timeline.Playhead);
        if (track.Clips.Exists(c => c.Start <= start && start < c.End))
        {
            _dialogs.ShowInfo("Editor de legendas", "Já existe uma legenda no cursor. Mova o cursor para um espaço livre.");
            return;
        }
        var next = track.Clips.Where(c => c.Start > start).Select(c => c.Start).DefaultIfEmpty(MediaTime.MaxValue).Min();
        var end = MediaTime.Min(start + MediaTime.FromSeconds(2), next);
        var template = SelectedRow?.Clip ?? track.Clips.LastOrDefault(c => c.IsText);
        var clip = TextPresets.Create(TextKind.Subtitle, project.Settings, start) with { Duration = end - start };
        if (template is not null) clip = clip with { TextFormat = template.TextFormat, Transform = template.Transform };
        if (Timeline.Execute(new AddTextClipCommand(Timeline.SequenceId, clip, TrackKind.Subtitle) { PreferredTrackId = track.Id }))
            SelectedRow = Rows.FirstOrDefault(r => r.ClipId == clip.Id);
    }

    private bool HasRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(HasRow))]
    private void Remove()
    {
        if (SelectedRow is { } row) Timeline.Execute(new RemoveClipsCommand(Timeline.SequenceId, [row.ClipId]));
    }

    /// <summary>Leva o cursor ao início da legenda (para conferir na pré-visualização).</summary>
    [RelayCommand(CanExecute = nameof(HasRow))]
    private void GoTo(SubtitleRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null) return;
        Timeline.Playhead = row.Clip.Start;
        Timeline.Select(row.ClipId, additive: false);
    }

    /// <summary>Desloca todas as legendas da trilha (ajuste de sincronia com o áudio).</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Shift()
    {
        if (SelectedTrack is not { } track || track.Clips.Count == 0) return;
        var delta = MediaTime.FromSecondsApprox(ShiftSeconds);
        var first = track.Clips.Min(c => c.Start);
        if (first + delta < MediaTime.Zero) delta = -first;
        if (delta == MediaTime.Zero) return;
        var moves = track.Clips.Select(c => new ClipMove(c.Id, track.Id, c.Start + delta)).ToList();
        Timeline.Execute(new CompositeCommand(
            $"Deslocar legendas {(delta > MediaTime.Zero ? "+" : "")}{delta.TotalSeconds.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"))} s",
            [new MoveClipsCommand(Timeline.SequenceId, moves)]));
    }

    /// <summary>Cria uma trilha de legendas vazia no topo.</summary>
    [RelayCommand]
    private void NewTrack()
    {
        if (Timeline.Sequence is not { } seq) return;
        var name = TextPresets.NewTrackName(seq, TrackKind.Subtitle);
        if (Timeline.Execute(new AddTrackCommand(Timeline.SequenceId, TrackKind.Subtitle, name, 0)))
            SelectedTrack = Tracks.FirstOrDefault(t => t.Name == name);
    }

    public void Dispose()
    {
        Timeline.Invalidated -= OnInvalidated;
        Timeline.PropertyChanged -= OnTimelinePropertyChanged;
    }
}
