using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application.Performance;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Opção de uma lista de escolha (rótulo + valor).</summary>
public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>Linha da tabela de atalhos (RF212).</summary>
public sealed partial class ShortcutItemViewModel(ShortcutAction action, SettingsViewModel owner) : ObservableObject
{
    public ShortcutAction Action { get; } = action;
    public string Id => Action.Id;
    public string Label => Action.Label;
    public string Category => Action.Category;

    /// <summary>Combinação escolhida pelo usuário; null = padrão; "" = sem atalho.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(IsCustom))]
    public partial string? Override { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    public partial string Conflict { get; set; } = "";

    public bool HasConflict => Conflict.Length > 0;

    public bool IsCustom => Override is not null;

    /// <summary>Texto exibido: as combinações em vigor ("Ctrl+Y ou Ctrl+Shift+Z"), ou "(sem atalho)".</summary>
    public string Text
    {
        get
        {
            var texts = Override is { } o ? (o.Length == 0 ? [] : [o]) : Action.Defaults;
            var chords = texts.Select(t => KeyChord.TryParse(t, out var c) ? c.Display : t).ToList();
            return chords.Count == 0 ? "(sem atalho)" : string.Join(" ou ", chords);
        }
    }

    /// <summary>Define a combinação pressionada pelo usuário.</summary>
    public void Assign(KeyChord chord)
    {
        var text = chord.ToString();
        Override = Action.Defaults.Length == 1 && Action.Defaults[0] == text ? null : text;
        owner.ShortcutsChanged();
    }

    [RelayCommand]
    private void Reset()
    {
        Override = null;
        owner.ShortcutsChanged();
    }

    [RelayCommand]
    private void Clear()
    {
        Override = "";
        owner.ShortcutsChanged();
    }
}

/// <summary>
/// Configurações do aplicativo (§3.15): geral, pastas (RF208), cache (RF209), pré-visualização (RF210), aceleração por GPU
/// e desempenho (RF211, §12.3), atalhos (RF212), salvamento automático (RF213) e IA (RF214). Só grava ao salvar.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _theme;
    private readonly IHardwareInfoService _hardware;
    private readonly ICacheMaintenance _cache;
    private readonly IAppPaths _paths;

    public SettingsViewModel(ISettingsService settings, IDialogService dialogs, IThemeService theme, IHardwareInfoService hardware,
        ICacheMaintenance cache, IAppPaths paths)
    {
        _settings = settings;
        _dialogs = dialogs;
        _theme = theme;
        _hardware = hardware;
        _cache = cache;
        _paths = paths;

        var s = settings.Current;
        Theme = Themes.First(t => t.Value == s.Theme);
        OpenLastProject = s.OpenLastProjectOnStartup;
        ProjectsDirectory = s.DefaultProjectsDirectory ?? DefaultProjectsDirectory;
        ExportDirectory = s.DefaultExportDirectory ?? DefaultExportDirectory;
        CacheGigabytes = Math.Clamp(s.PreviewCacheGigabytes, 1, 200);
        PreviewQuality = PreviewQualities.First(q => q.Value == s.PreviewQuality);
        PreviewMemory = PreviewMemories.FirstOrDefault(m => m.Value == s.PreviewMemoryMegabytes) ?? PreviewMemories[0];
        UseProxies = s.UseProxies;
        AutoGenerateProxies = s.AutoGenerateProxies;
        UseGpu = s.UseGpuAcceleration;
        AiDevice = AiDevices.First(d => d.Value == s.AiDevice);
        BackgroundRendering = s.BackgroundRendering;
        AutosaveEnabled = s.AutosaveEnabled;
        AutosaveMinutes = Math.Clamp(s.AutosaveIntervalMinutes, 1, 60);

        foreach (var action in ShortcutCatalog.All)
            Shortcuts.Add(new ShortcutItemViewModel(action, this)
            {
                Override = s.Shortcuts.TryGetValue(action.Id, out var o) ? o : null,
            });
        ShortcutsChanged();
    }

    public static string DefaultProjectsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Meu Editor de Vídeo");

    public static string DefaultExportDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Meu Editor de Vídeo", "Exportações");

    /// <summary>Fecha a janela (true = salvo).</summary>
    public event EventHandler<bool>? CloseRequested;

    // ---------------------------------------------------------------- Geral

    public IReadOnlyList<Choice<AppTheme>> Themes { get; } =
    [
        new("Escuro", AppTheme.Dark),
        new("Claro", AppTheme.Light),
        new("Igual ao Windows", AppTheme.System),
    ];

    [ObservableProperty]
    public partial Choice<AppTheme> Theme { get; set; }

    [ObservableProperty]
    public partial bool OpenLastProject { get; set; }

    // ---------------------------------------------------------------- Pastas (RF208)

    [ObservableProperty]
    public partial string ProjectsDirectory { get; set; }

    [ObservableProperty]
    public partial string ExportDirectory { get; set; }

    public string CacheDirectory => _paths.Cache;

    [RelayCommand]
    private void BrowseProjects()
    {
        if (_dialogs.PickFolder("Pasta padrão dos projetos", Directory.Exists(ProjectsDirectory) ? ProjectsDirectory : null) is { } f)
            ProjectsDirectory = f;
    }

    [RelayCommand]
    private void BrowseExport()
    {
        if (_dialogs.PickFolder("Pasta padrão das exportações", Directory.Exists(ExportDirectory) ? ExportDirectory : null) is { } f)
            ExportDirectory = f;
    }

    // ---------------------------------------------------------------- Cache (RF209)

    /// <summary>Limite do cache em disco (GB).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheLimitText))]
    public partial double CacheGigabytes { get; set; }

    public string CacheLimitText => $"{Math.Round(CacheGigabytes):0} GB";

    [ObservableProperty]
    public partial IReadOnlyList<Choice<string>> CacheUsage { get; set; } = [];

    [ObservableProperty]
    public partial string CacheTotal { get; set; } = "Calculando…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCacheCommand), nameof(TrimCacheCommand))]
    public partial bool CacheBusy { get; set; }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", Pt) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", Pt) + " MB",
        >= 1L << 10 => (bytes / 1024.0).ToString("0", Pt) + " KB",
        _ => bytes.ToString(Pt) + " bytes",
    };

    public async Task RefreshCacheAsync()
    {
        var usage = await Task.Run(_cache.Measure);
        CacheUsage = usage.Select(u => new Choice<string>(u.Area, u.Bytes == 0 ? "vazio" : $"{Size(u.Bytes)} · {u.Entries} itens")).ToList();
        CacheTotal = $"Em uso: {Size(usage.Sum(u => u.Bytes))} de {CacheLimitText}";
    }

    private bool CacheIdle() => !CacheBusy;

    [RelayCommand(CanExecute = nameof(CacheIdle))]
    private async Task ClearCacheAsync()
    {
        if (!_dialogs.Confirm("Limpar cache", "Apagar miniaturas, formas de onda, proxies e máscaras de IA? Eles serão recriados quando necessário (proxies e máscaras levam algum tempo)."))
            return;
        CacheBusy = true;
        try
        {
            var freed = await Task.Run(() => _cache.Clear());
            CacheStatus = $"Cache limpo: {Size(freed)} liberados.";
            await RefreshCacheAsync();
        }
        finally
        {
            CacheBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CacheIdle))]
    private async Task TrimCacheAsync()
    {
        CacheBusy = true;
        try
        {
            var limit = (long)Math.Round(CacheGigabytes) << 30;
            var freed = await Task.Run(() => _cache.Trim(limit));
            CacheStatus = freed > 0 ? $"{Size(freed)} liberados (itens usados há mais tempo)." : "O cache já está dentro do limite.";
            await RefreshCacheAsync();
        }
        finally
        {
            CacheBusy = false;
        }
    }

    [ObservableProperty]
    public partial string CacheStatus { get; set; } = "";

    // ---------------------------------------------------------------- Pré-visualização (RF210)

    public IReadOnlyList<Choice<PreviewQualityMode>> PreviewQualities { get; } =
    [
        new("Automática (pelo hardware e pelo projeto)", PreviewQualityMode.Auto),
        new("Completa", PreviewQualityMode.Full),
        new("1/2 da resolução", PreviewQualityMode.Half),
        new("1/4 da resolução", PreviewQualityMode.Quarter),
    ];

    [ObservableProperty]
    public partial Choice<PreviewQualityMode> PreviewQuality { get; set; }

    public IReadOnlyList<Choice<int>> PreviewMemories { get; } =
    [
        new("Automática", 0),
        new("256 MB", 256),
        new("512 MB", 512),
        new("1 GB", 1024),
        new("2 GB", 2048),
        new("4 GB", 4096),
    ];

    [ObservableProperty]
    public partial Choice<int> PreviewMemory { get; set; }

    [ObservableProperty]
    public partial bool UseProxies { get; set; }

    [ObservableProperty]
    public partial bool AutoGenerateProxies { get; set; }

    // ---------------------------------------------------------------- Desempenho e GPU (RF211, §12.3)

    [ObservableProperty]
    public partial bool UseGpu { get; set; }

    public IReadOnlyList<Choice<AiDevice>> AiDevices { get; } =
    [
        new("GPU (DirectML), com recuo para a CPU", Core.AI.AiDevice.Gpu),
        new("Somente CPU", Core.AI.AiDevice.Cpu),
    ];

    [ObservableProperty]
    public partial Choice<AiDevice> AiDevice { get; set; }

    [ObservableProperty]
    public partial bool BackgroundRendering { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Choice<string>> HardwareRows { get; set; } = [new("Hardware", "Detectando…")];

    [ObservableProperty]
    public partial string PerformanceSummary { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> Limitations { get; set; } = [];

    [ObservableProperty]
    public partial string Recommendation { get; set; } = "";

    public async Task RefreshHardwareAsync()
    {
        HardwareInfo hw;
        try
        {
            hw = await Task.Run(() => _hardware.Get(refresh: true));
        }
        catch (Exception ex)
        {
            HardwareRows = [new("Hardware", "Não foi possível detectar: " + ex.Message)];
            return;
        }
        var profile = PerformanceAdvisor.Evaluate(hw);
        var gb = PerformanceAdvisor.Gb;
        var rows = new List<Choice<string>>
        {
            new("Processador", $"{hw.CpuName} · {hw.LogicalProcessors} núcleos lógicos"),
            new("Memória", $"{gb(hw.TotalMemoryBytes)} ({gb(hw.AvailableMemoryBytes)} livres)"),
        };
        rows.AddRange(hw.Gpus.Count == 0
            ? [new Choice<string>("Placa de vídeo", "Não detectada")]
            : hw.Gpus.Select(g => new Choice<string>("Placa de vídeo", $"{g.Name} · {(g.VideoMemoryBytes > 0 ? gb(g.VideoMemoryBytes) : "memória desconhecida")} · {(g.Dedicated ? "dedicada" : "integrada/compartilhada")}")));
        rows.Add(new("Decodificação por GPU", hw.HardwareDecoding ? "Disponível (D3D11VA)" : "Indisponível"));
        rows.Add(new("Codificação por GPU", hw.HardwareEncoders.Count == 0 ? "Indisponível" : string.Join("; ", hw.HardwareEncoders)));
        rows.AddRange(hw.Disks.Select(d => new Choice<string>($"Disco ({d.Label})", $"{d.Root} · {gb(d.FreeBytes)} livres de {gb(d.TotalBytes)}")));
        HardwareRows = rows;
        PerformanceSummary = profile.Summary;
        Limitations = profile.Limitations.Count == 0 ? ["Nenhuma limitação encontrada."] : profile.Limitations;
        var quality = profile.Preview switch { PreviewQualityMode.Full => "completa", PreviewQualityMode.Quarter => "1/4", _ => "1/2" };
        Recommendation = $"Recomendado para projetos Full HD: pré-visualização {quality}, {profile.PreviewMemoryMegabytes} MB para quadros prontos"
            + (profile.RecommendProxies ? ", proxies ligados." : ".");
    }

    // ---------------------------------------------------------------- Salvamento automático (RF213)

    [ObservableProperty]
    public partial bool AutosaveEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutosaveText))]
    public partial double AutosaveMinutes { get; set; }

    public string AutosaveText => $"A cada {Math.Round(AutosaveMinutes):0} min";

    // ---------------------------------------------------------------- Atalhos (RF212)

    public ObservableCollection<ShortcutItemViewModel> Shortcuts { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string ShortcutConflicts { get; set; } = "";

    /// <summary>Recalcula os conflitos depois de uma alteração.</summary>
    public void ShortcutsChanged()
    {
        var map = ShortcutService.Resolve(Overrides());
        var conflicts = ShortcutService.Conflicts(map);
        foreach (var item in Shortcuts)
        {
            var others = conflicts.Where(c => c.Value.Contains(item.Id))
                .SelectMany(c => c.Value.Where(id => id != item.Id).Select(id => (Chord: c.Key, Id: id)))
                .Select(x => $"{x.Chord.Display} também é usado em \"{ShortcutCatalog.Find(x.Id)?.Label}\"")
                .ToList();
            item.Conflict = string.Join("; ", others);
        }
        ShortcutConflicts = conflicts.Count == 0 ? "" : $"{conflicts.Count} combinação(ões) em conflito: corrija antes de salvar.";
    }

    private Dictionary<string, string> Overrides() =>
        Shortcuts.Where(s => s.Override is not null).ToDictionary(s => s.Id, s => s.Override!);

    [RelayCommand]
    private void ResetShortcuts()
    {
        foreach (var item in Shortcuts) item.Override = null;
        ShortcutsChanged();
    }

    // ---------------------------------------------------------------- IA (RF214)

    public event EventHandler? OpenModelManagerRequested;

    [RelayCommand]
    private void OpenModelManager() => OpenModelManagerRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>O gerenciador de modelos grava o dispositivo da IA direto nas configurações; acompanha aqui.</summary>
    public void ReloadAiDevice() => AiDevice = AiDevices.First(d => d.Value == _settings.Current.AiDevice);

    // ---------------------------------------------------------------- Salvar

    private bool CanSave() => ShortcutConflicts.Length == 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var current = _settings.Current;
        var updated = current with
        {
            Theme = Theme.Value,
            OpenLastProjectOnStartup = OpenLastProject,
            DefaultProjectsDirectory = string.Equals(ProjectsDirectory, DefaultProjectsDirectory, StringComparison.OrdinalIgnoreCase) ? null : ProjectsDirectory,
            DefaultExportDirectory = string.Equals(ExportDirectory, DefaultExportDirectory, StringComparison.OrdinalIgnoreCase) ? null : ExportDirectory,
            PreviewCacheGigabytes = (int)Math.Round(CacheGigabytes),
            PreviewQuality = PreviewQuality.Value,
            PreviewMemoryMegabytes = PreviewMemory.Value,
            UseProxies = UseProxies,
            AutoGenerateProxies = AutoGenerateProxies,
            UseGpuAcceleration = UseGpu,
            AiDevice = AiDevice.Value,
            BackgroundRendering = BackgroundRendering,
            AutosaveEnabled = AutosaveEnabled,
            AutosaveIntervalMinutes = (int)Math.Round(AutosaveMinutes),
            Shortcuts = Overrides(),
        };
        await _settings.SaveAsync(updated);
        if (updated.Theme != current.Theme) _theme.Apply(updated.Theme);
        if (updated.PreviewCacheGigabytes < current.PreviewCacheGigabytes)
        {
            var limit = (long)updated.PreviewCacheGigabytes << 30;
            _ = Task.Run(() => _cache.Trim(limit));
        }
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>Tecla capturada no campo de atalho; ignora modificadores sozinhos.</summary>
    public static KeyChord? Capture(Key key, ModifierKeys modifiers)
    {
        if (!KeyChord.IsAssignable(key)) return null;
        return new KeyChord(key, modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt));
    }
}
