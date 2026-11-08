using CommunityToolkit.Mvvm.ComponentModel;
using MeuEditorDeVideo.App.Services;

namespace MeuEditorDeVideo.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = AppInfo.ProductName;
}
