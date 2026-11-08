using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.App.Views;

public partial class EditorView : UserControl
{
    public EditorView() => InitializeComponent();

    /// <summary>Arrastar arquivos sobre a biblioteca (RF027): aceita somente arquivos do Windows Explorer.</summary>
    private void BibliotecaArea_DragOver(object sender, DragEventArgs e)
    {
        var arquivos = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = arquivos ? DragDropEffects.Copy : DragDropEffects.None;
        DicaSoltar.Visibility = arquivos ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void BibliotecaArea_DragLeave(object sender, DragEventArgs e) => DicaSoltar.Visibility = Visibility.Collapsed;

    private async void BibliotecaArea_Drop(object sender, DragEventArgs e)
    {
        DicaSoltar.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && DataContext is EditorViewModel { Library: { } lib })
            await lib.ImportPathsAsync(files);
    }

    /// <summary>O clique direito também seleciona o item, para que o menu de contexto atue sobre ele.</summary>
    private void Biblioteca_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null and not ListBox; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
                return;
            }
        }
    }
}
