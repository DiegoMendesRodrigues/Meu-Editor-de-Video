using CommunityToolkit.Mvvm.ComponentModel;
using MeuEditorDeVideo.Presentation.Services;

namespace MeuEditorDeVideo.Presentation.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = AppInfo.ProductName;
}
