using Avalonia.Controls;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

public partial class MissingMediaWindow : Window
{
    private bool _closeRequested;

    public MissingMediaWindow() { InitializeComponent(); }

    public MissingMediaWindow(MissingMediaViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        vm.CloseRequested += OnCloseRequested;
        DataContext = vm;
        Opened += (_, _) =>
        {
            if (_closeRequested) Close();
        };
        // Pode disparar CloseRequested aqui (sem mídias ausentes); tratado em OnCloseRequested/Opened.
        vm.Refresh();
    }

    public MissingMediaViewModel? ViewModel { get; }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_closeRequested) return;
        _closeRequested = true;
        if (IsVisible) Close();
    }
}
