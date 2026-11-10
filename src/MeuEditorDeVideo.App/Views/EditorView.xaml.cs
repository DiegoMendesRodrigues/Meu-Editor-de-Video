using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.Controls;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels;
using MeuEditorDeVideo.App.ViewModels.Media;

namespace MeuEditorDeVideo.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        // Os atalhos ficam na janela: funcionam mesmo com o foco fora do editor (ex.: depois de fechar um menu).
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is not { } window || ReferenceEquals(window, _host)) return;
            Unhook();
            _host = window;
            window.PreviewKeyDown += EditorView_PreviewKeyDown;
            window.KeyDown += EditorView_KeyDown;
        };
        Unloaded += (_, _) => Unhook();
    }

    private Window? _host;

    private void Unhook()
    {
        if (_host is null) return;
        _host.PreviewKeyDown -= EditorView_PreviewKeyDown;
        _host.KeyDown -= EditorView_KeyDown;
        _host = null;
    }

    private static ShortcutService? Shortcuts =>
        System.Windows.Application.Current is App ? App.Services.GetService(typeof(ShortcutService)) as ShortcutService : null;

    private static Key RealKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    /// <summary>
    /// Reproduzir/pausar e tela cheia (RF212) antes dos controles, para que o Espaço não acione o botão em foco — exceto
    /// enquanto se digita num campo de texto.
    /// </summary>
    private void EditorView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or PasswordBox) return;
        if (DataContext is not EditorViewModel { Preview: { } preview }) return;
        switch (Shortcuts?.Find(RealKey(e), Keyboard.Modifiers))
        {
            case ShortcutCatalog.PlayPause:
                if (preview.PlayPauseCommand.CanExecute(null)) preview.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case ShortcutCatalog.FullScreen:
                AbrirTelaCheia();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Atalhos gerais do editor (RF212), depois dos controles: uma caixa de texto em foco trata primeiro as próprias
    /// teclas (ex.: Ctrl+Z desfaz a digitação). Os atalhos da linha do tempo ficam no <see cref="TimelineControl"/>.
    /// </summary>
    private void EditorView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not EditorViewModel vm) return;
        var action = Shortcuts?.Find(RealKey(e), Keyboard.Modifiers);
        // Teclas sem modificador não disparam ações gerais enquanto se digita.
        if (action is null || (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is TextBox or PasswordBox)) return;
        if (action == ShortcutCatalog.Settings)
        {
            AbrirConfiguracoes();
            e.Handled = true;
            return;
        }
        System.Windows.Input.ICommand? command = action switch
        {
            ShortcutCatalog.NewProject => vm.NewProjectCommand,
            ShortcutCatalog.OpenProject => vm.OpenCommand,
            ShortcutCatalog.Save => vm.SaveCommand,
            ShortcutCatalog.SaveAs => vm.SaveAsCommand,
            ShortcutCatalog.Import => vm.ImportMediaCommand,
            ShortcutCatalog.Export => vm.ExportCommand,
            ShortcutCatalog.Undo => vm.UndoCommand,
            ShortcutCatalog.Redo => vm.RedoCommand,
            _ => null,
        };
        if (command is null) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    private void Configuracoes_Click(object sender, RoutedEventArgs e) => AbrirConfiguracoes();

    /// <summary>Informações do aplicativo (RF215).</summary>
    private void Sobre_Click(object sender, RoutedEventArgs e) => Dialogs.AboutWindow.ShowFor(Window.GetWindow(this));

    /// <summary>Configurações do aplicativo (RF208–RF214).</summary>
    private void AbrirConfiguracoes() => Dialogs.SettingsWindow.ShowFor(Window.GetWindow(this));

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
    private Dialogs.ModelManagerWindow? _modelos;

    /// <summary>Gerenciador de modelos de IA (RF166–RF170).</summary>
    private void ModelosIA_Click(object sender, RoutedEventArgs e)
    {
        if (_modelos is not null)
        {
            _modelos.Activate();
            return;
        }
        var vm = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ViewModels.ModelManagerViewModel>(App.Services);
        _modelos = new Dialogs.ModelManagerWindow(vm) { Owner = Window.GetWindow(this) };
        _modelos.Closed += (_, _) => _modelos = null;
        _modelos.Show();
    }

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
