using System.Windows;
using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.App.Views.Dialogs;

public partial class MissingMediaWindow : Window
{
    private bool _closeRequested;

    public MissingMediaWindow(MissingMediaViewModel vm)
    {
        InitializeComponent();
        vm.CloseRequested += OnCloseRequested;
        DataContext = vm;
        Loaded += OnLoaded;
        // Pode disparar CloseRequested aqui (sem mídias ausentes); tratado em OnCloseRequested/OnLoaded.
        vm.Refresh();
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_closeRequested) return;
        _closeRequested = true;
        if (IsLoaded) Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_closeRequested) Close();
    }
}
