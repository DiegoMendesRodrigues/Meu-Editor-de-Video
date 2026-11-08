using System.Windows;
using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.App.Views.Dialogs;

public partial class NewProjectWindow : Window
{
    public NewProjectWindow(NewProjectViewModel vm)
    {
        InitializeComponent();
        vm.CloseRequested += OnCloseRequested;
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private void OnCloseRequested(object? sender, bool result) => DialogResult = result;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }
}
