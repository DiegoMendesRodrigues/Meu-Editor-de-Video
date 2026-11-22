using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using MeuEditorDeVideo.AudioEngine.Dsp;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.Presentation.ViewModels.Inspector;

/// <summary>
/// Propriedades de áudio do clipe selecionado (aba Áudio do painel C, F7): volume com keyframes, entrada/saída gradual,
/// preservação de tom, efeitos de áudio, normalização de loudness, remoção de silêncios e sincronização por áudio.
/// </summary>
public sealed partial class AudioInspectorViewModel : ObservableObject, IClipEditor, IDisposable
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private readonly IDialogService _dialogs;
    private readonly IAudioAnalysisService _analysis;
    private readonly IVoiceActivityService _vad;
    private Guid? _clipId;
    private bool _updating;
    private CancellationTokenSource? _work;

    public AudioInspectorViewModel(TimelineViewModel timeline, IDialogService dialogs, IAudioAnalysisService analysis, IVoiceActivityService vad)
    {
        Timeline = timeline;
        _dialogs = dialogs;
        _analysis = analysis;
        _vad = vad;
        Volume = new AnimatedValueViewModel(this, AnimatedProperty.Volume, "Volume do clipe", 0, 400, "%", 100, 1);
        Fades =
        [
            new(this, "fade.in", "Entrada gradual", 0, 10, " s", c => c.FadeIn.TotalSeconds,
                (c, v) => c with { FadeIn = MediaTime.Min(c.Duration, MediaTime.FromSecondsApprox(v)) }, step: 0.1),
            new(this, "fade.out", "Saída gradual", 0, 10, " s", c => c.FadeOut.TotalSeconds,
                (c, v) => c with { FadeOut = MediaTime.Min(c.Duration, MediaTime.FromSecondsApprox(v)) }, step: 0.1),
        ];
        SelectedTarget = Targets[0];
        Timeline.Invalidated += OnTimelineInvalidated;
        Refresh();
    }

    public TimelineViewModel Timeline { get; }

    public AnimatedValueViewModel Volume { get; }
    public IReadOnlyList<ClipValueViewModel> Fades { get; }
    public ObservableCollection<EffectItemViewModel> Effects { get; } = [];

    public IReadOnlyList<LibraryEffectItem> LibraryAudio { get; } =
        EffectCatalog.All.Where(d => d.Category == EffectCategory.Audio).Select(d => new LibraryEffectItem(d.Type, d.Name, d.Description, AudioGlyph(d.Type))).ToList();

    /// <summary>Ícone (Segoe Fluent Icons) que representa cada efeito de áudio no catálogo.</summary>
    private static string AudioGlyph(string type) => type switch
    {
        EffectCatalog.Equalizer => "",
        EffectCatalog.Compressor => "",
        EffectCatalog.Denoise => "",
        EffectCatalog.Echo => "",
        EffectCatalog.Reverb => "",
        EffectCatalog.Telephone => "",
        EffectCatalog.Distortion => "",
        _ => "",
    };

    /// <summary>Alvos de loudness (RF126).</summary>
    public IReadOnlyList<Option<double>> Targets { get; } =
    [
        new(-14, "−14 LUFS — internet (YouTube, redes sociais)"),
        new(-16, "−16 LUFS — podcast"),
        new(-23, "−23 LUFS — televisão (EBU R128)"),
    ];

    [ObservableProperty]
    public partial bool HasClip { get; set; }

    [ObservableProperty]
    public partial string ClipTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool PreservePitch { get; set; } = true;

    [ObservableProperty]
    public partial bool HasSpeedChange { get; set; }

    [ObservableProperty]
    public partial string SpeedText { get; set; } = "1×";

    [ObservableProperty]
    public partial Option<double>? SelectedTarget { get; set; }

    /// <summary>Limiar de silêncio (dBFS).</summary>
    [ObservableProperty]
    public partial double SilenceThreshold { get; set; } = -40;

    /// <summary>Duração mínima de um silêncio para ser removido (s).</summary>
    [ObservableProperty]
    public partial double SilenceMinSeconds { get; set; } = 0.6;

    public string SilenceThresholdDisplay => SilenceThreshold.ToString("0", Pt) + " dB";
    public string SilenceMinDisplay => SilenceMinSeconds.ToString("0.0", Pt) + " s";

    /// <summary>Atenuação automática: redução do volume durante a fala (dB, negativo).</summary>
    [ObservableProperty]
    public partial double DuckGainDb { get; set; } = -12;

    /// <summary>Atenuação automática: duração das rampas de descida e subida (s).</summary>
    [ObservableProperty]
    public partial double DuckFadeSeconds { get; set; } = 0.3;

    /// <summary>Detecção de batidas: sensibilidade 0–100 (maior = mais batidas).</summary>
    [ObservableProperty]
    public partial double BeatSensitivity { get; set; } = 50;

    public string BeatSensitivityDisplay => BeatSensitivity.ToString("0", Pt) + " %";

    partial void OnBeatSensitivityChanged(double value) => OnPropertyChanged(nameof(BeatSensitivityDisplay));

    public string DuckGainDisplay => DuckGainDb.ToString("0", Pt) + " dB";
    public string DuckFadeDisplay => DuckFadeSeconds.ToString("0.0", Pt) + " s";

    partial void OnDuckGainDbChanged(double value) => OnPropertyChanged(nameof(DuckGainDisplay));
    partial void OnDuckFadeSecondsChanged(double value) => OnPropertyChanged(nameof(DuckFadeDisplay));

    partial void OnSilenceThresholdChanged(double value) => OnPropertyChanged(nameof(SilenceThresholdDisplay));
    partial void OnSilenceMinSecondsChanged(double value) => OnPropertyChanged(nameof(SilenceMinDisplay));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NormalizeCommand), nameof(RemoveSilenceCommand), nameof(SyncCommand), nameof(DuckCommand),
        nameof(DetectBeatsCommand), nameof(CutAtBeatsCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Resultado da última análise (ex.: "Medido: −20,1 LUFS · ganho aplicado +6,1 dB").</summary>
    [ObservableProperty]
    public partial string AnalysisStatus { get; set; } = "";

    // ---------------------------------------------------------------- estado

    private void OnTimelineInvalidated(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// Clipe de áudio selecionado: o primeiro numa trilha de áudio; se só um vídeo estiver selecionado,
    /// o clipe de áudio vinculado a ele.
    /// </summary>
    private (Track Track, Clip Clip)? Current()
    {
        if (Timeline.Sequence is not { } seq || Timeline.Selection.Count == 0) return null;
        var audioTracks = seq.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
        foreach (var t in audioTracks)
            foreach (var c in t.Clips)
                if (Timeline.Selection.Contains(c.Id) && c.MediaAssetId is not null) return (t, c);

        var links = seq.Tracks.SelectMany(t => t.Clips).Where(c => Timeline.Selection.Contains(c.Id) && c.LinkId is not null)
            .Select(c => c.LinkId).ToHashSet();
        foreach (var t in audioTracks)
            foreach (var c in t.Clips)
                if (c.LinkId is not null && links.Contains(c.LinkId) && c.MediaAssetId is not null) return (t, c);
        return null;
    }

    /// <summary>Clipes de áudio selecionados (para sincronização), na ordem das trilhas.</summary>
    private List<(Track Track, Clip Clip)> SelectedAudioClips()
    {
        if (Timeline.Sequence is not { } seq) return [];
        return seq.Tracks.Where(t => t.Kind == TrackKind.Audio)
            .SelectMany(t => t.Clips.Where(c => Timeline.Selection.Contains(c.Id) && c.MediaAssetId is not null).Select(c => (t, c)))
            .ToList();
    }

    /// <summary>Clipe de áudio selecionado e a forma de onda da mídia dele (nula enquanto carrega), para a visão do áudio.</summary>
    public (Clip Clip, WaveformData? Peaks)? WaveformClip() =>
        Current() is { } cur ? (cur.Clip, Timeline.Library.Find(cur.Clip.MediaAssetId!.Value)?.Peaks) : null;

    public MediaTime Offset(Clip c)
    {
        var offset = Timeline.FrameRate.SnapToFrame(Timeline.Playhead) - c.Start;
        return MediaTime.Max(MediaTime.Zero, MediaTime.Min(c.Duration, offset));
    }

    private void Refresh()
    {
        var current = Current();
        HasClip = current is not null;
        if (current is not { } cur)
        {
            _clipId = null;
            Effects.Clear();
            return;
        }
        var clip = cur.Clip;
        _updating = true;
        try
        {
            ClipTitle = Timeline.Project?.FindMedia(clip.MediaAssetId!.Value)?.Name ?? "Clipe";
            Volume.Refresh(clip, Offset(clip));
            foreach (var f in Fades) f.Refresh(clip);
            PreservePitch = clip.PreservePitch;
            HasSpeedChange = clip.Speed != Rational.One;
            SpeedText = clip.Speed.Value.ToString("0.##", Pt) + "×";
        }
        finally
        {
            _updating = false;
        }
        if (_clipId != clip.Id) AnalysisStatus = "";
        SyncEffects(clip);
    }

    private void SyncEffects(Clip clip)
    {
        var effects = clip.Effects.Where(e => EffectCatalog.IsAudio(e.Type)).ToList();
        var sameClip = _clipId == clip.Id;
        _clipId = clip.Id;
        if (sameClip && Effects.Select(t => t.Id).SequenceEqual(effects.Select(e => e.Id)))
        {
            for (var i = 0; i < effects.Count; i++)
                if (!ReferenceEquals(Effects[i].Effect, effects[i])) Effects[i].Load(effects[i]);
            return;
        }
        var collapsed = Effects.Where(t => !t.IsExpanded).Select(t => t.Id).ToHashSet();
        Effects.Clear();
        foreach (var e in effects) Effects.Add(new EffectItemViewModel(this, e) { IsExpanded = !collapsed.Contains(e.Id) });
    }

    // ---------------------------------------------------------------- edição

    public void UpdateClip(Func<Clip, Clip> change, string description, string? mergeKey = null)
    {
        if (Current() is not { } cur) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, change, description,
            mergeKey is null ? null : $"{cur.Clip.Id}:audio:{mergeKey}"));
    }

    public void UpdateEffect(Guid effectId, Func<EffectInstance, EffectInstance> change, string description, string? mergeKey) =>
        UpdateClip(c =>
        {
            var index = c.Effects.FindIndex(e => e.Id == effectId);
            return index < 0 ? c : c with { Effects = c.Effects.SetItem(index, change(c.Effects[index])) };
        }, description, mergeKey);

    public void RemoveEffect(Guid effectId)
    {
        var name = Effects.FirstOrDefault(e => e.Id == effectId)?.Name ?? "efeito";
        UpdateClip(c => c with { Effects = c.Effects.RemoveAll(e => e.Id == effectId) }, $"Remover {name}");
    }

    /// <summary>Move entre os efeitos de áudio (os visuais do mesmo clipe ficam onde estão).</summary>
    public void MoveEffect(Guid effectId, int direction) =>
        UpdateClip(c =>
        {
            var audio = c.Effects.Select((e, i) => (e, i)).Where(x => EffectCatalog.IsAudio(x.e.Type)).ToList();
            var pos = audio.FindIndex(x => x.e.Id == effectId);
            var target = pos + direction;
            if (pos < 0 || target < 0 || target >= audio.Count) return c;
            var (a, ia) = audio[pos];
            var (b, ib) = audio[target];
            return c with { Effects = c.Effects.SetItem(ia, b).SetItem(ib, a) };
        }, "Reordenar efeitos de áudio");

    public void ChooseLut(EffectItemViewModel item)
    {
    }

    [RelayCommand]
    public void AddEffect(string type)
    {
        if (!EnsureClip() || EffectCatalog.Find(type) is not { Category: EffectCategory.Audio } definition) return;
        var effect = EffectCatalog.Create(type);
        UpdateClip(c => c with { Effects = c.Effects.Add(effect) }, $"Adicionar {definition.Name}");
    }

    partial void OnPreservePitchChanged(bool value)
    {
        if (!_updating) UpdateClip(c => c with { PreservePitch = value }, value ? "Manter o tom ao mudar a velocidade" : "Tom acompanha a velocidade");
    }

    private bool EnsureClip()
    {
        if (HasClip) return true;
        _dialogs.ShowInfo("Selecione um clipe", "Selecione um clipe de áudio (ou um vídeo com áudio vinculado) na linha do tempo.");
        return false;
    }

    private bool CanAnalyze() => !IsBusy;

    /// <summary>Detecta as batidas da música selecionada e cria marcadores nelas.</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task DetectBeatsAsync()
    {
        if (!EnsureClip() || Current() is not { } cur || Timeline.Project is not { } project) return;
        var sensitivity = BeatSensitivity / 100;
        await RunAsync("Detectando as batidas…", async ct =>
        {
            var beats = await _analysis.DetectBeatsAsync(project, cur.Clip, Timeline.MediaPaths, sensitivity, ct);
            if (beats.Count == 0)
            {
                AnalysisStatus = "Nenhuma batida encontrada. Aumente a sensibilidade.";
                return;
            }
            AnalysisStatus = Timeline.Execute(new AddMarkersCommand(Timeline.SequenceId, beats, "Batida"))
                ? $"{beats.Count} batidas marcadas na linha do tempo."
                : "Não foi possível criar os marcadores.";
        });
    }

    /// <summary>
    /// Cortar nas batidas: detecta as batidas da música selecionada e divide os clipes de vídeo e imagem das trilhas
    /// visuais (desbloqueadas) que tocam junto com ela.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task CutAtBeatsAsync()
    {
        if (!EnsureClip() || Current() is not { } cur || Timeline.Project is not { } project || Timeline.Sequence is not { } seq) return;
        var music = cur.Clip;
        var targets = seq.Tracks.Where(t => t.IsVisual && !t.IsLocked).SelectMany(t => t.Clips)
            .Where(c => c.MediaAssetId is not null && c.Id != music.Id && (music.LinkId is null || c.LinkId != music.LinkId) && c.Start < music.End && c.End > music.Start)
            .Select(c => c.Id).ToList();
        if (targets.Count == 0)
        {
            _dialogs.ShowInfo("Cortar nas batidas", "Não há clipes de vídeo ou imagem tocando junto com esta música.");
            return;
        }
        var sensitivity = BeatSensitivity / 100;
        await RunAsync("Detectando as batidas…", async ct =>
        {
            var beats = await _analysis.DetectBeatsAsync(project, music, Timeline.MediaPaths, sensitivity, ct);
            AnalysisStatus = beats.Count > 0 && Timeline.Execute(new SplitAtTimesCommand(Timeline.SequenceId, beats, targets))
                ? $"Clipes cortados nas batidas ({beats.Count} batidas)."
                : "Nenhuma batida cai dentro dos clipes de vídeo.";
        });
    }

    /// <summary>Normaliza o loudness do clipe para o alvo escolhido, limitando o pico em −1 dBFS (RF126).</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task NormalizeAsync()
    {
        if (!EnsureClip() || Current() is not { } cur || Timeline.Project is not { } project || SelectedTarget is not { } target) return;
        await RunAsync("Medindo o loudness…", async ct =>
        {
            var m = await _analysis.MeasureLoudnessAsync(project, cur.Clip, Timeline.MediaPaths, ct);
            if (!m.HasSignal)
            {
                AnalysisStatus = "O clipe está em silêncio; nada a normalizar.";
                return;
            }
            var gainDb = LoudnessMeter.NormalizationGainDb(m.IntegratedLufs, m.PeakDbfs, target.Value);
            var factor = Math.Pow(10, gainDb / 20);
            UpdateClip(c => ClipAnimation.ScaleVolume(c, factor), $"Normalizar áudio ({target.Value.ToString("0", Pt)} LUFS)");
            var limited = gainDb < target.Value - m.IntegratedLufs - 0.05;
            AnalysisStatus = $"Medido: {m.IntegratedLufs.ToString("0.0", Pt)} LUFS · ganho {gainDb.ToString("+0.0;−0.0;0", Pt)} dB" +
                             (limited ? " (limitado pelo pico)" : "");
        });
    }

    /// <summary>Detecta e remove os silêncios do clipe e dos vinculados, fechando os espaços (RF128).</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task RemoveSilenceAsync()
    {
        if (!EnsureClip() || Current() is not { } cur || Timeline.Project is not { } project) return;
        await RunAsync("Procurando silêncios…", async ct =>
        {
            var ranges = await _analysis.DetectSilenceAsync(project, cur.Clip, Timeline.MediaPaths, SilenceThreshold,
                MediaTime.FromSecondsApprox(SilenceMinSeconds), MediaTime.FromSecondsApprox(0.15), ct);
            if (ranges.Count == 0)
            {
                AnalysisStatus = "Nenhum silêncio encontrado com esses ajustes.";
                return;
            }
            var total = ranges.Sum(r => r.Duration.TotalSeconds);
            var command = new RemoveTimeRangesCommand(Timeline.SequenceId, cur.Clip.Id, ranges,
                $"Remover {ranges.Count} {(ranges.Count == 1 ? "silêncio" : "silêncios")}");
            AnalysisStatus = Timeline.Execute(command)
                ? $"{ranges.Count} {Localization.Translator.T(ranges.Count == 1 ? "trecho removido" : "trechos removidos")} ({total.ToString("0.0", Pt)} s)."
                : "Não foi possível remover os silêncios.";
        });
    }

    /// <summary>
    /// Sincroniza dois clipes de áudio selecionados pelo som (RF129): o da trilha mais acima é a referência;
    /// o outro (com os clipes vinculados a ele) é movido.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task SyncAsync()
    {
        var selected = SelectedAudioClips();
        if (selected.Count != 2 || Timeline.Project is not { } project || Timeline.Sequence is not { } seq)
        {
            _dialogs.ShowInfo("Sincronizar pelo áudio",
                "Selecione dois clipes de áudio do mesmo acontecimento (Ctrl+clique). O da trilha mais acima fica parado; o outro é movido.");
            return;
        }
        var (reference, other) = (selected[0].Clip, selected[1].Clip);
        await RunAsync("Comparando os áudios…", async ct =>
        {
            var result = await _analysis.FindSyncAsync(project, reference, other, Timeline.MediaPaths, ct);
            if (result is null)
            {
                AnalysisStatus = "Não foi possível encontrar um trecho em comum entre os dois áudios.";
                return;
            }
            var delta = result.NewStart - other.Start;
            if (delta == MediaTime.Zero)
            {
                AnalysisStatus = "Os clipes já estão sincronizados.";
                return;
            }
            var moves = seq.Tracks.SelectMany(t => t.Clips
                    .Where(c => c.Id == other.Id || (other.LinkId is not null && c.LinkId == other.LinkId))
                    .Select(c => new ClipMove(c.Id, t.Id, c.Start + delta)))
                .ToList();
            if (moves.Any(m => m.Start < MediaTime.Zero))
            {
                AnalysisStatus = "Para sincronizar, o clipe teria de começar antes do início da linha do tempo. Afaste a referência e tente de novo.";
                return;
            }
            AnalysisStatus = Timeline.Execute(new MoveClipsCommand(Timeline.SequenceId, moves))
                ? $"Sincronizado: deslocado {delta.TotalSeconds.ToString("+0.000;−0.000", Pt)} s (confiança {result.Confidence.ToString("P0", Pt)})."
                : "Não foi possível mover o clipe.";
        });
    }

    /// <summary>
    /// Atenuação automática: procura fala nos clipes com áudio das outras trilhas (não mudas) que tocam junto com o
    /// clipe selecionado e abaixa o volume dele nesses trechos, com keyframes de volume.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task DuckAsync()
    {
        if (!EnsureClip() || Current() is not { } cur || Timeline.Project is not { } project || Timeline.Sequence is not { } seq) return;
        var target = cur.Clip;
        // Fala: clipes das trilhas de áudio; um vídeo só conta quando não tem um clipe de áudio vinculado.
        var linkedAudio = seq.Tracks.Where(t => t.Kind == TrackKind.Audio).SelectMany(t => t.Clips)
            .Where(c => c.LinkId is not null).Select(c => c.LinkId).ToHashSet();
        var voices = new List<Clip>();
        foreach (var track in seq.Tracks)
        {
            if (track.Id == cur.Track.Id || track.IsMuted) continue;
            if (track.Kind is not (TrackKind.Audio or TrackKind.Video)) continue;
            foreach (var c in track.Clips)
            {
                if (c.Id == target.Id || c.Start >= target.End || c.End <= target.Start) continue;
                if (target.LinkId is not null && c.LinkId == target.LinkId) continue;
                if (track.Kind == TrackKind.Video && c.LinkId is not null && linkedAudio.Contains(c.LinkId)) continue;
                if (c.MediaAssetId is { } id && project.FindMedia(id) is { HasAudio: true }) voices.Add(c);
            }
        }
        if (voices.Count == 0)
        {
            _dialogs.ShowInfo("Atenuação automática",
                "Não há outro clipe com áudio tocando junto com este. Coloque a narração ou a fala em outra trilha e tente de novo.");
            return;
        }

        await RunAsync("Procurando fala nas outras trilhas…", async ct =>
        {
            var speech = new List<TimeRange>();
            foreach (var v in voices)
            {
                if (_vad.IsAvailable)
                    speech.AddRange(await _vad.DetectSpeechAsync(project, v, Timeline.MediaPaths, cancellationToken: ct));
                else
                {
                    var silences = await _analysis.DetectSilenceAsync(project, v, Timeline.MediaPaths, SilenceThreshold,
                        MediaTime.FromSecondsApprox(0.3), MediaTime.Zero, ct);
                    speech.AddRange(Ducking.Complement(silences, v.Start, v.End));
                }
            }
            var fade = MediaTime.FromSecondsApprox(DuckFadeSeconds);
            var inClip = Ducking.Merge(Ducking.ToClipOffsets(target, speech), Ducking.DefaultMergeGap + fade + fade);
            if (inClip.Count == 0)
            {
                AnalysisStatus = "Nenhuma fala encontrada enquanto este clipe toca.";
                return;
            }
            var gain = DuckGainDb;
            Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, target.Id, c => Ducking.Apply(c, speech, gain, fade), "Atenuação automática"));
            AnalysisStatus = $"Volume reduzido em {inClip.Count} {(inClip.Count == 1 ? "trecho" : "trechos")} com fala" +
                             (_vad.IsAvailable ? "." : " (detecção pelo nível do som; instale o modelo de detecção de fala para mais precisão).");
        });
    }

    /// <summary>Remove a atenuação (a animação de volume) do clipe selecionado.</summary>
    [RelayCommand]
    private void RemoveDuck()
    {
        if (!EnsureClip()) return;
        UpdateClip(Ducking.Remove, "Remover atenuação");
        AnalysisStatus = "";
    }

    private async Task RunAsync(string status, Func<CancellationToken, Task> work)
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();
        IsBusy = true;
        AnalysisStatus = status;
        try
        {
            await work(_work.Token);
        }
        catch (OperationCanceledException)
        {
            AnalysisStatus = "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            AnalysisStatus = "";
            _dialogs.ShowInfo("Análise de áudio", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _work?.Cancel();
        Timeline.Invalidated -= OnTimelineInvalidated;
    }
}
