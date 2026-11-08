using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.Controls;
using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.App.ViewModels.Media;

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

    // ---------- Linha do tempo ----------

    private Point _arrasteInicio;
    private MediaItemViewModel? _arrasteItem;

    /// <summary>Guarda o item sob o mouse para iniciar o arraste até a linha do tempo (RF044).</summary>
    private void Biblioteca_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _arrasteInicio = e.GetPosition(null);
        _arrasteItem = ItemSob(e.OriginalSource as DependencyObject);
    }

    private void Biblioteca_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _arrasteItem is not { IsMissing: false } item) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _arrasteInicio.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _arrasteInicio.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _arrasteItem = null;
        var dados = new DataObject(TimelineSurface.MediaDragFormat, item.Id.ToString());
        DragDrop.DoDragDrop((DependencyObject)sender, dados, DragDropEffects.Copy);
    }

    /// <summary>Clique duplo na mídia: adiciona ao fim da linha do tempo.</summary>
    private void Biblioteca_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemSob(e.OriginalSource as DependencyObject) is null) return;
        if (DataContext is EditorViewModel { Timeline: { } tl } && tl.AddSelectedMediaCommand.CanExecute(null))
            tl.AddSelectedMediaCommand.Execute(null);
    }

    private static MediaItemViewModel? ItemSob(DependencyObject? d)
    {
        for (; d is not null and not ListBox; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ListBoxItem { DataContext: MediaItemViewModel item }) return item;
        return null;
    }

    private void AdicionarTrilha_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } botao)
        {
            menu.PlacementTarget = botao;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void RenomearMarcador_Click(object sender, RoutedEventArgs e)
    {
        if (Regua.ContextMarker is { } id && DataContext is EditorViewModel { Timeline: { } tl }) tl.RenameMarker(id);
    }

    private void RemoverMarcador_Click(object sender, RoutedEventArgs e)
    {
        if (Regua.ContextMarker is { } id && DataContext is EditorViewModel { Timeline: { } tl }) tl.RemoveMarker(id);
    }
}
