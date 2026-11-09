using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.ViewModels.Inspector;

/// <summary>
/// Aba IA do painel de propriedades (F10): recursos de IA local sobre o clipe selecionado. Cada recurso só fica ativo
/// quando há um modelo instalado para ele; senão aparece como "indisponível" com a orientação para instalar (§8.3).
/// Todo o processamento é local (RF155, RF169).
/// </summary>
public sealed partial class AiInspectorViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private const string Install = "Indisponível — instale o modelo em Ferramentas › Modelos de IA.";

    private readonly IModelRegistry _models;
    private readonly ISegmentationService _segmentation;
    private readonly ISpeechRecognitionService _speech;
    private readonly IVoiceActivityService _vad;
    private readonly IVoiceEnhancementService _voice;
    private readonly ISceneDetectionService _scenes;
    private readonly IImageEnhancementService _enhancer;
    private readonly IMediaImportService _import;
    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly ILogger<AiInspectorViewModel> _logger;
    private CancellationTokenSource? _work;
    private bool _updating;

    public AiInspectorViewModel(TimelineViewModel timeline, IModelRegistry models, ISegmentationService segmentation,
        ISpeechRecognitionService speech, IVoiceActivityService vad, IVoiceEnhancementService voice, ISceneDetectionService scenes,
        IImageEnhancementService enhancer, IMediaImportService import, IProjectService projects, IDialogService dialogs,
        ILogger<AiInspectorViewModel> logger)
    {
        Timeline = timeline;
        _models = models;
        _segmentation = segmentation;
        _speech = speech;
        _vad = vad;
        _voice = voice;
        _scenes = scenes;
        _enhancer = enhancer;
        _import = import;
        _projects = projects;
        _dialogs = dialogs;
        _logger = logger;
        SelectedLanguage = Languages[0];
        SelectedBackgroundMode = BackgroundModes[0];
        _models.Changed += OnModelsChanged;
        Timeline.Invalidated += OnTimelineInvalidated;
        Refresh();
    }

    public TimelineViewModel Timeline { get; }

    public IReadOnlyList<Option<string>> Languages { get; } =
    [
        new("pt", "Português"),
        new("en", "Inglês"),
        new("es", "Espanhol"),
        new("auto", "Detectar automaticamente"),
    ];

    public IReadOnlyList<Option<int>> BackgroundModes { get; } =
    [
        new(0, "Transparente"),
        new(1, "Cor sólida"),
        new(2, "Desfoque"),
        new(3, "Imagem ou vídeo da biblioteca"),
    ];

    /// <summary>Imagens e vídeos da biblioteca para o fundo substituto (RF105, RF106).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Option<Guid>> BackgroundMedia { get; set; } = [];

    [ObservableProperty]
    public partial Option<Guid>? SelectedBackgroundMedia { get; set; }

    [ObservableProperty]
    public partial Option<string> SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedBackgroundMode { get; set; }

    [ObservableProperty]
    public partial bool HasClip { get; set; }

    [ObservableProperty]
    public partial string ClipTitle { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBackgroundCommand), nameof(PrecomputeMattesCommand), nameof(TrackPersonCommand))]
    public partial bool IsVisualMedia { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DetectScenesCommand))]
    public partial bool IsVideo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnhanceImageCommand))]
    public partial bool IsImage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateSubtitlesCommand), nameof(IsolateVoiceCommand), nameof(DenoiseCommand), nameof(RemoveSilenceCommand))]
    public partial bool HasAudio { get; set; }

    [ObservableProperty]
    public partial bool HasBackgroundEffect { get; set; }

    // Disponibilidade de cada recurso (modelo instalado).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBackgroundCommand), nameof(PrecomputeMattesCommand), nameof(TrackPersonCommand))]
    public partial bool SegmentationAvailable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateSubtitlesCommand))]
    public partial bool SpeechAvailable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IsolateVoiceCommand), nameof(DenoiseCommand))]
    public partial bool VoiceAvailable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSilenceCommand))]
    public partial bool VadAvailable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnhanceImageCommand))]
    public partial bool EnhancerAvailable { get; set; }

    [ObservableProperty]
    public partial string SegmentationStatus { get; set; } = "";

    [ObservableProperty]
    public partial string SpeechStatus { get; set; } = "";

    [ObservableProperty]
    public partial string VoiceStatus { get; set; } = "";

    [ObservableProperty]
    public partial string VadStatus { get; set; } = "";

    [ObservableProperty]
    public partial string SceneStatus { get; set; } = "";

    [ObservableProperty]
    public partial string EnhancerStatus { get; set; } = "";

    /// <summary>Quanto do som original é removido na redução de ruído (%).</summary>
    [ObservableProperty]
    public partial double DenoiseStrength { get; set; } = 85;

    public string DenoiseStrengthDisplay => DenoiseStrength.ToString("0", Pt) + "%";

    partial void OnDenoiseStrengthChanged(double value) => OnPropertyChanged(nameof(DenoiseStrengthDisplay));

    [ObservableProperty]
    public partial double SilenceMinSeconds { get; set; } = 0.6;

    public string SilenceMinDisplay => SilenceMinSeconds.ToString("0.0", Pt) + " s";

    partial void OnSilenceMinSecondsChanged(double value) => OnPropertyChanged(nameof(SilenceMinDisplay));

    [ObservableProperty]
    public partial double SceneSensitivity { get; set; } = 50;

    public string SceneSensitivityDisplay => SceneSensitivity.ToString("0", Pt) + "%";

    partial void OnSceneSensitivityChanged(double value) => OnPropertyChanged(nameof(SceneSensitivityDisplay));

    [ObservableProperty]
    public partial bool SplitAtScenes { get; set; }

    [ObservableProperty]
    public partial bool MuteOriginal { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBackgroundCommand), nameof(PrecomputeMattesCommand), nameof(TrackPersonCommand),
        nameof(GenerateSubtitlesCommand), nameof(IsolateVoiceCommand), nameof(DenoiseCommand), nameof(RemoveSilenceCommand),
        nameof(DetectScenesCommand), nameof(EnhanceImageCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string BusyText { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>Resultado da última operação.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    // ---------------------------------------------------------------- estado

    private void OnModelsChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);

    private void OnTimelineInvalidated(object? sender, EventArgs e) => Refresh();

    private static string StatusText(bool available, string ready) => available ? ready : Install;

    public void Refresh()
    {
        _updating = true;
        try
        {
            SegmentationAvailable = _segmentation.IsAvailable;
            SpeechAvailable = _speech.IsAvailable;
            VoiceAvailable = _voice.IsAvailable;
            VadAvailable = _vad.IsAvailable;
            EnhancerAvailable = _enhancer.IsAvailable;
            SegmentationStatus = StatusText(SegmentationAvailable, Ready(ModelCapability.PersonSegmentation));
            SpeechStatus = StatusText(SpeechAvailable, Ready(ModelCapability.SpeechRecognition));
            VoiceStatus = StatusText(VoiceAvailable, Ready(ModelCapability.SpeechEnhancement));
            VadStatus = StatusText(VadAvailable, Ready(ModelCapability.VoiceActivity));
            EnhancerStatus = StatusText(EnhancerAvailable, Ready(ModelCapability.ImageEnhancement));
            SceneStatus = _scenes.ModelAvailable
                ? Ready(ModelCapability.SceneDetection)
                : "Sem modelo: usa comparação de cores entre quadros (menos precisa). Instale o modelo de cenas para melhor resultado.";

            var visual = VisualClip();
            var audio = AudioClip();
            var project = Timeline.Project;
            var clip = visual?.Clip ?? audio?.Clip;
            HasClip = clip is not null;
            var asset = clip?.MediaAssetId is { } id ? project?.FindMedia(id) : null;
            ClipTitle = asset?.Name ?? "";
            var visualAsset = visual?.Clip.MediaAssetId is { } vid ? project?.FindMedia(vid) : null;
            IsVisualMedia = visualAsset?.Kind is MediaKind.Video or MediaKind.Image;
            IsVideo = visualAsset?.Kind == MediaKind.Video;
            IsImage = visualAsset?.Kind == MediaKind.Image;
            HasAudio = audio is not null;

            var effect = visual is { } v ? v.Clip.Effects.FirstOrDefault(e => e.Type == EffectCatalog.AiBackground) : null;
            HasBackgroundEffect = effect is not null;
            if (effect is not null)
            {
                var mode = (int)Math.Round(EffectCatalog.Value(effect, "mode"));
                SelectedBackgroundMode = BackgroundModes.FirstOrDefault(m => m.Value == mode) ?? BackgroundModes[0];
            }

            var media = (project?.Media ?? [])
                .Where(m => m.Kind is MediaKind.Image or MediaKind.Video && m.Id != visualAsset?.Id)
                .Select(m => new Option<Guid>(m.Id, m.Name))
                .ToList();
            if (!media.Select(m => m.Value).SequenceEqual(BackgroundMedia.Select(m => m.Value))) BackgroundMedia = media;
            var resource = effect is not null && Guid.TryParse(effect.Resource, out var r) ? r : (Guid?)null;
            SelectedBackgroundMedia = BackgroundMedia.FirstOrDefault(m => m.Value == resource);
        }
        finally
        {
            _updating = false;
        }
    }

    private string Ready(ModelCapability capability) =>
        _models.Find(capability) is { } m ? $"Pronto · {m.Manifest.Name} {m.Manifest.Version}" : Install;

    /// <summary>Clipe de vídeo/imagem selecionado.</summary>
    private (Track Track, Clip Clip)? VisualClip()
    {
        if (Timeline.Sequence is not { } seq || Timeline.Selection.Count == 0) return null;
        foreach (var t in seq.Tracks.Where(t => t.IsVisual))
            foreach (var c in t.Clips)
                if (Timeline.Selection.Contains(c.Id) && c.MediaAssetId is not null) return (t, c);
        return null;
    }

    /// <summary>Clipe de áudio selecionado, ou o áudio vinculado ao vídeo selecionado.</summary>
    private (Track Track, Clip Clip)? AudioClip()
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

    /// <summary>
    /// Todos os clipes de áudio selecionados (ou vinculados aos vídeos selecionados), em ordem de tempo — ex.: os trechos
    /// que sobraram depois de remover silêncios.
    /// </summary>
    private List<Clip> AudioClips()
    {
        if (Timeline.Sequence is not { } seq || Timeline.Selection.Count == 0) return [];
        var links = seq.Tracks.SelectMany(t => t.Clips).Where(c => Timeline.Selection.Contains(c.Id) && c.LinkId is not null)
            .Select(c => c.LinkId).ToHashSet();
        return seq.Tracks.Where(t => t.Kind == TrackKind.Audio)
            .SelectMany(t => t.Clips)
            .Where(c => c.MediaAssetId is not null && (Timeline.Selection.Contains(c.Id) || (c.LinkId is not null && links.Contains(c.LinkId))))
            .OrderBy(c => c.Start.Ticks)
            .ToList();
    }

    /// <summary>Progresso de uma etapa <paramref name="index"/> de <paramref name="count"/>.</summary>
    private static IProgress<double> Part(IProgress<double> total, int index, int count) =>
        new Progress<double>(p => total.Report((index + Math.Clamp(p, 0, 1)) / count));

    // ---------------------------------------------------------------- fundo (RF102–RF109, RF156, RF162, RF163)

    private bool CanSegment() => !IsBusy && IsVisualMedia && SegmentationAvailable;

    /// <summary>Adiciona (ou reconfigura) o efeito "Remover fundo (IA)" no clipe.</summary>
    [RelayCommand(CanExecute = nameof(CanSegment))]
    private void RemoveBackground()
    {
        if (VisualClip() is not { } cur) return;
        var mode = SelectedBackgroundMode.Value;
        if (cur.Clip.Effects.FirstOrDefault(e => e.Type == EffectCatalog.AiBackground) is { } existing)
        {
            UpdateEffect(cur.Clip, existing.Id, e => EffectCatalog.WithValue(e, "mode", mode), "Fundo (IA)");
            return;
        }
        var effect = EffectCatalog.WithValue(EffectCatalog.Create(EffectCatalog.AiBackground, SelectedBackgroundMedia?.Value.ToString()), "mode", mode);
        // Primeiro da pilha: os demais efeitos (cor, refinamento) atuam sobre o recorte.
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, c => c with { Effects = c.Effects.Insert(0, effect) },
            "Remover fundo (IA)"));
        Status = "Fundo removido. Ajuste bordas e cor no efeito \"Remover fundo (IA)\" da aba Vídeo.";
    }

    partial void OnSelectedBackgroundModeChanged(Option<int> value)
    {
        if (_updating || value is null || VisualClip() is not { } cur) return;
        if (cur.Clip.Effects.FirstOrDefault(e => e.Type == EffectCatalog.AiBackground) is { } existing
            && (int)Math.Round(EffectCatalog.Value(existing, "mode")) != value.Value)
            UpdateEffect(cur.Clip, existing.Id, e => EffectCatalog.WithValue(e, "mode", value.Value), "Fundo (IA)");
    }

    partial void OnSelectedBackgroundMediaChanged(Option<Guid>? value)
    {
        if (_updating || value is null || VisualClip() is not { } cur) return;
        if (cur.Clip.Effects.FirstOrDefault(e => e.Type == EffectCatalog.AiBackground) is { } existing && existing.Resource != value.Value.ToString())
            UpdateEffect(cur.Clip, existing.Id, e => EffectCatalog.WithValue(e with { Resource = value.Value.ToString() }, "mode", 3),
                "Trocar fundo (IA)");
    }

    private void UpdateEffect(Clip clip, Guid effectId, Func<EffectInstance, EffectInstance> change, string description) =>
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, clip.Id, c =>
        {
            var i = c.Effects.FindIndex(e => e.Id == effectId);
            return i < 0 ? c : c with { Effects = c.Effects.SetItem(i, change(c.Effects[i])) };
        }, description));

    /// <summary>Processamento em lote em segundo plano: calcula as máscaras de todos os quadros e guarda no cache.</summary>
    [RelayCommand(CanExecute = nameof(CanSegment))]
    private async Task PrecomputeMattesAsync()
    {
        if (VisualClip() is not { } cur || Timeline.Project is not { } project) return;
        await RunAsync("Calculando as máscaras da pessoa…", async (progress, ct) =>
        {
            await _segmentation.PrecomputeAsync(project, cur.Clip, Timeline.MediaPaths, progress, ct);
            Status = "Máscaras prontas: a reprodução e a exportação usam o cache.";
            Timeline.Invalidate();
        });
    }

    /// <summary>Rastreamento inteligente (RF163): cria um rastreamento a partir da posição da pessoa em cada quadro.</summary>
    [RelayCommand(CanExecute = nameof(CanSegment))]
    private async Task TrackPersonAsync()
    {
        if (VisualClip() is not { } cur || Timeline.Project is not { } project) return;
        await RunAsync("Localizando a pessoa em cada quadro…", async (progress, ct) =>
        {
            var samples = await _segmentation.TrackPersonAsync(project, cur.Clip, Timeline.MediaPaths, progress, ct);
            if (samples.Count == 0)
            {
                Status = "Nenhuma pessoa encontrada no clipe.";
                return;
            }
            var id = Guid.NewGuid();
            Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, c =>
            {
                var n = c.Trackers.Count(t => t.Name.StartsWith("Pessoa", StringComparison.Ordinal)) + 1;
                var added = CompositionOps.AddTracker(c, samples[0], id);
                added = CompositionOps.UpdateTracker(added, id, t => t with { Name = $"Pessoa (IA) {n}" });
                return CompositionOps.AddSamples(added, id, samples);
            }, "Rastrear pessoa (IA)"));
            Status = $"Pessoa rastreada em {samples.Count} quadros. Vincule elementos na aba Composição.";
        });
    }

    // ---------------------------------------------------------------- fala (RF142, RF157, RF158)

    private bool CanTranscribe() => !IsBusy && HasAudio && SpeechAvailable;

    [RelayCommand(CanExecute = nameof(CanTranscribe))]
    private async Task GenerateSubtitlesAsync()
    {
        var sources = AudioClips();
        if (sources.Count == 0 || Timeline.Project is not { } project) return;
        var language = SelectedLanguage.Value;
        await RunAsync("Transcrevendo a fala…", async (progress, ct) =>
        {
            var segments = new List<TranscriptSegment>();
            for (var i = 0; i < sources.Count; i++)
                segments.AddRange(await _speech.TranscribeAsync(project, sources[i], Timeline.MediaPaths, language, Part(progress, i, sources.Count), ct));
            if (segments.Count == 0)
            {
                Status = "Nenhuma fala reconhecida no clipe.";
                return;
            }
            var clips = SubtitleOps.ToClips(segments.Select(s => new SubtitleEntry(s.Start, s.End, s.Text)), project.Settings);
            var name = "Legendas (IA) · " + ClipTitle;
            Timeline.Execute(new ImportSubtitlesCommand(Timeline.SequenceId, clips)
            {
                NewTrackName = name,
                Description = $"Gerar legendas ({clips.Count})",
            });
            Status = $"{clips.Count} legendas geradas. Revise o texto em Ferramentas › Editor de legendas.";
        });
    }

    // ---------------------------------------------------------------- voz (RF127, RF159, RF160)

    private bool CanEnhanceVoice() => !IsBusy && HasAudio && VoiceAvailable;

    [RelayCommand(CanExecute = nameof(CanEnhanceVoice))]
    private Task IsolateVoiceAsync() => ProcessVoiceAsync(VoiceProcessingMode.Isolate);

    [RelayCommand(CanExecute = nameof(CanEnhanceVoice))]
    private Task DenoiseAsync() => ProcessVoiceAsync(VoiceProcessingMode.Denoise);

    private async Task ProcessVoiceAsync(VoiceProcessingMode mode)
    {
        var sources = AudioClips();
        if (sources.Count == 0 || Timeline.Project is not { } project) return;
        var strength = DenoiseStrength / 100;
        var mute = MuteOriginal;
        await RunAsync(mode == VoiceProcessingMode.Isolate ? "Separando a voz dos demais sons…" : "Reduzindo o ruído…", async (progress, ct) =>
        {
            var folder = GeneratedFolder();
            var results = new List<(Clip Source, VoiceProcessingResult Result)>();
            for (var i = 0; i < sources.Count; i++)
                results.Add((sources[i], await _voice.ProcessAsync(project, sources[i], Timeline.MediaPaths, mode, strength, folder,
                    Part(progress, i, sources.Count), ct)));
            var files = results.SelectMany(r => r.Result.ResidualPath is { } res ? new[] { r.Result.VoicePath, res } : [r.Result.VoicePath]).ToList();
            var imported = await _import.ImportAsync(files, "IA", null, ct);
            var byPath = imported.Imported.ToDictionary(a => Path.GetFullPath(a.OriginalPath), a => a.Id, StringComparer.OrdinalIgnoreCase);
            if (files.Any(f => !byPath.ContainsKey(Path.GetFullPath(f))))
            {
                Status = "Os arquivos foram gerados, mas não puderam ser importados: " +
                         string.Join("; ", imported.Failed.Select(f => f.Reason));
                return;
            }
            var items = results.Select(r => new GeneratedAudio(r.Source.Id,
                (r.Result.ResidualPath is { } res ? new[] { r.Result.VoicePath, res } : [r.Result.VoicePath])
                    .Select(f => byPath[Path.GetFullPath(f)]).ToList())).ToList();
            string[] names = mode == VoiceProcessingMode.Isolate ? ["Voz (IA)", "Ambiente (IA)"] : ["Sem ruído (IA)"];
            var done = Timeline.Execute(new PlaceGeneratedAudioCommand(Timeline.SequenceId, items, names, mute,
                mode == VoiceProcessingMode.Isolate ? "Isolar voz (IA)" : "Reduzir ruído (IA)"));
            Status = !done ? "Não foi possível inserir o áudio processado."
                : mode == VoiceProcessingMode.Isolate
                    ? "Voz e ambiente em trilhas separadas" + (mute ? "; o áudio original foi silenciado." : ".")
                    : "Áudio sem ruído numa trilha nova" + (mute ? "; o original foi silenciado." : ".");
        });
    }

    /// <summary>Pasta dos arquivos gerados: ao lado do projeto salvo ou em Documentos.</summary>
    private string GeneratedFolder()
    {
        var baseDir = _projects.Current?.FilePath is { } file ? Path.GetDirectoryName(file)! :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Meu Editor de Vídeo");
        return Path.Combine(baseDir, "Mídias geradas (IA)");
    }

    // ---------------------------------------------------------------- silêncio (RF165)

    private bool CanVad() => !IsBusy && HasAudio && VadAvailable;

    /// <summary>Remove os trechos sem fala (detectada por IA), fechando os espaços com os clipes vinculados.</summary>
    [RelayCommand(CanExecute = nameof(CanVad))]
    private async Task RemoveSilenceAsync()
    {
        if (AudioClip() is not { } cur || Timeline.Project is not { } project) return;
        var min = MediaTime.FromSecondsApprox(SilenceMinSeconds);
        await RunAsync("Detectando a fala…", async (progress, ct) =>
        {
            var speech = await _vad.DetectSpeechAsync(project, cur.Clip, Timeline.MediaPaths, 0.5, progress, ct);
            var gaps = SilenceGaps(cur.Clip, speech, min);
            if (gaps.Count == 0)
            {
                Status = speech.Count == 0 ? "Nenhuma fala detectada no clipe; nada foi removido." : "Nenhum silêncio longo entre as falas.";
                return;
            }
            var total = gaps.Sum(g => g.Duration.TotalSeconds);
            Status = Timeline.Execute(new RemoveTimeRangesCommand(Timeline.SequenceId, cur.Clip.Id, gaps,
                    $"Remover {gaps.Count} {(gaps.Count == 1 ? "silêncio" : "silêncios")} (IA)"))
                ? $"{gaps.Count} {(gaps.Count == 1 ? "trecho sem fala removido" : "trechos sem fala removidos")} ({total.ToString("0.0", Pt)} s)."
                : "Não foi possível remover os silêncios.";
        });
    }

    /// <summary>Intervalos sem fala dentro do clipe com pelo menos <paramref name="min"/> de duração.</summary>
    public static IReadOnlyList<TimeRange> SilenceGaps(Clip clip, IReadOnlyList<TimeRange> speech, MediaTime min)
    {
        var gaps = new List<TimeRange>();
        if (speech.Count == 0) return gaps;
        var cursor = clip.Start;
        foreach (var s in speech.OrderBy(s => s.Start.Ticks))
        {
            if (s.Start - cursor >= min) gaps.Add(new TimeRange(cursor, s.Start));
            if (s.End > cursor) cursor = s.End;
        }
        if (clip.End - cursor >= min) gaps.Add(new TimeRange(cursor, clip.End));
        return gaps;
    }

    // ---------------------------------------------------------------- cenas (RF161)

    private bool CanDetectScenes() => !IsBusy && IsVideo;

    [RelayCommand(CanExecute = nameof(CanDetectScenes))]
    private async Task DetectScenesAsync()
    {
        if (VisualClip() is not { } cur || Timeline.Project is not { } project) return;
        var sensitivity = SceneSensitivity / 100;
        var split = SplitAtScenes;
        await RunAsync("Procurando mudanças de cena…", async (progress, ct) =>
        {
            var result = await _scenes.DetectAsync(project, cur.Clip, Timeline.MediaPaths, sensitivity, progress, ct);
            if (result.Cuts.Count == 0)
            {
                Status = "Nenhuma mudança de cena encontrada.";
                return;
            }
            var seq = Timeline.Sequence;
            var existing = seq?.Markers.Count(m => m.Name.StartsWith("Cena", StringComparison.Ordinal)) ?? 0;
            var commands = new List<IEditCommand>();
            for (var i = 0; i < result.Cuts.Count; i++)
                commands.Add(new AddMarkerCommand(Timeline.SequenceId, result.Cuts[i], $"Cena {existing + i + 2}"));
            if (split)
            {
                // Do fim para o começo: a parte esquerda mantém o Id do clipe a cada divisão.
                foreach (var cut in result.Cuts.OrderByDescending(c => c.Ticks))
                    commands.Add(new SplitClipsCommand(Timeline.SequenceId, cut, [cur.Clip.Id]));
            }
            var n = result.Cuts.Count;
            Timeline.Execute(new CompositeCommand(split ? $"Dividir nas cenas ({n})" : $"Marcar cenas ({n})", commands));
            Status = $"{n} {(n == 1 ? "mudança de cena marcada" : "mudanças de cena marcadas")}" +
                     (split ? " e clipe dividido" : "") + (result.UsedModel ? " (TransNetV2)." : " (comparação de cores).");
        });
    }

    // ---------------------------------------------------------------- melhoria de imagem (RF164)

    private bool CanEnhanceImage() => !IsBusy && IsImage && EnhancerAvailable;

    /// <summary>Amplia a foto 2× com IA e troca a mídia do clipe pela versão melhorada (desfazer volta à original).</summary>
    [RelayCommand(CanExecute = nameof(CanEnhanceImage))]
    private async Task EnhanceImageAsync()
    {
        if (VisualClip() is not { } cur || Timeline.Project is not { } project || cur.Clip.MediaAssetId is not { } id
            || project.FindMedia(id) is not { } asset || !Timeline.MediaPaths.TryGetValue(id, out var path)) return;
        await RunAsync("Melhorando a imagem…", async (progress, ct) =>
        {
            var folder = GeneratedFolder();
            Directory.CreateDirectory(folder);
            var output = UniquePath(Path.Combine(folder, Path.GetFileNameWithoutExtension(asset.Name) + " (melhorada 2x).png"));
            await _enhancer.EnhanceImageAsync(path, output, progress, ct);
            var imported = await _import.ImportAsync([output], "IA", null, ct);
            if (imported.Imported.Count == 0)
            {
                Status = "A imagem foi gerada, mas não pôde ser importada.";
                return;
            }
            var newId = imported.Imported[0].Id;
            Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, c => c with { MediaAssetId = newId }, "Melhorar imagem (IA)"));
            Status = $"Imagem ampliada para {imported.Imported[0].Width}×{imported.Imported[0].Height}.";
        });
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    // ---------------------------------------------------------------- execução em segundo plano

    private async Task RunAsync(string text, Func<IProgress<double>, CancellationToken, Task> work)
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        IsBusy = true;
        BusyText = text;
        Status = "";
        Progress = 0;
        var progress = new Progress<double>(p => Progress = Math.Round(Math.Clamp(p, 0, 1) * 100));
        try
        {
            await work(progress, token);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelado.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException
                                       or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or OpenCvSharp.OpenCVException)
        {
            _logger.LogWarning(ex, "Falha no recurso de IA");
            _dialogs.ShowInfo("Inteligência artificial", $"Não foi possível concluir: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _work?.Cancel();

    public void Dispose()
    {
        _work?.Cancel();
        _models.Changed -= OnModelsChanged;
        Timeline.Invalidated -= OnTimelineInvalidated;
    }
}
