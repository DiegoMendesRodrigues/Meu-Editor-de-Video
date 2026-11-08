using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.App.ViewModels;

public sealed record MissingMediaItem(Guid AssetId, string Name, string OriginalPath, MediaKind Kind)
{
    public string KindLabel => Kind switch
    {
        MediaKind.Video => "Vídeo",
        MediaKind.Audio => "Áudio",
        MediaKind.Image => "Imagem",
        MediaKind.ImageSequence => "Sequência de imagens",
        MediaKind.AnimatedGif => "GIF animado",
        _ => "Mídia",
    };
}

/// <summary>Mídias ausentes ao abrir o projeto (RF016/RF017): localizar uma a uma ou procurar numa pasta.</summary>
public sealed partial class MissingMediaViewModel(IProjectService projects, IDialogService dialogs) : ObservableObject
{
    public ObservableCollection<MissingMediaItem> Items { get; } = [];

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public int Count => Items.Count;

    public event EventHandler? CloseRequested;

    public void Refresh()
    {
        Items.Clear();
        if (projects.Current is not { } session) return;
        foreach (var location in session.Media.Missing)
        {
            if (session.Project.FindMedia(location.AssetId) is { } asset)
                Items.Add(new MissingMediaItem(asset.Id, asset.Name, asset.OriginalPath, asset.Kind));
        }
        OnPropertyChanged(nameof(Count));
        if (Items.Count == 0) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task Locate(MissingMediaItem? item)
    {
        if (item is null) return;
        var ext = Path.GetExtension(item.Name);
        var filter = string.IsNullOrEmpty(ext) ? "Todos os arquivos|*.*" : $"{item.Name}|*{ext}|Todos os arquivos|*.*";
        var dir = Path.GetDirectoryName(item.OriginalPath);
        var path = dialogs.PickFile($"Localizar \"{item.Name}\"", filter, Directory.Exists(dir) ? dir : null);
        if (path is null) return;

        await RunAsync(async () =>
        {
            await projects.RelinkAsync(item.AssetId, path);
            StatusMessage = $"\"{item.Name}\" foi localizada.";
        });
    }

    [RelayCommand]
    private async Task SearchFolder()
    {
        var folder = dialogs.PickFolder("Procurar mídias ausentes nesta pasta (inclui subpastas)", null);
        if (folder is null) return;

        await RunAsync(async () =>
        {
            var found = await projects.RelinkFromFolderAsync(folder);
            StatusMessage = found switch
            {
                0 => "Nenhuma das mídias ausentes foi encontrada nessa pasta.",
                1 => "1 mídia foi localizada.",
                _ => $"{found} mídias foram localizadas.",
            };
        });
    }

    [RelayCommand]
    private void Continue() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (MedException ex)
        {
            dialogs.ShowError(ex.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }
}
