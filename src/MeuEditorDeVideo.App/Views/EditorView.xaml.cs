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
    public EditorView()
    {
        InitializeComponent();
        PreviewKeyDown += EditorView_PreviewKeyDown;
    }

    /// <summary>Espaço reproduz/pausa e F11 abre a tela cheia, exceto enquanto se digita num campo de texto.</summary>
    private void EditorView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or PasswordBox || Keyboard.Modifiers != ModifierKeys.None) return;
        if (DataContext is not EditorViewModel { Preview: { } preview }) return;
        if (e.Key == Key.Space)
        {
            if (preview.PlayPauseCommand.CanExecute(null)) preview.PlayPauseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            AbrirTelaCheia();
            e.Handled = true;
        }
    }

    private void TelaCheia_Click(object sender, RoutedEventArgs e) => AbrirTelaCheia();

    private Dialogs.MixerWindow? _mixer;

    /// <summary>Abre (ou traz para a frente) a janela não modal do mixer (RF130).</summary>
    private void Mixer_Click(object sender, RoutedEventArgs e)
    {
        if (_mixer is not null)
        {
            _mixer.Activate();
            return;
        }
        if (DataContext is not EditorViewModel { Timeline: { } timeline, Preview: { } preview } editor) return;
        _mixer = new Dialogs.MixerWindow(new ViewModels.Timeline.MixerViewModel(timeline, preview), editor.UndoCommand, editor.RedoCommand)
        {
            Owner = Window.GetWindow(this),
        };
        _mixer.Closed += (_, _) => _mixer = null;
        _mixer.Show();
    }

    private Dialogs.SubtitleEditorWindow? _legendas;

    /// <summary>Abre (ou traz para a frente) o editor de legendas (RF143).</summary>
    private void EditorLegendas_Click(object sender, RoutedEventArgs e)
    {
        if (_legendas is not null)
        {
            _legendas.Activate();
            return;
        }
        if (DataContext is not EditorViewModel { Timeline: { } timeline, TextLibrary: { } library } editor) return;
        _legendas = new Dialogs.SubtitleEditorWindow(new ViewModels.Timeline.SubtitleEditorViewModel(timeline, library, library.Dialogs),
            editor.UndoCommand, editor.RedoCommand)
        {
            Owner = Window.GetWindow(this),
        };
        _legendas.Closed += (_, _) => _legendas = null;
        _legendas.Show();
    }

    /// <summary>Duplo clique num modelo de texto insere-o no cursor.</summary>
    private void ModelosTexto_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ItemsControl)sender, d) is ListBoxItem { DataContext: ViewModels.Timeline.TextPresetItem item }
            && DataContext is EditorViewModel { TextLibrary: { } library })
            library.AddPresetCommand.Execute(item);
    }

    /// <summary>Duplo clique no monitor abre a tela cheia.</summary>
    private void Monitor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) AbrirTelaCheia();
    }

    private void ElementosGraficos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EditorViewModel { GraphicsLibrary: { } g } && g.AddPresetCommand.CanExecute(null)) g.AddPresetCommand.Execute(null);
    }

    private void AbrirTelaCheia()
    {
        if (DataContext is not EditorViewModel { Preview: { HasContent: true } preview }) return;
        var window = new FullScreenPreviewWindow(preview) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    /// <summary>Arrastar arquivos sobre a biblioteca (RF027): aceita somente arquivos do Windows Explorer.</summary>
    private void BibliotecaArea_DragOver(object sender, DragEventArgs e)
    {
        var arquivos = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = arquivos ? DragDropEffects.Copy : DragDropEffects.None;
        DicaSoltar.Visibility = arquivos ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void BibliotecaArea_DragLeave(object sender, DragEventArgs e) => DicaSoltar.Visibility = Visibility.Collapsed;

    /// <summary>Duplo clique num item do catálogo de transições/efeitos/cor aplica ao clipe selecionado.</summary>
    private void ListaEfeitos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.EditorViewModel vm && ListaEfeitos.SelectedItem is ViewModels.Inspector.LibraryEffectItem item)
            vm.ApplySectionItemCommand.Execute(item);
    }

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
