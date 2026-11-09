using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Linha de um modelo instalado com todos os campos do manifesto (§8.3) e a verificação de memória (RF168).</summary>
public sealed partial class ModelItemViewModel(InstalledModel model, ulong availableMemory) : ObservableObject
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public InstalledModel Model { get; } = model;
    public ModelManifest Manifest => Model.Manifest;
    public string Name => Manifest.Name;
    public string Id => Manifest.Id;
    public string Version => Manifest.Version;
    public string Source => Manifest.Source;
    public string Purpose => Manifest.Purpose;
    public string Capability => ModelManagerViewModel.CapabilityName(Manifest.Capability);
    public string Format => Manifest.Format;
    public string Size => FormatBytes(Manifest.SizeBytes > 0 ? Manifest.SizeBytes : FileSize());
    public string Memory => $"{Manifest.MemoryMb.ToString("N0", Pt)} MB";
    public string Cpu => Manifest.Cpu ? "Sim" : "Não";
    public string Gpu => Manifest.Gpu ? "Sim (DirectML)" : "Não";
    public string License => Manifest.License;
    public string Hash => Manifest.Sha256;
    public string Location => Model.ModelPath;
    public bool BuiltIn => Model.BuiltIn;
    public string Installation => Model.BuiltIn ? "Incluído no aplicativo (não pode ser removido)" : "Instalado pelo usuário (pacote .medmodel)";
    public string Subtitle => Model.BuiltIn ? $"{Capability} · incluído no aplicativo" : Capability;

    /// <summary>Aviso quando a memória livre do computador é menor que a exigida.</summary>
    public string MemoryCheck => availableMemory == 0 ? ""
        : (ulong)Manifest.MemoryMb * 1024 * 1024 > availableMemory
            ? $"Atenção: requer {Memory}, mas há só {FormatBytes((long)availableMemory)} livres agora."
            : $"Memória suficiente ({FormatBytes((long)availableMemory)} livres).";

    public bool MemoryWarning => availableMemory > 0 && (ulong)Manifest.MemoryMb * 1024 * 1024 > availableMemory;

    [ObservableProperty]
    public partial string Integrity { get; set; } = "Não verificado";

    /// <summary>Documentação de uso que acompanha o pacote.</summary>
    public string Documentation
    {
        get
        {
            try
            {
                return Model.DocumentationPath is { } p && File.Exists(p) ? File.ReadAllText(p) : "Este pacote não traz documentação.";
            }
            catch (IOException)
            {
                return "Não foi possível ler a documentação.";
            }
        }
    }

    private long FileSize()
    {
        try
        {
            return File.Exists(Model.ModelPath) ? new FileInfo(Model.ModelPath).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", Pt) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", Pt) + " MB",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", Pt) + " KB",
        _ => bytes + " bytes",
    };
}

/// <summary>Situação de um recurso de IA: disponível com qual modelo, ou indisponível.</summary>
public sealed record CapabilityStatus(string Feature, string Status, bool Available);

/// <summary>
/// Gerenciador de modelos de IA (RF166–RF170): lista os modelos instalados com todos os dados do manifesto, instala
/// pacotes .medmodel locais (com verificação de hash), remove, verifica integridade e escolhe CPU ou GPU.
/// Nenhum modelo é baixado: a instalação é sempre a partir de arquivos no computador.
/// </summary>
public sealed partial class ModelManagerViewModel : ObservableObject, IDisposable
{
    private readonly IModelRegistry _registry;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;

    public ModelManagerViewModel(IModelRegistry registry, ISettingsService settings, IDialogService dialogs)
    {
        _registry = registry;
        _settings = settings;
        _dialogs = dialogs;
        UseGpu = settings.Current.AiDevice == AiDevice.Gpu;
        _registry.Changed += OnChanged;
        Load();
    }

    public ObservableCollection<ModelItemViewModel> Models { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CapabilityStatus> Capabilities { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(VerifyCommand))]
    public partial ModelItemViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial bool UseGpu { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(RemoveCommand), nameof(VerifyCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public string MemorySummary => AvailableMemory() is var free and > 0
        ? $"Memória livre agora: {ModelItemViewModel.FormatBytes((long)free)}."
        : "";

    public string Privacy => "Os modelos rodam somente neste computador. Nenhum arquivo, áudio ou vídeo é enviado para a internet.";

    public static string CapabilityName(ModelCapability c) => c switch
    {
        ModelCapability.PersonSegmentation => "Segmentação de pessoas (remover fundo, rastrear pessoa)",
        ModelCapability.SpeechRecognition => "Reconhecimento de fala (legendas automáticas)",
        ModelCapability.SpeechEnhancement => "Isolamento de voz e redução de ruído",
        ModelCapability.VoiceActivity => "Detecção de fala (remover silêncios)",
        ModelCapability.SceneDetection => "Detecção de cenas",
        ModelCapability.ImageEnhancement => "Melhoria de imagem",
        _ => c.ToString(),
    };

    private void OnChanged(object? sender, EventArgs e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Load);

    private void Load()
    {
        var id = Selected?.Id;
        var free = AvailableMemory();
        Models.Clear();
        foreach (var m in _registry.Installed.OrderBy(m => m.Manifest.Capability).ThenBy(m => m.Manifest.Name))
            Models.Add(new ModelItemViewModel(m, free));
        Selected = Models.FirstOrDefault(m => m.Id == id) ?? Models.FirstOrDefault();
        Capabilities = Enum.GetValues<ModelCapability>().Select(c => _registry.Find(c) is { } m
                ? new CapabilityStatus(CapabilityName(c), $"Disponível · {m.Manifest.Name} {m.Manifest.Version}", true)
                : new CapabilityStatus(CapabilityName(c), c == ModelCapability.SceneDetection
                    ? "Sem modelo: usa comparação de cores (menos precisa)"
                    : "Indisponível — instale um pacote .medmodel", false))
            .ToList();
        OnPropertyChanged(nameof(MemorySummary));
    }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task InstallAsync()
    {
        var path = _dialogs.PickFile("Instalar modelo de IA", "Pacote de modelo (*.medmodel)|*.medmodel", null);
        if (path is null) return;
        IsBusy = true;
        Status = "Verificando e instalando o pacote…";
        try
        {
            var model = await _registry.InstallAsync(path);
            Status = $"\"{model.Manifest.Name}\" instalado (hash verificado).";
            Selected = Models.FirstOrDefault(m => m.Id == model.Manifest.Id) ?? Selected;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Status = "";
            _dialogs.ShowInfo("Instalar modelo de IA", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool HasSelection() => !IsBusy && Selected is not null;

    private bool CanRemove() => HasSelection() && Selected?.BuiltIn == false;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        if (Selected is not { } item) return;
        if (!_dialogs.Confirm("Remover modelo", $"Remover \"{item.Name}\"? Os recursos que dependem dele ficarão indisponíveis.")) return;
        IsBusy = true;
        try
        {
            await _registry.RemoveAsync(item.Id);
            Status = $"\"{item.Name}\" removido.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowInfo("Remover modelo", $"Não foi possível remover: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task VerifyAsync()
    {
        if (Selected is not { } item) return;
        IsBusy = true;
        Status = "Calculando o hash SHA-256…";
        try
        {
            var ok = await _registry.VerifyAsync(item.Id);
            item.Integrity = ok ? "Íntegro (SHA-256 confere)" : "Corrompido ou ausente — reinstale o pacote";
            Status = item.Integrity;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnUseGpuChanged(bool value)
    {
        var device = value ? AiDevice.Gpu : AiDevice.Cpu;
        if (_settings.Current.AiDevice == device) return;
        _ = _settings.SaveAsync(_settings.Current with { AiDevice = device });
        Status = value ? "Os modelos compatíveis usarão a GPU (DirectML), com retorno automático para a CPU."
            : "Os modelos usarão somente a CPU.";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    /// <summary>Memória física livre (bytes), ou 0 se não for possível medir.</summary>
    public static ulong AvailableMemory()
    {
        var s = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref s) ? s.AvailPhys : 0;
    }

    public void Dispose() => _registry.Changed -= OnChanged;
}
