using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Export;

namespace MeuEditorDeVideo.App.ViewModels.Export;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record TemplateItem(ExportTemplate Template)
{
    public string Name => Template.Name;
    public string Description => Template.Description;
    public bool IsBuiltIn => Template.IsBuiltIn;
    public string Badge => Template.IsBuiltIn ? "" : "Meu modelo";
}

/// <summary>
/// Tela de exportação (§5.4): modelos (§10), parâmetros técnicos, intervalo, destino e envio para a fila.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IExportTemplateStore _templates;
    private readonly IRenderQueueService _queue;
    private readonly IRenderEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly ProjectSession _session;
    private readonly MediaTime _playhead;
    private bool _loading;

    public ExportViewModel(IExportTemplateStore templates, IRenderQueueService queue, IRenderEngine engine, IDialogService dialogs,
        ISettingsService settings, IProjectService projects, MediaTime playhead)
    {
        _templates = templates;
        _queue = queue;
        _engine = engine;
        _dialogs = dialogs;
        _settings = settings;
        _session = projects.Current ?? throw new InvalidOperationException("Nenhum projeto aberto.");
        _playhead = playhead;

        var project = _session.Project;
        Directory = settings.Current.DefaultExportDirectory is { Length: > 0 } d ? d
            : _session.FilePath is { } fp ? Path.GetDirectoryName(fp)!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        FileName = project.Name;
        Background = settings.Current.BackgroundRendering;
        RangeStartText = Timecode.Format(MediaTime.Zero, project.Settings.FrameRate);
        RangeEndText = Timecode.Format(project.MainSequence.Duration, project.Settings.FrameRate);
        ReloadTemplates(templates.GetAll().FirstOrDefault()?.Id);
    }

    public event EventHandler<bool>? CloseRequested;

    /// <summary>Trabalho criado ao confirmar (para a janela da fila acompanhar).</summary>
    public RenderJob? CreatedJob { get; private set; }

    public ObservableCollection<TemplateItem> Templates { get; } = [];

    public IReadOnlyList<Choice<ExportContainer>> Containers { get; } =
        ExportFormats.Containers.Select(c => new Choice<ExportContainer>(c, ExportFormats.ContainerName(c))).ToList();

    public IReadOnlyList<Choice<EncoderPreference>> EncoderPreferences { get; } =
    [
        new(EncoderPreference.Auto, "Automático (placa de vídeo quando disponível)"),
        new(EncoderPreference.Hardware, "Placa de vídeo (inclui Media Foundation)"),
        new(EncoderPreference.Software, "Software (mais compatível)"),
    ];

    public IReadOnlyList<Choice<RateControl>> RateControls { get; } =
    [
        new(Core.Export.RateControl.Vbr, "Taxa variável (VBR)"),
        new(Core.Export.RateControl.Cbr, "Taxa constante (CBR)"),
        new(Core.Export.RateControl.ConstantQuality, "Qualidade constante"),
    ];

    public IReadOnlyList<Choice<FrameRate?>> FrameRates { get; } =
        new[] { new Choice<FrameRate?>(null, "Do projeto") }
            .Concat(Domain.Time.FrameRate.Standard.Select(f => new Choice<FrameRate?>(f, f.Value.ToString("0.###", PtBr) + " FPS"))).ToList();

    public IReadOnlyList<Choice<int>> SampleRates { get; } = [new(44_100, "44,1 kHz"), new(48_000, "48 kHz"), new(96_000, "96 kHz")];

    public IReadOnlyList<Choice<int>> ChannelOptions { get; } = [new(1, "Mono"), new(2, "Estéreo"), new(6, "5.1")];

    public IReadOnlyList<Choice<SubtitleFileExport>> SubtitleFiles { get; } =
        [new(SubtitleFileExport.None, "Nenhum"), new(SubtitleFileExport.Srt, "SubRip (.srt)"), new(SubtitleFileExport.Vtt, "WebVTT (.vtt)")];

    /// <summary>A sequência tem trilhas de legenda com conteúdo (mostra as opções de legenda).</summary>
    public bool HasSubtitles => _session.Project.MainSequence.Tracks.Exists(t => t.Kind == Domain.Model.TrackKind.Subtitle && t.Clips.Count > 0);

    public IReadOnlyList<Choice<string?>> Profiles { get; } = [new(null, "Padrão"), new("high", "High"), new("main", "Main"), new("baseline", "Baseline")];

    public ObservableCollection<Choice<VideoCodec>> VideoCodecs { get; } = [];

    public ObservableCollection<Choice<AudioCodec>> AudioCodecs { get; } = [];

    [ObservableProperty] public partial TemplateItem? SelectedTemplate { get; set; }

    [ObservableProperty] public partial Choice<ExportContainer>? Container { get; set; }
    [ObservableProperty] public partial Choice<VideoCodec>? VideoCodec { get; set; }
    [ObservableProperty] public partial Choice<EncoderPreference>? Encoder { get; set; }
    [ObservableProperty] public partial bool UseProjectResolution { get; set; }
    [ObservableProperty] public partial string WidthText { get; set; } = "";
    [ObservableProperty] public partial string HeightText { get; set; } = "";
    [ObservableProperty] public partial Choice<FrameRate?>? FrameRate { get; set; }
    [ObservableProperty] public partial Choice<RateControl>? RateControl { get; set; }
    [ObservableProperty] public partial string BitrateText { get; set; } = "";
    [ObservableProperty] public partial double Quality { get; set; } = 23;
    [ObservableProperty] public partial Choice<string?>? Profile { get; set; }
    [ObservableProperty] public partial Choice<AudioCodec>? AudioCodec { get; set; }
    [ObservableProperty] public partial string AudioBitrateText { get; set; } = "";
    [ObservableProperty] public partial Choice<int>? SampleRate { get; set; }
    [ObservableProperty] public partial Choice<int>? Channels { get; set; }
    [ObservableProperty] public partial bool Alpha { get; set; }
    [ObservableProperty] public partial bool BurnSubtitles { get; set; } = true;
    [ObservableProperty] public partial Choice<SubtitleFileExport>? SubtitleFile { get; set; }
    [ObservableProperty] public partial bool SplitByDuration { get; set; }
    [ObservableProperty] public partial string MaxSecondsText { get; set; } = "60";
    [ObservableProperty] public partial bool LimitSize { get; set; }
    [ObservableProperty] public partial string TargetSizeText { get; set; } = "16";

    [ObservableProperty] public partial bool ExportRange { get; set; }
    [ObservableProperty] public partial string RangeStartText { get; set; }
    [ObservableProperty] public partial string RangeEndText { get; set; }

    [ObservableProperty] public partial string Directory { get; set; }
    [ObservableProperty] public partial string FileName { get; set; }
    [ObservableProperty] public partial bool Background { get; set; }

    [ObservableProperty] public partial string ValidationMessage { get; set; } = "";
    [ObservableProperty] public partial string EncoderInfo { get; set; } = "";
    [ObservableProperty] public partial bool IsModified { get; set; }

    public string ProjectSummary
    {
        get
        {
            var s = _session.Project.Settings;
            return string.Create(PtBr, $"Projeto: {s.Width} × {s.Height}, {s.FrameRate.Value:0.###} FPS · duração {Timecode.FormatSeconds(_session.Project.MainSequence.Duration)}");
        }
    }

    public bool SupportsAlpha => Container is { } c && VideoCodec is { } v && ExportFormats.SupportsAlpha(c.Value, v.Value);

    public bool HasBitrate => VideoCodec is { } v && ExportFormats.HasBitrate(v.Value) && RateControl?.Value != Core.Export.RateControl.ConstantQuality;

    public bool HasQuality => VideoCodec is { } v && ExportFormats.HasBitrate(v.Value) && RateControl?.Value == Core.Export.RateControl.ConstantQuality;

    public bool HasRateControl => VideoCodec is { } v && ExportFormats.HasBitrate(v.Value);

    public bool HasProfile => VideoCodec?.Value is Core.Export.VideoCodec.H264 or Core.Export.VideoCodec.Hevc;

    public bool HasAudio => AudioCodec is { } a && a.Value != Core.Export.AudioCodec.None;

    public bool HasAudioBitrate => AudioCodec is { } a && ExportFormats.HasAudioBitrate(a.Value);

    public string QualityLabel => string.Create(PtBr, $"Qualidade: {Quality:0} (menor = melhor; 18–23 recomendado)");

    public string Extension => Container is { } c ? ExportFormats.Extension(c.Value) : ".mp4";

    public string OutputPath => Path.Combine(Directory ?? "", (FileName ?? "").Trim() + Extension);

    public bool CanDeleteTemplate => SelectedTemplate is { IsBuiltIn: false };

    public bool CanRestoreTemplate => SelectedTemplate is { IsBuiltIn: true } t && _templates.GetBuiltIn(t.Template.Id) is { } b && b != t.Template;

    // ------------------------------------------------------------------ modelos

    private void ReloadTemplates(string? selectId)
    {
        Templates.Clear();
        foreach (var t in _templates.GetAll()) Templates.Add(new TemplateItem(t));
        SelectedTemplate = Templates.FirstOrDefault(t => t.Template.Id == selectId) ?? Templates.FirstOrDefault();
        if (SelectedTemplate is null) Load(new ExportSettings());
    }

    partial void OnSelectedTemplateChanged(TemplateItem? value)
    {
        if (value is not null) Load(value.Template.Settings);
        OnPropertyChanged(nameof(CanDeleteTemplate));
        OnPropertyChanged(nameof(CanRestoreTemplate));
        DeleteTemplateCommand.NotifyCanExecuteChanged();
        RestoreTemplateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Preenche os campos a partir de uma configuração.</summary>
    private void Load(ExportSettings s)
    {
        _loading = true;
        try
        {
            Container = Containers.First(c => c.Value == s.Container);
            RefreshCodecLists();
            VideoCodec = VideoCodecs.FirstOrDefault(v => v.Value == s.VideoCodec) ?? VideoCodecs[0];
            AudioCodec = AudioCodecs.FirstOrDefault(a => a.Value == s.AudioCodec) ?? AudioCodecs[0];
            Encoder = EncoderPreferences.First(e => e.Value == s.Encoder);
            UseProjectResolution = s.Width is null || s.Height is null;
            var ps = _session.Project.Settings;
            WidthText = (s.Width ?? ps.Width).ToString(CultureInfo.InvariantCulture);
            HeightText = (s.Height ?? ps.Height).ToString(CultureInfo.InvariantCulture);
            FrameRate = FrameRates.FirstOrDefault(f => Equals(f.Value, s.FrameRate)) ?? FrameRates[0];
            RateControl = RateControls.First(r => r.Value == s.RateControl);
            BitrateText = s.VideoBitrateKbps.ToString(CultureInfo.InvariantCulture);
            Quality = s.Quality;
            Profile = Profiles.FirstOrDefault(p => p.Value == s.Profile) ?? Profiles[0];
            AudioBitrateText = s.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture);
            SampleRate = SampleRates.FirstOrDefault(r => r.Value == s.SampleRate) ?? SampleRates[1];
            Channels = ChannelOptions.FirstOrDefault(c => c.Value == s.AudioChannels) ?? ChannelOptions[1];
            Alpha = s.Alpha;
            BurnSubtitles = s.BurnSubtitles;
            SubtitleFile = SubtitleFiles.First(f => f.Value == s.SubtitleFile);
            SplitByDuration = s.MaxSecondsPerFile is not null;
            MaxSecondsText = (s.MaxSecondsPerFile ?? 60).ToString("0.##", PtBr);
            LimitSize = s.TargetSizeMegabytes is not null;
            TargetSizeText = (s.TargetSizeMegabytes ?? 16).ToString("0.##", PtBr);
        }
        finally
        {
            _loading = false;
        }
        IsModified = false;
        RefreshDerived();
    }

    private void RefreshCodecLists()
    {
        if (Container is null) return;
        var v = VideoCodec?.Value;
        var a = AudioCodec?.Value;
        VideoCodecs.Clear();
        foreach (var c in ExportFormats.VideoCodecsFor(Container.Value)) VideoCodecs.Add(new Choice<VideoCodec>(c, ExportFormats.VideoCodecName(c)));
        AudioCodecs.Clear();
        foreach (var c in ExportFormats.AudioCodecsFor(Container.Value)) AudioCodecs.Add(new Choice<AudioCodec>(c, ExportFormats.AudioCodecName(c)));
        VideoCodec = VideoCodecs.FirstOrDefault(x => x.Value == v) ?? VideoCodecs[0];
        AudioCodec = AudioCodecs.FirstOrDefault(x => x.Value == a) ?? AudioCodecs[0];
    }

    partial void OnContainerChanged(Choice<ExportContainer>? value)
    {
        if (!_loading) RefreshCodecLists();
        Changed();
    }

    partial void OnVideoCodecChanged(Choice<VideoCodec>? value) => Changed();
    partial void OnEncoderChanged(Choice<EncoderPreference>? value) => Changed();
    partial void OnUseProjectResolutionChanged(bool value) => Changed();
    partial void OnWidthTextChanged(string value) => Changed();
    partial void OnHeightTextChanged(string value) => Changed();
    partial void OnFrameRateChanged(Choice<FrameRate?>? value) => Changed();
    partial void OnRateControlChanged(Choice<RateControl>? value) => Changed();
    partial void OnBitrateTextChanged(string value) => Changed();
    partial void OnQualityChanged(double value) => Changed();
    partial void OnProfileChanged(Choice<string?>? value) => Changed();
    partial void OnAudioCodecChanged(Choice<AudioCodec>? value) => Changed();
    partial void OnAudioBitrateTextChanged(string value) => Changed();
    partial void OnSampleRateChanged(Choice<int>? value) => Changed();
    partial void OnChannelsChanged(Choice<int>? value) => Changed();
    partial void OnAlphaChanged(bool value) => Changed();
    partial void OnBurnSubtitlesChanged(bool value) => Changed();
    partial void OnSubtitleFileChanged(Choice<SubtitleFileExport>? value) => Changed();
    partial void OnSplitByDurationChanged(bool value) => Changed();
    partial void OnMaxSecondsTextChanged(string value) => Changed();
    partial void OnLimitSizeChanged(bool value) => Changed();
    partial void OnTargetSizeTextChanged(string value) => Changed();
    partial void OnExportRangeChanged(bool value) => RefreshDerived();
    partial void OnRangeStartTextChanged(string value) => RefreshDerived();
    partial void OnRangeEndTextChanged(string value) => RefreshDerived();
    partial void OnDirectoryChanged(string value) => RefreshDerived();
    partial void OnFileNameChanged(string value) => RefreshDerived();

    private void Changed()
    {
        if (_loading) return;
        IsModified = true;
        RefreshDerived();
    }

    private void RefreshDerived()
    {
        if (_loading) return;
        OnPropertyChanged(nameof(SupportsAlpha));
        OnPropertyChanged(nameof(HasBitrate));
        OnPropertyChanged(nameof(HasQuality));
        OnPropertyChanged(nameof(HasRateControl));
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(HasAudioBitrate));
        OnPropertyChanged(nameof(QualityLabel));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(OutputPath));
        UpdateEncoderInfo();
        ValidationMessage = string.Join("\n", Problems());
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void UpdateEncoderInfo()
    {
        if (VideoCodec is null) return;
        var available = _engine.AvailableEncoders(VideoCodec.Value);
        EncoderInfo = available.Count == 0
            ? "Nenhum codificador disponível para este codec."
            : "Disponíveis neste computador: " + string.Join(", ", available.Select(e => e.DisplayName));
    }

    /// <summary>Configuração atual dos campos; null (com mensagem) se algum número for inválido.</summary>
    public ExportSettings? Build(out string? error)
    {
        error = null;
        int? width = null, height = null;
        if (!UseProjectResolution)
        {
            if (!int.TryParse(WidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
                !int.TryParse(HeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
            {
                error = "Informe largura e altura em pixels.";
                return null;
            }
            (width, height) = (w, h);
        }
        if (!int.TryParse(BitrateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kbps)) { error = "Informe a taxa de bits do vídeo em kbps."; return null; }
        if (!int.TryParse(AudioBitrateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var akbps)) { error = "Informe a taxa de bits do áudio em kbps."; return null; }
        double? maxSeconds = null, target = null;
        if (SplitByDuration)
        {
            if (!double.TryParse(MaxSecondsText, NumberStyles.Float, PtBr, out var m)) { error = "Informe a duração máxima por arquivo em segundos."; return null; }
            maxSeconds = m;
        }
        if (LimitSize)
        {
            if (!double.TryParse(TargetSizeText, NumberStyles.Float, PtBr, out var mb)) { error = "Informe o tamanho alvo em MB."; return null; }
            target = mb;
        }

        return new ExportSettings
        {
            Container = Container?.Value ?? ExportContainer.Mp4,
            VideoCodec = VideoCodec?.Value ?? Core.Export.VideoCodec.H264,
            Encoder = Encoder?.Value ?? EncoderPreference.Auto,
            Width = width,
            Height = height,
            FrameRate = FrameRate?.Value,
            RateControl = RateControl?.Value ?? Core.Export.RateControl.Vbr,
            VideoBitrateKbps = kbps,
            Quality = (int)Math.Round(Quality),
            Profile = HasProfile ? Profile?.Value : null,
            AudioCodec = AudioCodec?.Value ?? Core.Export.AudioCodec.Aac,
            AudioBitrateKbps = akbps,
            SampleRate = SampleRate?.Value ?? 48_000,
            AudioChannels = Channels?.Value ?? 2,
            Alpha = Alpha && SupportsAlpha,
            BurnSubtitles = BurnSubtitles,
            SubtitleFile = SubtitleFile?.Value ?? SubtitleFileExport.None,
            MaxSecondsPerFile = maxSeconds,
            TargetSizeMegabytes = target,
        };
    }

    private IEnumerable<string> Problems()
    {
        var s = Build(out var error);
        if (error is not null)
        {
            yield return error;
            yield break;
        }
        foreach (var p in ExportFormats.Validate(s!, _session.Project.Settings)) yield return p;
        if (string.IsNullOrWhiteSpace(FileName) || FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            yield return "Informe um nome de arquivo válido.";
        if (string.IsNullOrWhiteSpace(Directory)) yield return "Escolha a pasta de destino.";
        if (ExportRange && !TryRange(out _, out _)) yield return "Intervalo inválido: use hh:mm:ss:qq, com início antes do fim.";
        if (_session.Project.MainSequence.Duration <= MediaTime.Zero) yield return "A linha do tempo está vazia.";
    }

    private bool TryRange(out MediaTime start, out MediaTime end)
    {
        var fps = _session.Project.Settings.FrameRate;
        end = MediaTime.Zero;
        return Timecode.TryParse(RangeStartText, fps, out start) && Timecode.TryParse(RangeEndText, fps, out end) && start < end;
    }

    [RelayCommand]
    private void RangeFromPlayhead()
    {
        ExportRange = true;
        RangeStartText = Timecode.Format(_playhead, _session.Project.Settings.FrameRate);
    }

    [RelayCommand]
    private void BrowseDirectory()
    {
        if (_dialogs.PickFolder("Pasta de destino", Directory) is { } d) Directory = d;
    }

    // ------------------------------------------------------------------ gerenciamento de modelos

    [RelayCommand]
    private async Task SaveAsNewTemplate()
    {
        var s = Build(out var error);
        if (s is null) { _dialogs.ShowInfo("Modelo de exportação", error!); return; }
        var name = _dialogs.Prompt("Salvar como novo modelo", "Nome do modelo:", (SelectedTemplate?.Name ?? "Modelo") + " (cópia)");
        if (string.IsNullOrWhiteSpace(name)) return;
        var template = new ExportTemplate
        {
            Id = JsonExportTemplateStore.NewUserId(),
            Name = name.Trim(),
            Description = SelectedTemplate?.Description ?? "",
            Settings = s,
        };
        await _templates.SaveAsync(template);
        ReloadTemplates(template.Id);
    }

    /// <summary>Grava as alterações no modelo selecionado (num embutido, vira uma personalização restaurável).</summary>
    [RelayCommand]
    private async Task SaveTemplate()
    {
        if (SelectedTemplate is not { } item) return;
        var s = Build(out var error);
        if (s is null) { _dialogs.ShowInfo("Modelo de exportação", error!); return; }
        await _templates.SaveAsync(item.Template with { Settings = s, IsBuiltIn = false });
        ReloadTemplates(item.Template.Id);
    }

    [RelayCommand]
    private async Task DuplicateTemplate()
    {
        if (SelectedTemplate is not { } item) return;
        var copy = item.Template with { Id = JsonExportTemplateStore.NewUserId(), Name = item.Name + " (cópia)", IsBuiltIn = false };
        await _templates.SaveAsync(copy);
        ReloadTemplates(copy.Id);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteTemplate))]
    private async Task DeleteTemplate()
    {
        if (SelectedTemplate is not { IsBuiltIn: false } item) return;
        if (!_dialogs.Confirm("Excluir modelo", $"Excluir o modelo \"{item.Name}\"?")) return;
        await _templates.DeleteAsync(item.Template.Id);
        ReloadTemplates(null);
    }

    [RelayCommand(CanExecute = nameof(CanRestoreTemplate))]
    private async Task RestoreTemplate()
    {
        if (SelectedTemplate is not { } item) return;
        await _templates.DeleteAsync(item.Template.Id);
        ReloadTemplates(item.Template.Id);
    }

    [RelayCommand]
    private async Task RestoreAllTemplates()
    {
        if (!_dialogs.Confirm("Restaurar modelos padrão", "Descartar as alterações feitas nos modelos que acompanham o aplicativo? Seus modelos próprios são mantidos.")) return;
        await _templates.RestoreDefaultsAsync();
        ReloadTemplates(SelectedTemplate?.Template.Id);
    }

    // ------------------------------------------------------------------ exportar

    private bool CanExport() => ValidationMessage.Length == 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var s = Build(out _);
        if (s is null) return;
        var path = OutputPath;
        if (File.Exists(path) && !_dialogs.Confirm("Substituir arquivo", $"O arquivo já existe:\n{path}\n\nDeseja substituí-lo?")) return;
        if (_queue.Jobs.Any(j => j.IsActive && string.Equals(j.OutputPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.ShowInfo("Exportar", "Já existe uma exportação na fila para este arquivo.");
            return;
        }

        MediaTime start = MediaTime.Zero;
        MediaTime? end = null;
        if (ExportRange && TryRange(out var a, out var b)) (start, end) = (a, b);

        try
        {
            CreatedJob = await _queue.EnqueueAsync(_session.Snapshot, _session.Media.AvailablePaths, s, path,
                SelectedTemplate?.Name ?? "Personalizado", start, end);
        }
        catch (MedException ex)
        {
            _dialogs.ShowError(ex.Error);
            return;
        }

        // Lembra as preferências da última exportação.
        var current = _settings.Current;
        if (current.DefaultExportDirectory != Directory || current.BackgroundRendering != Background)
            await _settings.SaveAsync(current with { DefaultExportDirectory = Directory, BackgroundRendering = Background });
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
