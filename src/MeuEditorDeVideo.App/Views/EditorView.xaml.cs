using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeuEditorDeVideo.App.Controls;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Media;

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
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is EditorViewModel old) old.PropertyChanged -= Editor_PropertyChanged;
            if (e.NewValue is EditorViewModel vm) vm.PropertyChanged += Editor_PropertyChanged;
            ObservarLinhaDoTempo();
        };
    }

    private Presentation.ViewModels.Timeline.TimelineViewModel? _linhaDoTempo;
    private const string RecentTag = "recente";

    /// <summary>
    /// Arquivo: depois do separador abaixo de "Abrir…", os 5 projetos recentes (_1 a _5, caminho na dica) e mais um
    /// separador. Sem projetos recentes, só o separador original.
    /// </summary>
    private void MenuArquivo_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MenuArquivo) || DataContext is not EditorViewModel vm) return;
        foreach (var old in MenuArquivo.Items.OfType<FrameworkElement>().Where(i => Equals(i.Tag, RecentTag)).ToList())
            MenuArquivo.Items.Remove(old);

        var recent = vm.RecentProjectsForMenu();
        if (recent.Count == 0) return;
        var index = MenuArquivo.Items.IndexOf(SeparadorRecentes) + 1;
        for (var i = 0; i < recent.Count; i++)
        {
            var path = recent[i];
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var item = new MenuItem
            {
                Header = $"_{i + 1}  {name.Replace("_", "__", StringComparison.Ordinal)}",
                ToolTip = path,
                Tag = RecentTag,
                Command = vm.OpenRecentCommand,
                CommandParameter = path,
            };
            AutomationProperties.SetName(item, name);
            MenuArquivo.Items.Insert(index++, item);
        }
        MenuArquivo.Items.Insert(index, new Separator { Tag = RecentTag });
    }

    private void Editor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Timeline)) ObservarLinhaDoTempo();
    }

    // ---------- Reordenar trilhas arrastando o cabeçalho ----------

    private const string FormatoTrilha = "MeuEditorDeVideo.Trilha";
    private Point? _inicioArrasteTrilha;

    private void Trilha_MouseDown(object sender, MouseButtonEventArgs e) =>
        // Botões do cabeçalho (ocultar, silenciar, bloquear) continuam só clicáveis.
        _inicioArrasteTrilha = e.OriginalSource is DependencyObject d && AcimaDe<System.Windows.Controls.Primitives.ButtonBase>(d) is null
            ? e.GetPosition(this) : null;

    private void Trilha_MouseMove(object sender, MouseEventArgs e)
    {
        if (_inicioArrasteTrilha is not { } inicio || e.LeftButton != MouseButtonState.Pressed
            || sender is not FrameworkElement { DataContext: Presentation.ViewModels.Timeline.TrackHeaderViewModel trilha } origem) return;
        if (Math.Abs(e.GetPosition(this).Y - inicio.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _inicioArrasteTrilha = null;
        DragDrop.DoDragDrop(origem, new DataObject(FormatoTrilha, trilha.Id), DragDropEffects.Move);
    }

    private void Trilha_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(FormatoTrilha) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Trilha_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(FormatoTrilha) is not Guid id || _linhaDoTempo is not { } tl
            || sender is not FrameworkElement { DataContext: Presentation.ViewModels.Timeline.TrackHeaderViewModel alvo }) return;
        e.Handled = true;
        var destino = tl.Tracks.IndexOf(alvo);
        var origem = tl.Tracks.ToList().FindIndex(t => t.Id == id);
        if (origem < 0 || destino < 0 || origem == destino) return;
        tl.MoveTrack(id, destino);
    }

    private static T? AcimaDe<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }

    private void ObservarLinhaDoTempo()
    {
        if (_linhaDoTempo is not null)
        {
            _linhaDoTempo.RevealTopRequested -= RevelarTopo;
            _linhaDoTempo.ClipInserted -= AbrirAbaDoClipeInserido;
        }
        _linhaDoTempo = (DataContext as EditorViewModel)?.Timeline;
        if (_linhaDoTempo is not null)
        {
            _linhaDoTempo.RevealTopRequested += RevelarTopo;
            _linhaDoTempo.ClipInserted += AbrirAbaDoClipeInserido;
        }
    }

    /// <summary>
    /// Ao inserir um clipe, abre a aba de propriedades correspondente: texto → Texto; vídeo, imagem, figurinha ou
    /// forma → Vídeo; só áudio → Áudio.
    /// </summary>
    private void AbrirAbaDoClipeInserido(object? sender, Guid clipId)
    {
        if (_linhaDoTempo?.Sequence is not { } seq) return;
        foreach (var track in seq.Tracks)
        {
            if (track.Clips.Find(c => c.Id == clipId) is not { } clip) continue;
            var aba = clip.IsText ? AbaTexto : track.IsVisual ? AbaVideo : AbaAudio;
            // Depois da seleção ser aplicada aos inspetores (eles escutam a mesma mudança de seleção).
            Dispatcher.BeginInvoke(() => aba.IsSelected = true, System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
    }

    private void RevelarTopo(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => RolagemTrilhas.ScrollToTop(), System.Windows.Threading.DispatcherPriority.Loaded);

    private Window? _host;

    private GridLength _larguraBiblioteca = new(560);
    private GridLength _alturaLinhaDoTempo = new(300);

    /// <summary>
    /// Maximiza ou restaura o monitor ("Linha do tempo"): maximizado, ele cobre a biblioteca à esquerda e a linha do
    /// tempo embaixo; o painel Propriedades continua visível. Restaurar volta aos tamanhos anteriores.
    /// </summary>
    // ---------------------------------------------------------------- Visualizar › Modo do layout

    private string _modoLayout = "Padrao";
    private GridLength _larguraPropriedades = new(400);
    private GridLength _larguraBibliotecaLayout = new(560);

    private void ModoLayout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string modo }) AplicarLayout(modo);
    }

    /// <summary>
    /// Padrão: biblioteca e monitor em cima, linha do tempo embaixo, Propriedades à direita em altura total.
    /// Linha do tempo: igual, mas a linha do tempo ocupa toda a largura da janela.
    /// Vídeo: o monitor ocupa a metade direita da janela, em altura total; biblioteca e Propriedades ficam em cima, à
    /// esquerda, com a linha do tempo embaixo.
    /// </summary>
    public void AplicarLayout(string modo)
    {
        if (BotaoMaximizarMonitor.IsChecked == true) BotaoMaximizarMonitor.IsChecked = false;
        foreach (var item in MenuModoLayout.Items.OfType<MenuItem>()) item.IsChecked = (string)item.Tag == modo;
        if (modo == _modoLayout) return;

        // Volta ao padrão.
        if (_modoLayout == "Video")
        {
            AreaCentral.Children.Remove(PainelPropriedades);
            GradeEditor.Children.Remove(PainelMonitor);
            Posicionar(PainelMonitor, 0, 1, 2);
            AreaCentral.Children.Add(PainelMonitor);
            Posicionar(PainelPropriedades, 1, 3, 2);
            GradeEditor.Children.Add(PainelPropriedades);
            ColunaPrincipal.Width = new GridLength(1, GridUnitType.Star);
            ColunaPrincipal.MinWidth = 760;
            ColunaPropriedades.Width = _larguraPropriedades;
            ColunaPropriedades.MinWidth = 340;
            ColunaBiblioteca.Width = _larguraBibliotecaLayout;
        }
        else if (_modoLayout != "Video")
        {
            _larguraPropriedades = ColunaPropriedades.Width;
        }
        Grid.SetColumnSpan(AreaInferior, 1);
        Grid.SetColumnSpan(DivisorCentral, 1);
        Grid.SetRowSpan(PainelPropriedades, 3);
        Grid.SetRowSpan(DivisorPropriedades, 3);

        if (modo == "LinhaDoTempo")
        {
            Grid.SetColumnSpan(AreaInferior, 3);
            Grid.SetColumnSpan(DivisorCentral, 3);
            Grid.SetRowSpan(PainelPropriedades, 1);
            Grid.SetRowSpan(DivisorPropriedades, 1);
        }
        else if (modo == "Video")
        {
            // Troca de lugar: Propriedades vai para a área de cima (ao lado da biblioteca) e o monitor para a direita.
            AreaCentral.Children.Remove(PainelMonitor);
            GradeEditor.Children.Remove(PainelPropriedades);
            Posicionar(PainelPropriedades, 0, 1, 2);
            AreaCentral.Children.Add(PainelPropriedades);
            Posicionar(PainelMonitor, 1, 3, 2);
            GradeEditor.Children.Add(PainelMonitor);
            ColunaPrincipal.MinWidth = 600;
            ColunaPrincipal.Width = new GridLength(1, GridUnitType.Star);
            ColunaPropriedades.MinWidth = 400;
            ColunaPropriedades.Width = new GridLength(1, GridUnitType.Star);
            // Biblioteca e Propriedades dividem a metade esquerda.
            _larguraBibliotecaLayout = ColunaBiblioteca.Width;
            ColunaBiblioteca.Width = new GridLength(1, GridUnitType.Star);
        }
        _modoLayout = modo;
    }

    private static void Posicionar(UIElement e, int linha, int linhas, int coluna)
    {
        Grid.SetRow(e, linha);
        Grid.SetRowSpan(e, linhas);
        Grid.SetColumn(e, coluna);
        Grid.SetColumnSpan(e, 1);
    }

    // ---------------------------------------------------------------- Visualizar › Qualidade, zoom e régua

    private void MenuQualidade_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel { Preview: { } preview }) return;
        MenuQualidade.Items.Clear();
        foreach (var opcao in preview.Qualities)
        {
            var item = new MenuItem { Header = opcao.Label, IsCheckable = true, IsChecked = ReferenceEquals(opcao, preview.Quality) };
            AutomationProperties.SetName(item, "Qualidade " + opcao.Label);
            item.Click += (_, _) => preview.Quality = opcao;
            MenuQualidade.Items.Add(item);
        }
    }

    private double _zoomVideo;

    private void ZoomVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        _zoomVideo = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var item in MenuZoomVideo.Items.OfType<MenuItem>()) item.IsChecked = ReferenceEquals(item, sender);
        AplicarZoomVideo();
    }

    /// <summary>Ajuste: a imagem cabe no monitor. Percentual: 100% = um pixel do vídeo por pixel da tela, com rolagem.</summary>
    private void AplicarZoomVideo()
    {
        if (_zoomVideo <= 0 || DataContext is not EditorViewModel { Timeline.Project.Settings: { } ajustes })
        {
            ConteudoMonitor.Width = ConteudoMonitor.Height = double.NaN;
            RolagemMonitor.HorizontalScrollBarVisibility = RolagemMonitor.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        ConteudoMonitor.Width = ajustes.Width * _zoomVideo / dpi.DpiScaleX;
        ConteudoMonitor.Height = ajustes.Height * _zoomVideo / dpi.DpiScaleY;
        RolagemMonitor.HorizontalScrollBarVisibility = RolagemMonitor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private void Regua_Click(object sender, RoutedEventArgs e) =>
        ReguaMonitor.Visibility = MenuRegua.IsChecked ? Visibility.Visible : Visibility.Collapsed;

    private void MaximizarMonitor_Changed(object sender, RoutedEventArgs e)
    {
        var maximizar = BotaoMaximizarMonitor.IsChecked == true;
        if (maximizar && _modoLayout != "Padrao") AplicarLayout("Padrao");
        if (maximizar)
        {
            _larguraBiblioteca = ColunaBiblioteca.Width;
            _alturaLinhaDoTempo = LinhaInferior.Height;
            ColunaBiblioteca.MinWidth = 0;
            ColunaBiblioteca.Width = new GridLength(0);
            LinhaInferior.MinHeight = 0;
            LinhaInferior.Height = new GridLength(0);
        }
        else
        {
            ColunaBiblioteca.Width = _larguraBiblioteca;
            ColunaBiblioteca.MinWidth = 360;
            LinhaInferior.Height = _alturaLinhaDoTempo;
            LinhaInferior.MinHeight = 200;
        }
        var visivel = maximizar ? Visibility.Collapsed : Visibility.Visible;
        PainelBiblioteca.Visibility = DivisorBiblioteca.Visibility = DivisorCentral.Visibility = AreaInferior.Visibility = visivel;
        IconeMaximizarMonitor.Text = maximizar ? "" : "";
        var texto = (string)FindResource(maximizar ? "Editor.Monitor.Restaurar" : "Editor.Monitor.Maximizar");
        BotaoMaximizarMonitor.ToolTip = texto;
        AutomationProperties.SetName(BotaoMaximizarMonitor, texto);
    }

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
        switch (Shortcuts?.Find(RealKey(e).ToMed(), Keyboard.Modifiers.ToMed()))
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
        var action = Shortcuts?.Find(RealKey(e).ToMed(), Keyboard.Modifiers.ToMed());
        // Teclas sem modificador não disparam ações gerais enquanto se digita.
        if (action is null || (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is TextBox or PasswordBox)) return;
        if (action == ShortcutCatalog.Settings)
        {
            AbrirConfiguracoes();
            e.Handled = true;
            return;
        }
        if (action == ShortcutCatalog.Help)
        {
            Dialogs.HelpWindow.ShowFor(Window.GetWindow(this));
            e.Handled = true;
            return;
        }
        if (action == ShortcutCatalog.Assistant)
        {
            MostrarAssistente(PainelAssistente.Visibility != Visibility.Visible);
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

    /// <summary>Ajuda › Suporte Técnico: abre a página de atendimento no navegador, no idioma da interface.</summary>
    private void Suporte_Click(object sender, RoutedEventArgs e) =>
        Presentation.ViewModels.AboutViewModel.OpenInBrowser(Presentation.ViewModels.AboutViewModel.SupportUrlFor(Presentation.Localization.Translator.Language));

    /// <summary>Ajuda › Verificar atualizações (consulta o serviço de versões).</summary>
    private async void VerificarAtualizacoes_Click(object sender, RoutedEventArgs e) =>
        await ((Presentation.Services.UpdateCheckService)App.Services.GetService(typeof(Presentation.Services.UpdateCheckService))!).CheckAsync(manual: true);

    /// <summary>Manual do usuário offline (F1).</summary>
    private void Manual_Click(object sender, RoutedEventArgs e) => Dialogs.HelpWindow.ShowFor(Window.GetWindow(this));

    private Presentation.ViewModels.AssistantViewModel? _assistente;
    private GridLength _larguraAssistente = new(380);
    private GridLength? _larguraBibliotecaSemAssistente;

    private void MenuAssistente_Click(object sender, RoutedEventArgs e) => MostrarAssistente(MenuAssistente.IsChecked);

    private void Assistente_CloseRequested(object? sender, EventArgs e) => MostrarAssistente(false);

    /// <summary>Atalho no canto inferior direito: abre o assistente (ou só leva o foco à pergunta, se já estiver aberto).</summary>
    private void AtalhoAssistente_Click(object sender, RoutedEventArgs e)
    {
        if (PainelAssistente.Visibility == Visibility.Visible) Assistente.FocusQuestion();
        else MostrarAssistente(true);
    }

    /// <summary>
    /// Abre ou fecha o assistente virtual numa coluna própria, à direita de Propriedades (vale para todos os modos do
    /// layout). A conversa continua ao reabrir; ao fechar, a resposta em andamento para e o modelo sai da memória.
    /// </summary>
    public void MostrarAssistente(bool mostrar)
    {
        MenuAssistente.IsChecked = mostrar;
        if (mostrar == (PainelAssistente.Visibility == Visibility.Visible)) return;
        if (mostrar)
        {
            if (_assistente is null)
            {
                // O catálogo (instalação do modelo pelo aviso do painel) não é serviço: é criado como na janela de modelos.
                var catalogo = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<Presentation.ViewModels.ModelCatalogViewModel>(App.Services);
                _assistente = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<Presentation.ViewModels.AssistantViewModel>(App.Services, catalogo);
                _assistente.OpenSectionRequested += (_, section) => Dialogs.HelpWindow.ShowFor(Window.GetWindow(this), section.Title);
                Assistente.DataContext = _assistente;
            }
            ColunaDivisorAssistente.Width = GridLength.Auto;
            ColunaAssistente.MinWidth = 300;
            ColunaAssistente.Width = _larguraAssistente;
            PainelAssistente.Visibility = DivisorAssistente.Visibility = Visibility.Visible;
            AbrirEspacoParaOMonitor();
            Assistente.FocusQuestion();
        }
        else
        {
            _larguraAssistente = ColunaAssistente.Width;
            ColunaAssistente.MinWidth = 0;
            ColunaAssistente.Width = ColunaDivisorAssistente.Width = new GridLength(0);
            PainelAssistente.Visibility = DivisorAssistente.Visibility = Visibility.Collapsed;
            if (_larguraBibliotecaSemAssistente is { } largura) ColunaBiblioteca.Width = largura;
            _larguraBibliotecaSemAssistente = null;
            _assistente?.Suspend();
        }
    }

    /// <summary>
    /// Em janelas estreitas, a coluna do assistente sai da área do monitor, que ficaria abaixo do mínimo e cortada:
    /// encolhe a biblioteca (até o mínimo dela) o que faltar. A largura anterior volta ao fechar o assistente.
    /// </summary>
    private void AbrirEspacoParaOMonitor()
    {
        UpdateLayout();
        if (PainelMonitor.Parent != AreaCentral || Grid.GetColumnSpan(AreaCentral) != 1) return; // outro modo do layout
        // As colunas da área central respeitam os mínimos e passam da coluna principal; o excesso é a parte cortada do monitor.
        var falta = AreaCentral.ColumnDefinitions.Sum(c => c.ActualWidth) - ColunaPrincipal.ActualWidth;
        var sobra = ColunaBiblioteca.ActualWidth - ColunaBiblioteca.MinWidth;
        if (falta <= 0.5 || sobra <= 0) return;
        _larguraBibliotecaSemAssistente ??= ColunaBiblioteca.Width;
        ColunaBiblioteca.Width = new GridLength(ColunaBiblioteca.ActualWidth - Math.Min(falta, sobra));
    }

    private void Atalhos_Click(object sender, RoutedEventArgs e) => Dialogs.InfoDocumentWindow.ShowShortcuts(Window.GetWindow(this));

    private void InformacoesSistema_Click(object sender, RoutedEventArgs e) => Dialogs.InfoDocumentWindow.ShowSystemInfo(Window.GetWindow(this));

    private void PastaLogs_Click(object sender, RoutedEventArgs e)
    {
        var logs = ((Core.Abstractions.IAppPaths)App.Services.GetService(typeof(Core.Abstractions.IAppPaths))!).Logs;
        System.IO.Directory.CreateDirectory(logs);
        AbrirNoWindows(logs);
    }

    /// <summary>Avisos e licenças dos componentes de terceiros (THIRD-PARTY-NOTICES.txt, instalado com o aplicativo).</summary>
    private void Licencas_Click(object sender, RoutedEventArgs e)
    {
        var notices = System.IO.Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (System.IO.File.Exists(notices)) AbrirNoWindows(notices);
        else Dialogs.AboutWindow.ShowFor(Window.GetWindow(this));
    }

    /// <summary>Abre uma pasta no Explorador ou um arquivo de texto no programa padrão do Windows (nada sai do computador).</summary>
    private static void AbrirNoWindows(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(Presentation.Localization.Translator.F("Não foi possível abrir \"{0}\".", path), "Meu Editor de Vídeo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Configurações do aplicativo (RF208–RF214).</summary>
    private void AbrirConfiguracoes() => Dialogs.SettingsWindow.ShowFor(Window.GetWindow(this));

    private void TelaCheia_Click(object sender, RoutedEventArgs e) => AbrirTelaCheia();

    private Dialogs.MixerWindow? _mixer;

    /// <summary>Abre (ou traz para a frente) a janela não modal do mixer (RF130).</summary>
    /// <summary>Cortar e ampliar: marca a área do clipe selecionado que deve ocupar o quadro.</summary>
    private void CortarAmpliar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel { Inspector: { } inspector, Timeline: { } timeline }) return;
        if (inspector.CropZoomTarget() is not { } target)
        {
            MessageBox.Show(Window.GetWindow(this), Presentation.Localization.Translator.T("Selecione um clipe de vídeo ou imagem na linha do tempo."),
                Presentation.Localization.Translator.T("Cortar e ampliar"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var (clip, asset) = target;
        var projects = (MeuEditorDeVideo.Application.Projects.IProjectService)App.Services.GetService(typeof(MeuEditorDeVideo.Application.Projects.IProjectService))!;
        var factory = (Core.Media.IVideoFrameSourceFactory)App.Services.GetService(typeof(Core.Media.IVideoFrameSourceFactory))!;
        string? path = null;
        projects.Current?.Media.AvailablePaths.TryGetValue(asset.Id, out path);
        var settings = timeline.Project!.Settings;
        var fps = asset.FrameRate ?? timeline.FrameRate;
        var frameDuration = TimeSpan.FromSeconds(fps.FrameDuration.TotalSeconds);
        var playhead = timeline.Playhead >= clip.Start && timeline.Playhead < clip.End ? timeline.Playhead - clip.Start : Domain.Time.MediaTime.Zero;

        using var source = factory.Create(false);
        var gate = new object();
        System.Windows.Media.Imaging.BitmapSource? Frame(TimeSpan offset)
        {
            if (path is null) return null;
            var at = clip.Start + Domain.Time.MediaTime.FromSecondsApprox(offset.TotalSeconds);
            var sourceTime = clip.FreezeAt ?? clip.SourceTimeAt(at);
            Core.Media.BgraImage? img;
            lock (gate) img = source.GetFrame(asset, path, sourceTime, 1280, 1280);
            if (img is null) return null;
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Pixels, img.Stride);
            bmp.Freeze();
            return bmp;
        }

        var t = clip.Transform;
        var dialog = new Dialogs.CropZoomWindow(Frame, TimeSpan.FromSeconds(clip.Duration.TotalSeconds), frameDuration,
            TimeSpan.FromSeconds(playhead.TotalSeconds), (asset.Width, asset.Height), (settings.Width, settings.Height),
            (t.CropLeft, t.CropTop, t.CropRight, t.CropBottom))
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true) return;
        var (l, tp, r, b) = dialog.Crop;
        inspector.ApplyCropZoom(clip.Id, l, tp, r, b);
    }

    private void Mixer_Click(object sender, RoutedEventArgs e)
    {
        if (_mixer is not null)
        {
            _mixer.Activate();
            return;
        }
        if (DataContext is not EditorViewModel { Timeline: { } timeline, Preview: { } preview } editor) return;
        _mixer = new Dialogs.MixerWindow(new Presentation.ViewModels.Timeline.MixerViewModel(timeline, preview), editor.UndoCommand, editor.RedoCommand)
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
        var vm = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<Presentation.ViewModels.ModelManagerViewModel>(App.Services);
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
        _legendas = new Dialogs.SubtitleEditorWindow(new Presentation.ViewModels.Timeline.SubtitleEditorViewModel(timeline, library, library.Dialogs),
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
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ItemsControl)sender, d) is ListBoxItem { DataContext: Presentation.ViewModels.Timeline.TextPresetItem item }
            && DataContext is EditorViewModel { TextLibrary: { } library })
            library.AddPresetCommand.Execute(item);
    }

    /// <summary>Duplo clique no monitor abre a tela cheia.</summary>
    private void Monitor_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) AbrirTelaCheia();
    }

    // ---- Comparar antes/depois: arrastar a linha divisória

    private void ImagemMonitor_SizeChanged(object sender, SizeChangedEventArgs e) => PosicionarAlcaComparacao();

    private void CamadaComparacao_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => PosicionarAlcaComparacao();

    private void PosicionarAlcaComparacao()
    {
        var split = DataContext is EditorViewModel { Preview: { } p } ? p.CompareSplit : 0.5;
        Canvas.SetLeft(AlcaComparacao, split * ImagemMonitor.ActualWidth - AlcaComparacao.Width / 2);
    }

    private void AlcaComparacao_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        AlcaComparacao.CaptureMouse();
        e.Handled = true; // não abre a tela cheia nem edita máscaras
    }

    private void AlcaComparacao_MouseMove(object sender, MouseEventArgs e)
    {
        if (!AlcaComparacao.IsMouseCaptured || ImagemMonitor.ActualWidth <= 0) return;
        if (DataContext is not EditorViewModel { Preview: { } preview }) return;
        preview.CompareSplit = Math.Clamp(e.GetPosition(CamadaComparacao).X / ImagemMonitor.ActualWidth, 0, 1);
        PosicionarAlcaComparacao();
    }

    private void AlcaComparacao_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => AlcaComparacao.ReleaseMouseCapture();

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
        if (DataContext is Presentation.ViewModels.EditorViewModel vm && ListaEfeitos.SelectedItem is Presentation.ViewModels.Inspector.LibraryEffectItem item)
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
