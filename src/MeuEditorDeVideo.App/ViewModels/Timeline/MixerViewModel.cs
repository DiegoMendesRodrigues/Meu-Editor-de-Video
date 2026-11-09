using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.ViewModels.Preview;
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.App.ViewModels.Timeline;

/// <summary>Conversões da escala do mixer (fader em dB e medidores de −60 a +6 dB).</summary>
public static class MixerScale
{
    public const double MinDb = -60;
    public const double MaxFaderDb = 12;
    public const double MaxMeterDb = 6;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static double GainToDb(double gain) => gain <= 0 ? MinDb : Math.Clamp(20 * Math.Log10(gain), MinDb, MaxFaderDb);

    public static double DbToGain(double db) => db <= MinDb + 1e-9 ? 0 : Math.Pow(10, Math.Min(db, MaxFaderDb) / 20);

    /// <summary>Posição do medidor (0…1) para uma amplitude.</summary>
    public static double MeterFraction(float amplitude)
    {
        var db = ChannelLevel.ToDb(amplitude);
        return Math.Clamp((db - MinDb) / (MaxMeterDb - MinDb), 0, 1);
    }

    public static string FormatDb(double db) => db <= MinDb + 1e-9 ? "−∞ dB" : db.ToString("+0.0;−0.0;0.0", Pt) + " dB";

    public static string FormatPan(double pan) =>
        Math.Abs(pan) < 0.005 ? "Centro" : pan < 0 ? $"E {Math.Round(-pan * 100)}" : $"D {Math.Round(pan * 100)}";
}

/// <summary>Canal do mixer: uma trilha de áudio (RF130).</summary>
public sealed partial class MixerStripViewModel : ObservableObject
{
    private readonly TimelineViewModel _timeline;
    private bool _updating;

    public MixerStripViewModel(TimelineViewModel timeline, Track track)
    {
        _timeline = timeline;
        Id = track.Id;
        Load(track);
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>Fader em dB (−60 = mudo).</summary>
    [ObservableProperty]
    public partial double VolumeDb { get; set; }

    /// <summary>Panorâmica −100 (esquerda) … 100 (direita).</summary>
    [ObservableProperty]
    public partial double Pan { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial bool IsSolo { get; set; }

    [ObservableProperty]
    public partial double LevelL { get; set; }

    [ObservableProperty]
    public partial double LevelR { get; set; }

    [ObservableProperty]
    public partial string PeakText { get; set; } = "−∞";

    public string VolumeDisplay => MixerScale.FormatDb(VolumeDb);
    public string PanDisplay => MixerScale.FormatPan(Pan / 100);

    public void Load(Track track)
    {
        _updating = true;
        try
        {
            Name = track.Name;
            var db = MixerScale.GainToDb(track.Volume);
            if (Math.Abs(db - VolumeDb) > 0.05) VolumeDb = db;
            if (Math.Abs(track.Pan * 100 - Pan) > 0.5) Pan = track.Pan * 100;
            IsMuted = track.IsMuted;
            IsSolo = track.IsSolo;
        }
        finally
        {
            _updating = false;
        }
    }

    public void ShowLevel(ChannelLevel level)
    {
        LevelL = MixerScale.MeterFraction(level.PeakL);
        LevelR = MixerScale.MeterFraction(level.PeakR);
        var peak = ChannelLevel.ToDb(Math.Max(level.PeakL, level.PeakR));
        PeakText = peak <= MixerScale.MinDb ? "−∞" : peak.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR"));
    }

    partial void OnVolumeDbChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeDisplay));
        if (!_updating) _timeline.Execute(UpdateTrackCommand.SetVolume(_timeline.SequenceId, Id, MixerScale.DbToGain(value)));
    }

    partial void OnPanChanged(double value)
    {
        OnPropertyChanged(nameof(PanDisplay));
        if (!_updating) _timeline.Execute(UpdateTrackCommand.SetPan(_timeline.SequenceId, Id, Math.Clamp(value / 100, -1, 1)));
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_updating) _timeline.Execute(UpdateTrackCommand.SetMuted(_timeline.SequenceId, Id, value));
    }

    partial void OnIsSoloChanged(bool value)
    {
        if (!_updating) _timeline.Execute(UpdateTrackCommand.SetSolo(_timeline.SequenceId, Id, value));
    }

    [RelayCommand]
    private void ResetVolume() => VolumeDb = 0;

    [RelayCommand]
    private void ResetPan() => Pan = 0;
}

/// <summary>Janela do mixer: canais por trilha de áudio com medidor, fader, panorâmica, mudo e solo, e o total.</summary>
public sealed partial class MixerViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;

    public MixerViewModel(TimelineViewModel timeline, PreviewViewModel preview)
    {
        Timeline = timeline;
        Preview = preview;
        Timeline.Invalidated += OnInvalidated;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => UpdateMeters();
        _timer.Start();
        Rebuild();
    }

    public TimelineViewModel Timeline { get; }
    public PreviewViewModel Preview { get; }
    public ObservableCollection<MixerStripViewModel> Strips { get; } = [];

    [ObservableProperty]
    public partial double MasterL { get; set; }

    [ObservableProperty]
    public partial double MasterR { get; set; }

    [ObservableProperty]
    public partial string MasterPeak { get; set; } = "−∞";

    [ObservableProperty]
    public partial bool HasTracks { get; set; }

    private void OnInvalidated(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var tracks = Timeline.Sequence?.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList() ?? [];
        HasTracks = tracks.Count > 0;
        if (Strips.Select(s => s.Id).SequenceEqual(tracks.Select(t => t.Id)))
        {
            for (var i = 0; i < tracks.Count; i++) Strips[i].Load(tracks[i]);
            return;
        }
        Strips.Clear();
        foreach (var t in tracks) Strips.Add(new MixerStripViewModel(Timeline, t));
    }

    private void UpdateMeters()
    {
        var levels = Preview.Levels;
        foreach (var s in Strips)
            s.ShowLevel(levels.Tracks.TryGetValue(s.Id, out var l) ? l : ChannelLevel.Silence);
        MasterL = MixerScale.MeterFraction(levels.Master.PeakL);
        MasterR = MixerScale.MeterFraction(levels.Master.PeakR);
        var peak = ChannelLevel.ToDb(Math.Max(levels.Master.PeakL, levels.Master.PeakR));
        MasterPeak = peak <= MixerScale.MinDb ? "−∞" : peak.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR"));
    }

    public void Dispose()
    {
        _timer.Stop();
        Timeline.Invalidated -= OnInvalidated;
    }
}
