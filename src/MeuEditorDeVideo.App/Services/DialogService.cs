using System.Windows;
using MeuEditorDeVideo.Core.Errors;

namespace MeuEditorDeVideo.App.Services;

public interface IDialogService
{
    void ShowError(MedError error);

    void ShowInfo(string title, string message);
}

public sealed class DialogService : IDialogService
{
    public void ShowError(MedError error) =>
        MessageBox.Show(Application.Current?.MainWindow!, $"{error.Message}\n\nCódigo: {error.Code}", AppInfo.ProductName,
            MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(Application.Current?.MainWindow!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
