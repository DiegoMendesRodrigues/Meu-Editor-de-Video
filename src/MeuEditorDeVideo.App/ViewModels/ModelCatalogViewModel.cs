using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Modelo do catálogo na ferramenta "Baixar modelos de IA".</summary>
public sealed partial class CatalogItemViewModel(ModelCatalogEntry entry, ModelCatalogViewModel owner) : ObservableObject
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public ModelCatalogEntry Entry { get; } = entry;
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Version => Entry.Version;
    public string Purpose => Entry.Purpose;
    public IReadOnlyList<string> Features => Entry.Features;
    public string Capability => ModelManagerViewModel.CapabilityName(Entry.Capability);
    public string Download => ModelItemViewModel.FormatBytes(Entry.SizeBytes);
    public string Memory => $"{Entry.MemoryMb.ToString("N0", Pt)} MB";
    public string Device => Entry.Gpu ? "GPU (DirectML) ou CPU" : "CPU";
    public string License => Entry.License;
    public string Source => Entry.Source;
    public string Origin => Entry.Url.Host;

    public string Dependencies => Entry.DependsOn.Count == 0
        ? "Nenhuma"
        : string.Join(", ", Entry.DependsOn.Select(id => owner.NameOf(id))) + " (instalado junto)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(ActionText), nameof(IsInstalled))]
    public partial CatalogModelState State { get; set; }

    public bool IsInstalled => State == CatalogModelState.Installed;

    public string StateText => State switch
    {
        CatalogModelState.Installed => "Instalado",
        CatalogModelState.UpdateAvailable => "Versão nova disponível",
        _ => "Não instalado",
    };

    public string ActionText => State switch
    {
        CatalogModelState.Installed => "Reinstalar",
        CatalogModelState.UpdateAvailable => "Atualizar",
        _ => "Instalar",
    };

    [ObservableProperty]
    public partial string Progress { get; set; } = "";

    [RelayCommand]
    private Task InstallAsync() => owner.InstallAsync([Id]);
}

/// <summary>
/// Ferramenta "Baixar modelos de IA": mostra os modelos do catálogo oficial (recursos, tamanho, memória, licença, origem e
/// dependências) e, ao instalar, baixa os arquivos das origens fixadas, confere o SHA-256, gera os pacotes .medmodel e os
/// instala na ordem das dependências — sem terminal.
/// </summary>
public sealed partial class ModelCatalogViewModel : ObservableObject, IDisposable
{
    private readonly CatalogModelInstaller? _installer;
    private readonly IModelRegistry _registry;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ModelCatalogViewModel(IServiceProvider services, IModelRegistry registry, IDialogService dialogs)
    {
        _registry = registry;
        _dialogs = dialogs;
        try
        {
            _installer = (CatalogModelInstaller?)services.GetService(typeof(CatalogModelInstaller));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            Status = "Não foi possível ler o catálogo de modelos: " + ex.Message;
        }
        if (_installer is not null)
        {
            foreach (var entry in _installer.Catalog.Entries) Items.Add(new CatalogItemViewModel(entry, this));
            if (Items.Count == 0) Status = "O catálogo de modelos não foi encontrado na pasta do aplicativo.";
        }
        Selected = Items.FirstOrDefault();
        RefreshStates();
        _registry.Changed += OnRegistryChanged;
    }

    public ObservableCollection<CatalogItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial CatalogItemViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallAllCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>Progresso geral (0–100).</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    public string Notice =>
        "Ao instalar, o aplicativo baixa os arquivos dos modelos das origens oficiais (huggingface.co e github.com), em " +
        "endereços fixos, e confere a assinatura SHA-256 de cada um. Nenhum dado seu é enviado: vídeos, áudios e projetos " +
        "continuam somente neste computador. Os pacotes .medmodel gerados ficam guardados e podem ser copiados para " +
        "instalar em outro computador sem internet.";

    public string PackagesDirectory => _installer?.PackagesDirectory ?? "";

    public string NameOf(string id) => _installer?.Catalog.Find(id)?.Name ?? id;

    private void OnRegistryChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshStates);

    private void RefreshStates()
    {
        if (_installer is null) return;
        foreach (var item in Items) item.State = _installer.StateOf(item.Entry);
        InstallAllCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstallAll() => !IsBusy && Items.Any(i => i.State != CatalogModelState.Installed);

    [RelayCommand(CanExecute = nameof(CanInstallAll))]
    private Task InstallAllAsync() =>
        InstallAsync(Items.Where(i => i.State != CatalogModelState.Installed).Select(i => i.Id).ToList());

    /// <summary>Instala os modelos pedidos e as dependências que faltam, depois de confirmar o download.</summary>
    public async Task InstallAsync(IReadOnlyList<string> ids)
    {
        if (_installer is null || IsBusy || ids.Count == 0) return;
        var plan = _installer.Plan(ids);
        var bytes = _installer.DownloadBytes(plan);
        var names = string.Join("\n", plan.Select(e => $"• {e.Name} ({ModelItemViewModel.FormatBytes(e.SizeBytes)})"
            + (ids.Contains(e.Id) ? "" : " — dependência")));
        var free = FreeSpace(_installer.PackagesDirectory);
        if (free >= 0 && free < bytes * 2 + (200L << 20))
        {
            _dialogs.ShowInfo("Espaço insuficiente", $"São necessários cerca de {ModelItemViewModel.FormatBytes(bytes * 2)} livres no disco " +
                $"para baixar e empacotar os modelos, mas há {ModelItemViewModel.FormatBytes(free)}.");
            return;
        }
        var download = bytes > 0
            ? $"Serão baixados {ModelItemViewModel.FormatBytes(bytes)} das origens oficiais (huggingface.co e github.com)."
            : "Os pacotes já foram baixados antes; nada será baixado.";
        if (!_dialogs.Confirm("Instalar modelos de IA", $"Serão instalados, nesta ordem:\n{names}\n\n{download}\nNenhum dado seu é enviado. Continuar?"))
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        var totalBytes = Math.Max(1, plan.Sum(e => e.SizeBytes));
        var doneBytes = 0L;
        string? current = null;
        var progress = new Progress<ModelInstallProgress>(p =>
        {
            var item = Items.FirstOrDefault(i => i.Id == p.Id);
            if (current != p.Id) { if (current is not null) doneBytes += plan.First(e => e.Id == current).SizeBytes; current = p.Id; }
            var text = p.Stage switch
            {
                ModelInstallStage.Downloading => $"Baixando… {ModelItemViewModel.FormatBytes(p.Bytes)} de {ModelItemViewModel.FormatBytes(p.TotalBytes)}",
                ModelInstallStage.Verifying => "Conferindo SHA-256…",
                ModelInstallStage.Packaging => "Gerando pacote .medmodel…",
                ModelInstallStage.Installing => "Instalando…",
                _ => "Instalado",
            };
            if (item is not null) item.Progress = text;
            Percent = Math.Min(100, (doneBytes + (p.Stage == ModelInstallStage.Downloading ? p.Bytes : p.TotalBytes)) * 100.0 / totalBytes);
            Status = $"{p.Index} de {p.Count}: {p.Name} — {text}";
        });
        try
        {
            await Task.Run(() => _installer.InstallAsync(plan, progress, _cts.Token));
            Percent = 100;
            Status = $"{plan.Count} modelo(s) instalado(s) em {started.Elapsed.TotalSeconds:0} s. Os recursos de IA correspondentes já estão disponíveis.";
        }
        catch (OperationCanceledException)
        {
            Status = "Instalação cancelada. O que já foi baixado é aproveitado na próxima vez.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            // O downloader converte falhas de rede em IOException com mensagem em pt-BR.
            Status = "A instalação parou: " + ex.Message;
        }
        finally
        {
            foreach (var item in Items.Where(i => i.Progress is not "" and not "Instalado")) item.Progress = "";
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            RefreshStates();
        }
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenPackagesFolder()
    {
        if (string.IsNullOrEmpty(PackagesDirectory)) return;
        Directory.CreateDirectory(PackagesDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PackagesDirectory}\"") { UseShellExecute = true });
    }

    private static long FreeSpace(string directory)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    public void Dispose()
    {
        _registry.Changed -= OnRegistryChanged;
        _cts?.Cancel();
    }
}
