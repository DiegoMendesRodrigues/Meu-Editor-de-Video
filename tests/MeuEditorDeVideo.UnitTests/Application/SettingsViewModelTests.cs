using System.Windows;
using System.Windows.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Errors;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Tela de configurações (RF208–RF214).</summary>
public sealed class SettingsViewModelTests
{
    private sealed class FakeSettings(AppSettings initial) : ISettingsService
    {
        public AppSettings Current { get; private set; } = initial;
        public int Saves { get; private set; }

        public event EventHandler<AppSettings>? Changed;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Saves++;
            Changed?.Invoke(this, settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmAnswer { get; set; } = true;
        public string? Folder { get; set; }

        public bool Confirm(string title, string message) => ConfirmAnswer;
        public string? PickFolder(string title, string? initialDirectory) => Folder;
        public void ShowError(MedError error) { }
        public void ShowInfo(string title, string message) { }
        public UnsavedChoice AskSaveChanges(string projectName) => throw new NotSupportedException();
        public string? PickOpenProject(string? initialDirectory) => null;
        public string? PickSaveProject(string suggestedName, string? initialDirectory) => null;
        public string? PickFile(string title, string filter, string? initialDirectory) => null;
        public string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory) => null;
        public IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory) => [];
        public string? Prompt(string title, string label, string initialValue) => null;
        public bool? ShowModal(Window dialog) => null;
        public RecentRemovalChoice AskRemoveRecent(string projectName, string path, bool fileExists) => RecentRemovalChoice.Cancel;
    }

    private sealed class FakeTheme : IThemeService
    {
        public AppTheme Current { get; private set; }
        public void Apply(AppTheme theme) => Current = theme;
    }

    private sealed class FakeHardware : IHardwareInfoService
    {
        public HardwareInfo Get(bool refresh = false) => new()
        {
            CpuName = "CPU de teste",
            LogicalProcessors = 16,
            TotalMemoryBytes = 32L << 30,
            AvailableMemoryBytes = 20L << 30,
            Gpus = [new GpuInfo("GPU de teste", 8L << 30, true)],
            Disks = [new DiskInfo("Cache", @"C:\", 100L << 30, 1000L << 30)],
            HardwareEncoders = ["NVIDIA NVENC (GPU): H.264"],
            HardwareDecoding = true,
        };
    }

    private sealed class FakeCache : ICacheMaintenance
    {
        public List<long> Trims { get; } = [];
        public int Clears { get; private set; }

        public IReadOnlyList<CacheUsage> Measure() =>
            [new("Miniaturas, formas de onda e proxies", @"C:\cache\media", 3L << 30, 120), new("Máscaras de IA", @"C:\cache\mattes", 0, 0)];

        public long Trim(long limitBytes, CancellationToken cancellationToken = default)
        {
            lock (Trims) Trims.Add(limitBytes);
            return 1L << 30;
        }

        public long Clear(CancellationToken cancellationToken = default)
        {
            Clears++;
            return 3L << 30;
        }
    }

    private sealed class FakePaths : IAppPaths
    {
        public string RoamingData => @"C:\r";
        public string LocalData => @"C:\l";
        public string Logs => @"C:\l\logs";
        public string Cache => @"C:\l\cache";
        public string Autosave => @"C:\l\autosave";
        public string AiModels => @"C:\l\ai-models";
        public string Backups => @"C:\r\backups";
        public string SettingsFile => @"C:\r\settings.json";
    }

    private static (SettingsViewModel Vm, FakeSettings Settings, FakeTheme Theme, FakeCache Cache, FakeDialogs Dialogs) Create(AppSettings? initial = null)
    {
        var settings = new FakeSettings(initial ?? new AppSettings());
        var theme = new FakeTheme();
        var cache = new FakeCache();
        var dialogs = new FakeDialogs();
        return (new SettingsViewModel(settings, dialogs, theme, new FakeHardware(), cache, new FakePaths()), settings, theme, cache, dialogs);
    }

    [Fact]
    public void Carrega_os_valores_atuais()
    {
        var (vm, _, _, _, _) = Create(new AppSettings
        {
            Theme = AppTheme.Light,
            PreviewQuality = PreviewQualityMode.Quarter,
            PreviewMemoryMegabytes = 2048,
            AutosaveIntervalMinutes = 10,
            AiDevice = AiDevice.Cpu,
            DefaultProjectsDirectory = @"D:\Projetos",
            Shortcuts = new Dictionary<string, string> { [ShortcutCatalog.Marker] = "Ctrl+M" },
        });
        Assert.Equal(AppTheme.Light, vm.Theme.Value);
        Assert.Equal(PreviewQualityMode.Quarter, vm.PreviewQuality.Value);
        Assert.Equal(2048, vm.PreviewMemory.Value);
        Assert.Equal(10, vm.AutosaveMinutes);
        Assert.Equal(AiDevice.Cpu, vm.AiDevice.Value);
        Assert.Equal(@"D:\Projetos", vm.ProjectsDirectory);
        Assert.Equal(SettingsViewModel.DefaultExportDirectory, vm.ExportDirectory);
        var marker = vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Marker);
        Assert.True(marker.IsCustom);
        Assert.Equal("Ctrl+M", marker.Text);
        Assert.Equal(ShortcutCatalog.All.Count, vm.Shortcuts.Count);
    }

    [Fact]
    public async Task Salvar_grava_tudo_e_aplica_o_tema()
    {
        var (vm, settings, theme, _, _) = Create();
        bool? closed = null;
        vm.CloseRequested += (_, saved) => closed = saved;

        vm.Theme = vm.Themes.Single(t => t.Value == AppTheme.Light);
        vm.PreviewQuality = vm.PreviewQualities.Single(q => q.Value == PreviewQualityMode.Full);
        vm.PreviewMemory = vm.PreviewMemories.Single(m => m.Value == 1024);
        vm.UseGpu = false;
        vm.AutosaveMinutes = 7;
        vm.ExportDirectory = @"E:\Saida";
        await vm.SaveCommand.ExecuteAsync(null);

        var s = settings.Current;
        Assert.Equal(1, settings.Saves);
        Assert.Equal(AppTheme.Light, s.Theme);
        Assert.Equal(AppTheme.Light, theme.Current);
        Assert.Equal(PreviewQualityMode.Full, s.PreviewQuality);
        Assert.Equal(1024, s.PreviewMemoryMegabytes);
        Assert.False(s.UseGpuAcceleration);
        Assert.Equal(7, s.AutosaveIntervalMinutes);
        Assert.Equal(@"E:\Saida", s.DefaultExportDirectory);
        Assert.Null(s.DefaultProjectsDirectory); // pasta padrão não é gravada
        Assert.Empty(s.Shortcuts);
        Assert.True(closed);
    }

    [Fact]
    public async Task Atalho_capturado_e_salvo_e_padrao_volta_a_nao_ser_gravado()
    {
        var (vm, settings, _, _, _) = Create();
        var marker = vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Marker);
        marker.Assign(new KeyChord(Key.M, ModifierKeys.Control));
        var save = vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Save);
        save.Assign(new KeyChord(Key.S, ModifierKeys.Control)); // igual ao padrão
        vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Snap).ClearCommand.Execute(null);

        Assert.False(save.IsCustom);
        Assert.Equal("(sem atalho)", vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Snap).Text);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new Dictionary<string, string> { [ShortcutCatalog.Marker] = "Ctrl+M", [ShortcutCatalog.Snap] = "" },
            settings.Current.Shortcuts);
    }

    [Fact]
    public void Conflito_impede_salvar_ate_ser_corrigido()
    {
        var (vm, _, _, _, _) = Create();
        var marker = vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Marker);
        marker.Assign(new KeyChord(Key.S, ModifierKeys.Control));

        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.NotEmpty(vm.ShortcutConflicts);
        Assert.True(marker.HasConflict);
        Assert.Contains("Salvar", marker.Conflict);
        Assert.True(vm.Shortcuts.Single(s => s.Id == ShortcutCatalog.Save).HasConflict);

        marker.ResetCommand.Execute(null);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.False(marker.HasConflict);
        Assert.Empty(vm.ShortcutConflicts);
    }

    [Fact]
    public void Restaurar_padroes_desfaz_todas_as_alteracoes()
    {
        var (vm, _, _, _, _) = Create(new AppSettings
        {
            Shortcuts = new Dictionary<string, string> { [ShortcutCatalog.Marker] = "Ctrl+M", [ShortcutCatalog.Snap] = "" },
        });
        vm.ResetShortcutsCommand.Execute(null);
        Assert.All(vm.Shortcuts, s => Assert.False(s.IsCustom));
    }

    [Theory]
    [InlineData(Key.LeftCtrl, ModifierKeys.Control, false)]
    [InlineData(Key.F5, ModifierKeys.None, true)]
    [InlineData(Key.K, ModifierKeys.Control | ModifierKeys.Windows, true)]
    public void Captura_ignora_modificadores_sozinhos(Key key, ModifierKeys mods, bool captured)
    {
        var chord = SettingsViewModel.Capture(key, mods);
        Assert.Equal(captured, chord is not null);
        if (chord is { } c) Assert.False(c.Modifiers.HasFlag(ModifierKeys.Windows));
    }

    [Fact]
    public async Task Reduzir_o_limite_do_cache_apaga_o_excesso()
    {
        var (vm, _, _, cache, _) = Create(new AppSettings { PreviewCacheGigabytes = 10 });
        vm.CacheGigabytes = 2;
        await vm.SaveCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && cache.Trims.Count == 0; i++) await Task.Delay(10);
        Assert.Equal([2L << 30], cache.Trims);
    }

    [Fact]
    public async Task Cancelar_nao_grava()
    {
        var (vm, settings, _, _, _) = Create();
        bool? closed = null;
        vm.CloseRequested += (_, saved) => closed = saved;
        vm.UseGpu = false;
        vm.CancelCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(0, settings.Saves);
        Assert.False(closed);
    }

    [Fact]
    public async Task Mostra_o_hardware_e_o_uso_do_cache()
    {
        var (vm, _, _, cache, dialogs) = Create();
        await vm.RefreshHardwareAsync();
        Assert.Contains(vm.HardwareRows, r => r.Label == "Processador" && r.Value.Contains("CPU de teste · 16"));
        Assert.Contains(vm.HardwareRows, r => r.Label == "Placa de vídeo" && r.Value.Contains("dedicada"));
        Assert.Contains("alto", vm.PerformanceSummary);
        Assert.Equal(["Nenhuma limitação encontrada."], vm.Limitations);

        await vm.RefreshCacheAsync();
        Assert.Equal("3,0 GB · 120 itens", vm.CacheUsage[0].Value);
        Assert.Equal("vazio", vm.CacheUsage[1].Value);
        Assert.Equal("Em uso: 3,0 GB de 4 GB", vm.CacheTotal);

        dialogs.ConfirmAnswer = false;
        await vm.ClearCacheCommand.ExecuteAsync(null);
        Assert.Equal(0, cache.Clears);
        dialogs.ConfirmAnswer = true;
        await vm.ClearCacheCommand.ExecuteAsync(null);
        Assert.Equal(1, cache.Clears);
        Assert.Contains("3,0 GB liberados", vm.CacheStatus);
    }

    [Fact]
    public void Procurar_pasta_usa_a_escolhida()
    {
        var (vm, _, _, _, dialogs) = Create();
        dialogs.Folder = @"F:\Videos";
        vm.BrowseProjectsCommand.Execute(null);
        Assert.Equal(@"F:\Videos", vm.ProjectsDirectory);
        dialogs.Folder = null;
        vm.BrowseExportCommand.Execute(null);
        Assert.Equal(SettingsViewModel.DefaultExportDirectory, vm.ExportDirectory);
    }
}
