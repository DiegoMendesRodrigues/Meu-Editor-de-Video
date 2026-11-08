using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.ViewModels.Media;

/// <summary>Filtro de tipo aplicado pela seção selecionada no painel A.</summary>
public enum MediaFilter
{
    All,
    Videos,
    Images,
    Audio,
}

/// <summary>Biblioteca de mídias (painel A): importar, organizar, pesquisar e inspecionar (RF021–RF036).</summary>
public sealed partial class MediaLibraryViewModel : ObservableObject, IDisposable
{
    public const string AllCategories = "Todas as categorias";
    public const string NoCategory = "Sem categoria";
    private const int ThumbnailWidth = 240;

    private readonly IProjectService _projects;
    private readonly IMediaImportService _import;
    private readonly IThumbnailProvider _thumbnails;
    private readonly IWaveformProvider _waveforms;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MediaLibraryViewModel> _logger;
    private readonly Dictionary<Guid, MediaItemViewModel> _byId = [];
    private readonly CancellationTokenSource _cts = new();
    private ProjectSession? _session;

    /// <summary>Última pasta usada na importação (durante a sessão do aplicativo).</summary>
    private static string? s_lastImportDirectory;

    public MediaLibraryViewModel(
        IProjectService projects,
        IMediaImportService import,
        IThumbnailProvider thumbnails,
        IWaveformProvider waveforms,
        IDialogService dialogs,
        ILogger<MediaLibraryViewModel> logger)
    {
        _projects = projects;
        _import = import;
        _thumbnails = thumbnails;
        _waveforms = waveforms;
        _dialogs = dialogs;
        _logger = logger;

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = Matches;
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(MediaItemViewModel.Name), ListSortDirection.Ascending));

        _projects.SessionChanged += OnSessionChanged;
        Attach();
    }

    public ObservableCollection<MediaItemViewModel> Items { get; } = [];

    /// <summary>Itens após filtro de seção, categoria e pesquisa (ligar a lista a esta visão).</summary>
    public ICollectionView ItemsView { get; }

    public ObservableCollection<string> Categories { get; } = [AllCategories];

    [ObservableProperty]
    public partial MediaFilter Filter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = AllCategories;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand), nameof(RenameSelectedCommand), nameof(SetCategoryCommand), nameof(ClearCategoryCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial MediaItemViewModel? SelectedItem { get; set; }

    public bool HasSelection => SelectedItem is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportFilesCommand), nameof(ImportSequenceCommand))]
    public partial bool IsImporting { get; set; }

    /// <summary>"Importando 3 de 10: arquivo.mp4".</summary>
    [ObservableProperty]
    public partial string ImportStatus { get; set; } = "";

    /// <summary>Progresso da importação (0–100).</summary>
    [ObservableProperty]
    public partial double ImportPercent { get; set; }

    /// <summary>Quantidade de itens visíveis após os filtros.</summary>
    [ObservableProperty]
    public partial int VisibleCount { get; set; }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Texto para lista vazia: biblioteca vazia ou nada encontrado na pesquisa.</summary>
    public string EmptyMessage => Items.Count == 0
        ? "Nenhuma mídia importada. Arraste arquivos ou pastas para cá ou use \"Importar Mídia\"."
        : "Nenhuma mídia corresponde à pesquisa.";

    partial void OnFilterChanged(MediaFilter value) => Refilter();
    partial void OnSearchTextChanged(string value) => Refilter();
    partial void OnSelectedCategoryChanged(string value) => Refilter();

    private void Refilter()
    {
        ItemsView.Refresh();
        VisibleCount = ItemsView.Cast<object>().Count();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private bool Matches(object o)
    {
        if (o is not MediaItemViewModel item) return false;

        var kindOk = Filter switch
        {
            MediaFilter.Videos => item.Kind == MediaKind.Video,
            MediaFilter.Images => item.Kind is MediaKind.Image or MediaKind.ImageSequence or MediaKind.AnimatedGif,
            MediaFilter.Audio => item.Kind == MediaKind.Audio,
            _ => true,
        };
        if (!kindOk) return false;

        var categoryOk = SelectedCategory switch
        {
            AllCategories or null => true,
            NoCategory => item.Category is null,
            var c => string.Equals(item.Category, c, StringComparison.CurrentCultureIgnoreCase),
        };
        if (!categoryOk) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        // Pesquisa sem diferenciar maiúsculas nem acentos (RF034).
        var needle = Normalize(SearchText.Trim());
        return Normalize(item.Name).Contains(needle, StringComparison.Ordinal)
            || (item.Category is { } cat && Normalize(cat).Contains(needle, StringComparison.Ordinal));
    }

    private static string Normalize(string s)
    {
        var decomposed = s.ToLower(CultureInfo.CurrentCulture).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    // ── Sincronização com o projeto ──────────────────────────────────────────

    private void OnSessionChanged(object? sender, EventArgs e) => Attach();

    private void OnHistoryChanged(object? sender, EventArgs e) => Sync();

    private void Attach()
    {
        if (!ReferenceEquals(_session, _projects.Current))
        {
            if (_session is not null) _session.History.Changed -= OnHistoryChanged;
            _session = _projects.Current;
            if (_session is not null) _session.History.Changed += OnHistoryChanged;
        }
        Sync();
    }

    /// <summary>Reflete o estado do projeto mantendo os itens existentes (e suas miniaturas).</summary>
    private void Sync()
    {
        var media = _session?.Project.Media ?? [];
        var missing = _session?.Media.Missing.Select(m => m.AssetId).ToHashSet() ?? [];
        var current = media.Select(m => m.Id).ToHashSet();

        foreach (var gone in _byId.Keys.Where(id => !current.Contains(id)).ToList())
        {
            Items.Remove(_byId[gone]);
            _byId.Remove(gone);
        }

        foreach (var asset in media)
        {
            if (_byId.TryGetValue(asset.Id, out var item))
            {
                if (!ReferenceEquals(item.Asset, asset)) item.Asset = asset;
                item.IsMissing = missing.Contains(asset.Id);
            }
            else
            {
                item = new MediaItemViewModel(asset, missing.Contains(asset.Id));
                _byId[asset.Id] = item;
                Items.Add(item);
            }
            if (!item.IsMissing && !item.PreviewRequested) _ = LoadPreviewAsync(item);
        }

        var categories = media.Select(m => m.Category).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var wanted = new List<string> { AllCategories };
        if (categories.Count > 0) wanted.Add(NoCategory);
        wanted.AddRange(categories);
        if (!Categories.SequenceEqual(wanted))
        {
            var selected = SelectedCategory;
            Categories.Clear();
            foreach (var c in wanted) Categories.Add(c);
            SelectedCategory = wanted.Contains(selected) ? selected : AllCategories;
        }

        if (SelectedItem is not null && !_byId.ContainsKey(SelectedItem.Id)) SelectedItem = null;
        OnPropertyChanged(nameof(IsEmpty));
        Refilter();
    }

    private async Task LoadPreviewAsync(MediaItemViewModel item)
    {
        item.PreviewRequested = true;
        var path = _session?.Media.Locations.GetValueOrDefault(item.Id)?.ResolvedPath;
        if (path is null) { item.PreviewRequested = false; return; }

        try
        {
            var asset = item.Asset;
            // Quadro representativo: 1 s ou 10% da duração, o que for menor.
            var at = asset.Kind == MediaKind.Image
                ? MediaTime.Zero
                : MediaTime.Min(MediaTime.FromSeconds(1), new MediaTime(asset.Duration.Ticks / 10));

            var thumbTask = asset.HasVideo ? _thumbnails.GetThumbnailAsync(asset, path, at, ThumbnailWidth, _cts.Token) : Task.FromResult<BgraImage?>(null);
            var waveTask = asset.Kind == MediaKind.Audio ? _waveforms.GetWaveformAsync(asset, path, _cts.Token) : Task.FromResult<WaveformData?>(null);
            await Task.WhenAll(thumbTask, waveTask);
            item.SetPreview(thumbTask.Result, waveTask.Result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (MedException ex)
        {
            item.PreviewFailed = true;
            _logger.LogWarning("Pré-visualização indisponível: {Code}", ex.Error.Code);
        }
    }

    // ── Importação ───────────────────────────────────────────────────────────

    private bool CanImport() => !IsImporting && _projects.Current is not null;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportFiles()
    {
        var files = _dialogs.PickFiles("Importar mídias", MediaFormats.OpenFileFilter, s_lastImportDirectory);
        if (files.Count == 0) return;
        s_lastImportDirectory = Path.GetDirectoryName(files[0]);
        await ImportPathsAsync(files);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportSequence()
    {
        var file = _dialogs.PickFile("Importar sequência de imagens — escolha qualquer imagem da sequência",
            "Imagens|" + string.Join(";", MediaFormats.Image.Select(e => "*" + e)), s_lastImportDirectory);
        if (file is null) return;
        s_lastImportDirectory = Path.GetDirectoryName(file);

        await RunImportAsync(() => _import.ImportSequenceAsync(file, cancellationToken: _cts.Token), "Analisando a sequência…");
    }

    /// <summary>Importa arquivos/pastas soltos na biblioteca (RF027) ou escolhidos na janela.</summary>
    public Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || IsImporting || _projects.Current is null) return Task.CompletedTask;
        var category = SelectedCategory is AllCategories or NoCategory ? null : SelectedCategory;
        var progress = new Progress<ImportProgress>(p =>
        {
            ImportPercent = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
            ImportStatus = p.Total == 0 ? "Preparando…" : $"Importando {p.Done} de {p.Total}" + (p.CurrentFile is { } f ? $": {f}" : "");
        });
        return RunImportAsync(() => _import.ImportAsync(paths, category, progress, _cts.Token), "Preparando…");
    }

    private async Task RunImportAsync(Func<Task<MediaImportResult>> import, string status)
    {
        IsImporting = true;
        ImportPercent = 0;
        ImportStatus = status;
        try
        {
            var result = await import();
            Report(result);
            if (result.Imported.Count > 0 && _byId.TryGetValue(result.Imported[^1].Id, out var last)) SelectedItem = last;
        }
        catch (MedException ex)
        {
            _dialogs.ShowError(ex.Error);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsImporting = false;
            ImportStatus = "";
        }
    }

    private void Report(MediaImportResult result)
    {
        if (result.Failed.Count == 0 && result.SkippedDuplicates == 0) return;

        var sb = new StringBuilder();
        if (result.Imported.Count > 0) sb.AppendLine($"{result.Imported.Count} mídia(s) importada(s).");
        if (result.SkippedDuplicates > 0) sb.AppendLine($"{result.SkippedDuplicates} arquivo(s) já estava(m) no projeto e foi(ram) ignorado(s).");
        if (result.Failed.Count > 0)
        {
            sb.AppendLine().AppendLine($"{result.Failed.Count} arquivo(s) não puderam ser importados:");
            foreach (var f in result.Failed.Take(12)) sb.AppendLine($"• {f.FileName}: {f.Reason}");
            if (result.Failed.Count > 12) sb.AppendLine($"… e mais {result.Failed.Count - 12}.");
        }
        _dialogs.ShowInfo("Importar mídias", sb.ToString().TrimEnd());
    }

    // ── Organização ──────────────────────────────────────────────────────────

    private bool CanEditSelection() => SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void RemoveSelected()
    {
        if (SelectedItem is not { } item) return;
        if (!_dialogs.Confirm("Remover mídia", $"Remover \"{item.Name}\" do projeto?\n\nO arquivo original não será apagado.")) return;
        Execute(new RemoveMediaAssetCommand(item.Id));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void RenameSelected()
    {
        if (SelectedItem is not { } item) return;
        var name = _dialogs.Prompt("Renomear mídia", "Novo nome na biblioteca (o arquivo original não é alterado):", item.Name);
        if (name is not null && name.Trim() != item.Name) Execute(new RenameMediaCommand(item.Id, name));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void SetCategory()
    {
        if (SelectedItem is not { } item) return;
        var name = _dialogs.Prompt("Categoria", "Nome da categoria (ex.: Entrevistas, Trilhas, Logotipos):", item.Category ?? "");
        if (name is null) return;
        Execute(new SetMediaCategoryCommand([item.Id], string.IsNullOrWhiteSpace(name) ? null : name));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void ClearCategory()
    {
        if (SelectedItem is { Category: not null } item) Execute(new SetMediaCategoryCommand([item.Id], null));
    }

    private void Execute(IEditCommand command)
    {
        try
        {
            _projects.Execute(command);
        }
        catch (EditRejectedException ex)
        {
            _dialogs.ShowInfo("Não foi possível concluir", ex.Message);
        }
    }

    public void Dispose()
    {
        _projects.SessionChanged -= OnSessionChanged;
        if (_session is not null) _session.History.Changed -= OnHistoryChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
