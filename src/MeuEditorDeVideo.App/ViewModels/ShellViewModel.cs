using CommunityToolkit.Mvvm.ComponentModel;
using MeuEditorDeVideo.App.Services;

namespace MeuEditorDeVideo.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    public string Title => AppInfo.ProductName;
}
