using CommunityToolkit.Mvvm.ComponentModel;

namespace MeuEditorDeVideo.App.ViewModels;

/// <summary>Base para páginas exibidas na janela principal.</summary>
public abstract class PageViewModel : ObservableObject
{
    public virtual void OnNavigatedTo()
    {
    }

    public virtual void OnNavigatedFrom()
    {
    }
}
