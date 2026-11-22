using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Media;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.Desktop.Views;

/// <summary>
/// Tela do editor: barra superior com menus, biblioteca, monitor, propriedades, linha do tempo e assistente virtual.
/// Os atalhos de teclado (RF212) ficam na janela que hospeda a tela, para valerem mesmo com o foco fora do editor.
/// </summary>
public partial class EditorView : UserControl
{
    private const string RecentTag = "recente";
    private static readonly DataFormat<string> TrilhaFormat = DataFormat.CreateStringApplicationFormat("MeuEditorDeVideo.Trilha");

    private TopLevel? _host;
    private TimelineViewModel? _linhaDoTempo;
    private EditorViewModel? _editor;

    public EditorView()
    {
        InitializeComponent();
        var colunas = GradeEditor.ColumnDefinitions;
        colunas[0].MinWidth = 760;
        colunas[2].MinWidth = 340;
        AreaCentral.ColumnDefinitions[0].MinWidth = 360;
        AreaCentral.ColumnDefinitions[2].MinWidth = 400;
        GradeEditor.RowDefinitions[3].MinHeight = 200;

        MenuRegua.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.IsCheckedProperty) ReguaMonitor.IsVisible = MenuRegua.IsChecked;
        };
        MenuAssistente.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.IsCheckedProperty) MostrarAssistente(MenuAssistente.IsChecked);
        };
        ImagemMonitor.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) PosicionarAlcaComparacao();
        };
        CamadaComparacao.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) PosicionarAlcaComparacao();
        };
        BarraTitulo.PointerPressed += BarraTitulo_PointerPressed;

        // Arrastar arquivos do gerenciador de arquivos sobre a biblioteca (RF027).
        BibliotecaArea.AddHandler(DragDrop.DragEnterEvent, BibliotecaArea_DragOver);
        BibliotecaArea.AddHandler(DragDrop.DragOverEvent, BibliotecaArea_DragOver);
        BibliotecaArea.AddHandler(DragDrop.DragLeaveEvent, BibliotecaArea_DragLeave);
        BibliotecaArea.AddHandler(DragDrop.DropEvent, BibliotecaArea_Drop);

        // Arrastar uma mídia da biblioteca até a linha do tempo (RF044); clique direito também seleciona o item.
        ListaMidias.AddHandler(PointerPressedEvent, Biblioteca_PointerPressed, RoutingStrategies.Tunnel);
        ListaMidias.AddHandler(PointerMovedEvent, Biblioteca_PointerMoved, RoutingStrategies.Tunnel);
        ListaMidias.AddHandler(PointerReleasedEvent, (_, _) => _arrasteItem = null, RoutingStrategies.Tunnel);

        // macOS: a barra de menus é a do sistema (NativeMenu); o menu da janela fica oculto.
        if (OperatingSystem.IsMacOS()) MenuPrincipal.IsVisible = false;
    }

    /// <summary>
    /// Serviços do aplicativo (atalhos, atualizações, assistente). Por padrão, os do <see cref="App"/>; os testes de
    /// interface, que não iniciam o host do aplicativo, informam os próprios.
    /// </summary>
    public IServiceProvider? Services { get; set; }

    private IServiceProvider? Sp => Services ?? App.ServicesOrNull;

    private ShortcutService? Shortcuts => Sp?.GetService<ShortcutService>();

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    // ---------------------------------------------------------------- ciclo de vida

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var top = TopLevel.GetTopLevel(this);
        if (top is null || ReferenceEquals(top, _host)) return;
        Unhook();
        _host = top;
        top.AddHandler(KeyDownEvent, EditorView_PreviewKeyDown, RoutingStrategies.Tunnel);
        top.AddHandler(KeyDownEvent, EditorView_KeyDown, RoutingStrategies.Bubble);
        if (OperatingSystem.IsMacOS() && top is Window window) InstalarMenuNativo(window);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unhook();
    }

    private void Unhook()
    {
        if (_host is null) return;
        _host.RemoveHandler(KeyDownEvent, EditorView_PreviewKeyDown);
        _host.RemoveHandler(KeyDownEvent, EditorView_KeyDown);
        _host = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_editor is not null) _editor.PropertyChanged -= Editor_PropertyChanged;
        _editor = DataContext as EditorViewModel;
        if (_editor is not null) _editor.PropertyChanged += Editor_PropertyChanged;
        ObservarLinhaDoTempo();
    }

    private void Editor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Timeline)) ObservarLinhaDoTempo();
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
        PreencherTelaDividida();
    }

    /// <summary>Projeto › Tela dividida: um item por layout (no WPF, <c>ItemsSource</c> com estilo de contêiner).</summary>
    private void PreencherTelaDividida()
    {
        MenuTelaDividida.Items.Clear();
        if (_linhaDoTempo is not { } timeline) return;
        foreach (var layout in timeline.SplitScreenLayouts)
        {
            var nome = Translator.T(layout.Name);
            var item = new MenuItem { Header = nome, Command = timeline.SplitScreenCommand, CommandParameter = layout.Id };
            Avalonia.Automation.AutomationProperties.SetName(item, nome);
            MenuTelaDividida.Items.Add(item);
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
            Dispatcher.UIThread.Post(() => aba.IsSelected = true, DispatcherPriority.Background);
            return;
        }
    }

    private void RevelarTopo(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => RolagemTrilhas.ScrollToHome(), DispatcherPriority.Loaded);

    // ---------------------------------------------------------------- barra superior

    /// <summary>
    /// A faixa superior ocupa a área de legenda (a área do cliente se estende sob a barra de título no Windows e no
    /// macOS): arrastar uma parte livre da faixa move a janela e o duplo clique maximiza ou restaura.
    /// O clique num item de menu sobe do popup (outra raiz visual) até a faixa sem estar marcado como tratado: se ele
    /// iniciasse o arraste, a janela capturaria o mouse e o item nunca receberia o "soltar" (o menu não fazia nada).
    /// </summary>
    private void BarraTitulo_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || OwnerWindow is not { } window || !e.GetCurrentPoint(BarraTitulo).Properties.IsLeftButtonPressed) return;
        if (!IniciaArrasteDaJanela(e.Source as Visual, window)) return;
        if (e.ClickCount == 2)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            window.BeginMoveDrag(e);
    }

    /// <summary>Só a parte livre da faixa (na própria janela, fora de menus e botões) arrasta a janela.</summary>
    public static bool IniciaArrasteDaJanela(Visual? origem, TopLevel janela)
    {
        if (origem is null || !ReferenceEquals(TopLevel.GetTopLevel(origem), janela)) return false;
        for (var v = origem; v is not null; v = v.GetVisualParent())
            if (v is MenuItem or Menu or Avalonia.Controls.Primitives.ToggleButton or Button or TextBox) return false;
        return true;
    }

    /// <summary>Barra de menus nativa do macOS: os mesmos itens do menu da janela, mais o menu do aplicativo.</summary>
    private void InstalarMenuNativo(Window window)
    {
        NativeMenu.SetMenu(window, NativeMenuBuilder.FromMenu(MenuPrincipal));
        if (Avalonia.Application.Current is { } app)
            NativeMenu.SetMenu(app, NativeMenuBuilder.ApplicationMenu(
                about: () => AboutWindow.ShowFor(OwnerWindow),
                settings: AbrirConfiguracoes,
                quit: () => OwnerWindow?.Close()));
    }

    /// <summary>
    /// Arquivo: depois do separador abaixo de "Abrir…", os 5 projetos recentes (_1 a _5, caminho na dica) e mais um
    /// separador. Sem projetos recentes, só o separador original.
    /// </summary>
    private void MenuArquivo_SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MenuArquivo) || DataContext is not EditorViewModel vm) return;
        foreach (var old in MenuArquivo.Items.OfType<Control>().Where(i => Equals(i.Tag, RecentTag)).ToList())
            MenuArquivo.Items.Remove(old);

        var recent = vm.RecentProjectsForMenu();
        if (recent.Count == 0) return;
        var index = MenuArquivo.Items.IndexOf(SeparadorRecentes) + 1;
        for (var i = 0; i < recent.Count; i++)
        {
            var path = recent[i];
            var name = Path.GetFileNameWithoutExtension(path);
            var item = new MenuItem
            {
                Header = $"_{i + 1}  {name.Replace("_", "__", StringComparison.Ordinal)}",
                Tag = RecentTag,
                Command = vm.OpenRecentCommand,
                CommandParameter = path,
            };
            ToolTip.SetTip(item, path);
            Avalonia.Automation.AutomationProperties.SetName(item, name);
            MenuArquivo.Items.Insert(index++, item);
        }
        MenuArquivo.Items.Insert(index, new Separator { Tag = RecentTag });
    }

    // ---------------------------------------------------------------- Visualizar › Modo do layout

    private string _modoLayout = "Padrao";
    private GridLength _larguraPropriedades = new(400);
    private GridLength _larguraBibliotecaLayout = new(560);
    private GridLength _larguraBiblioteca = new(560);
    private GridLength _alturaLinhaDoTempo = new(300);

    private ColumnDefinition ColunaPrincipal => GradeEditor.ColumnDefinitions[0];
    private ColumnDefinition ColunaPropriedades => GradeEditor.ColumnDefinitions[2];
    private ColumnDefinition ColunaDivisorAssistente => GradeEditor.ColumnDefinitions[3];
    private ColumnDefinition ColunaAssistente => GradeEditor.ColumnDefinitions[4];
    private ColumnDefinition ColunaBiblioteca => AreaCentral.ColumnDefinitions[0];
    private RowDefinition LinhaInferior => GradeEditor.RowDefinitions[3];

    private void ModoLayout_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string modo }) AplicarLayout(modo);
    }

    /// <summary>
    /// Padrão: biblioteca e monitor em cima, linha do tempo embaixo, Propriedades à direita em altura total.
    /// Linha do tempo: igual, mas a linha do tempo ocupa toda a largura da janela.
    /// Vídeo: o monitor ocupa a metade direita da janela, em altura total; biblioteca e Propriedades ficam em cima, à
    /// esquerda, com a linha do tempo embaixo.
    /// </summary>
    public void AplicarLayout(string modo) => AplicarLayout(modo, desmaximizar: true);

    /// <param name="desmaximizar">Desliga o botão de maximizar o monitor (o próprio botão pede o layout padrão sem desligar-se).</param>
    private void AplicarLayout(string modo, bool desmaximizar)
    {
        if (desmaximizar && BotaoMaximizarMonitor.IsChecked == true) BotaoMaximizarMonitor.IsChecked = false;
        foreach (var item in MenuModoLayout.Items.OfType<MenuItem>()) item.IsChecked = (string?)item.Tag == modo;
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
        else
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

    /// <summary>Modo do layout em vigor ("Padrao", "LinhaDoTempo" ou "Video").</summary>
    public string ModoLayoutAtual => _modoLayout;

    private static void Posicionar(Control e, int linha, int linhas, int coluna)
    {
        Grid.SetRow(e, linha);
        Grid.SetRowSpan(e, linhas);
        Grid.SetColumn(e, coluna);
        Grid.SetColumnSpan(e, 1);
    }

    // ---------------------------------------------------------------- Visualizar › Qualidade, zoom e régua

    private void MenuQualidade_SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MenuQualidade) || DataContext is not EditorViewModel { Preview: { } preview }) return;
        MenuQualidade.Items.Clear();
        foreach (var opcao in preview.Qualities)
        {
            var item = new MenuItem { Header = opcao.Label, ToggleType = MenuItemToggleType.Radio, GroupName = "qualidade", IsChecked = ReferenceEquals(opcao, preview.Quality) };
            Avalonia.Automation.AutomationProperties.SetName(item, "Qualidade " + opcao.Label);
            item.Click += (_, _) => preview.Quality = opcao;
            MenuQualidade.Items.Add(item);
        }
    }

    private double _zoomVideo;

    private void ZoomVideo_Click(object? sender, RoutedEventArgs e)
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
        var escala = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        ConteudoMonitor.Width = ajustes.Width * _zoomVideo / escala;
        ConteudoMonitor.Height = ajustes.Height * _zoomVideo / escala;
        RolagemMonitor.HorizontalScrollBarVisibility = RolagemMonitor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private void MaximizarMonitor_Changed(object? sender, RoutedEventArgs e)
    {
        var maximizar = BotaoMaximizarMonitor.IsChecked == true;
        if (maximizar && _modoLayout != "Padrao") AplicarLayout("Padrao", desmaximizar: false);
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
        PainelBiblioteca.IsVisible = DivisorBiblioteca.IsVisible = DivisorCentral.IsVisible = AreaInferior.IsVisible = !maximizar;
        IconeMaximizarMonitor.Text = maximizar ? "" : "";
        var texto = this.FindResource(maximizar ? "Editor.Monitor.Restaurar" : "Editor.Monitor.Maximizar") as string ?? "";
        ToolTip.SetTip(BotaoMaximizarMonitor, texto);
        Avalonia.Automation.AutomationProperties.SetName(BotaoMaximizarMonitor, texto);
    }

    // ---------------------------------------------------------------- atalhos de teclado

    private bool DigitandoTexto => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Reproduzir/pausar e tela cheia (RF212) antes dos controles, para que o Espaço não acione o botão em foco — exceto
    /// enquanto se digita num campo de texto.
    /// </summary>
    private void EditorView_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DigitandoTexto || DataContext is not EditorViewModel { Preview: { } preview }) return;
        switch (Shortcuts?.Find(e.Key.ToMed(), e.KeyModifiers.ToMed()))
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
    private void EditorView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not EditorViewModel vm) return;
        var action = Shortcuts?.Find(e.Key.ToMed(), e.KeyModifiers.ToMed());
        // Teclas sem modificador não disparam ações gerais enquanto se digita.
        if (action is null || (e.KeyModifiers == KeyModifiers.None && DigitandoTexto)) return;
        if (action == ShortcutCatalog.Settings)
        {
            AbrirConfiguracoes();
            e.Handled = true;
            return;
        }
        if (action == ShortcutCatalog.Help)
        {
            HelpWindow.ShowFor(OwnerWindow);
            e.Handled = true;
            return;
        }
        if (action == ShortcutCatalog.Assistant)
        {
            MostrarAssistente(!PainelAssistente.IsVisible);
            e.Handled = true;
            return;
        }
        ICommand? command = action switch
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

    // ---------------------------------------------------------------- Ferramentas e Ajuda

    private void Configuracoes_Click(object? sender, RoutedEventArgs e) => AbrirConfiguracoes();

    /// <summary>Informações do aplicativo (RF215).</summary>
    private void Sobre_Click(object? sender, RoutedEventArgs e) => AboutWindow.ShowFor(OwnerWindow);

    /// <summary>Ajuda › Suporte Técnico: abre a página de atendimento no navegador, no idioma da interface.</summary>
    private void Suporte_Click(object? sender, RoutedEventArgs e) =>
        AboutViewModel.OpenInBrowser(AboutViewModel.SupportUrlFor(Translator.Language));

    /// <summary>Ajuda › Verificar atualizações (consulta o serviço de versões).</summary>
    private async void VerificarAtualizacoes_Click(object? sender, RoutedEventArgs e)
    {
        if (Sp?.GetService<UpdateCheckService>() is { } service) await service.CheckAsync(manual: true);
    }

    /// <summary>Manual do usuário offline (F1).</summary>
    private void Manual_Click(object? sender, RoutedEventArgs e) => HelpWindow.ShowFor(OwnerWindow);

    private void Atalhos_Click(object? sender, RoutedEventArgs e) => InfoDocumentWindow.ShowShortcuts(OwnerWindow);

    private void InformacoesSistema_Click(object? sender, RoutedEventArgs e) => InfoDocumentWindow.ShowSystemInfo(OwnerWindow);

    private void PastaLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (Sp?.GetService<IAppPaths>() is not { } paths) return;
        Directory.CreateDirectory(paths.Logs);
        Abrir(paths.Logs);
    }

    /// <summary>Avisos e licenças dos componentes de terceiros (THIRD-PARTY-NOTICES.txt, instalado com o aplicativo).</summary>
    private void Licencas_Click(object? sender, RoutedEventArgs e)
    {
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notices)) Abrir(notices);
        else AboutWindow.ShowFor(OwnerWindow);
    }

    /// <summary>Abre uma pasta no gerenciador de arquivos ou um arquivo de texto no programa padrão (nada sai do computador).</summary>
    private void Abrir(string path)
    {
        try
        {
            SystemShell.Open(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Sp?.GetService<IDialogService>()?.ShowInfo("Meu Editor de Vídeo", Translator.F("Não foi possível abrir \"{0}\".", path));
        }
    }

    /// <summary>Configurações do aplicativo (RF208–RF214).</summary>
    private void AbrirConfiguracoes() => SettingsWindow.ShowFor(OwnerWindow);

    // ---------------------------------------------------------------- assistente virtual

    private AssistantViewModel? _assistente;
    private GridLength _larguraAssistente = new(380);
    private GridLength? _larguraBibliotecaSemAssistente;

    private void Assistente_CloseRequested(object? sender, EventArgs e) => MostrarAssistente(false);

    /// <summary>Atalho no canto inferior direito: abre o assistente (ou só leva o foco à pergunta, se já estiver aberto).</summary>
    private void AtalhoAssistente_Click(object? sender, RoutedEventArgs e)
    {
        if (PainelAssistente.IsVisible) Assistente.FocusQuestion();
        else MostrarAssistente(true);
    }

    /// <summary>O painel do assistente está aberto.</summary>
    public bool AssistenteAberto => PainelAssistente.IsVisible;

    /// <summary>
    /// Abre ou fecha o assistente virtual numa coluna própria, à direita de Propriedades (vale para todos os modos do
    /// layout). A conversa continua ao reabrir; ao fechar, a resposta em andamento para e o modelo sai da memória.
    /// </summary>
    public void MostrarAssistente(bool mostrar)
    {
        if (MenuAssistente.IsChecked != mostrar) MenuAssistente.IsChecked = mostrar;
        if (mostrar == PainelAssistente.IsVisible) return;
        if (mostrar)
        {
            if (_assistente is null && Sp is { } services)
            {
                // O catálogo (instalação do modelo pelo aviso do painel) não é serviço: é criado como na janela de modelos.
                var catalogo = ActivatorUtilities.CreateInstance<ModelCatalogViewModel>(services);
                _assistente = ActivatorUtilities.CreateInstance<AssistantViewModel>(services, catalogo);
                _assistente.OpenSectionRequested += (_, section) => HelpWindow.ShowFor(OwnerWindow, section.Title);
                Assistente.DataContext = _assistente;
            }
            ColunaDivisorAssistente.Width = GridLength.Auto;
            ColunaAssistente.MinWidth = 300;
            ColunaAssistente.Width = _larguraAssistente;
            PainelAssistente.IsVisible = DivisorAssistente.IsVisible = true;
            AbrirEspacoParaOMonitor();
            Assistente.FocusQuestion();
        }
        else
        {
            _larguraAssistente = ColunaAssistente.Width;
            ColunaAssistente.MinWidth = 0;
            ColunaAssistente.Width = ColunaDivisorAssistente.Width = new GridLength(0);
            PainelAssistente.IsVisible = DivisorAssistente.IsVisible = false;
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

    // ---------------------------------------------------------------- janelas auxiliares

    private MixerWindow? _mixer;
    private SubtitleEditorWindow? _legendas;
    private ModelManagerWindow? _modelos;

    /// <summary>Abre (ou traz para a frente) a janela não modal do mixer (RF130).</summary>
    private void Mixer_Click(object? sender, RoutedEventArgs e)
    {
        if (_mixer is not null)
        {
            _mixer.Activate();
            return;
        }
        if (DataContext is not EditorViewModel { Timeline: { } timeline, Preview: { } preview } editor) return;
        _mixer = new MixerWindow(new MixerViewModel(timeline, preview), editor.UndoCommand, editor.RedoCommand);
        _mixer.Closed += (_, _) => _mixer = null;
        MostrarNaoModal(_mixer);
    }

    /// <summary>Gerenciador de modelos de IA (RF166–RF170).</summary>
    private void ModelosIA_Click(object? sender, RoutedEventArgs e)
    {
        if (_modelos is not null)
        {
            _modelos.Activate();
            return;
        }
        if (Sp is not { } services) return;
        var vm = ActivatorUtilities.CreateInstance<ModelManagerViewModel>(services);
        _modelos = new ModelManagerWindow(vm);
        _modelos.Closed += (_, _) => _modelos = null;
        MostrarNaoModal(_modelos);
    }

    /// <summary>Abre (ou traz para a frente) o editor de legendas (RF143).</summary>
    private void EditorLegendas_Click(object? sender, RoutedEventArgs e)
    {
        if (_legendas is not null)
        {
            _legendas.Activate();
            return;
        }
        if (DataContext is not EditorViewModel { Timeline: { } timeline, TextLibrary: { } library } editor) return;
        _legendas = new SubtitleEditorWindow(new SubtitleEditorViewModel(timeline, library, library.Dialogs), editor.UndoCommand, editor.RedoCommand);
        _legendas.Closed += (_, _) => _legendas = null;
        MostrarNaoModal(_legendas);
    }

    private void MostrarNaoModal(Window window)
    {
        if (OwnerWindow is { IsVisible: true } owner) window.Show(owner);
        else window.Show();
    }

    /// <summary>Cortar e ampliar: marca a área do clipe selecionado que deve ocupar o quadro.</summary>
    private void CortarAmpliar_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel { Inspector: { } inspector, Timeline: { } timeline } || Sp is not { } services) return;
        if (inspector.CropZoomTarget() is not { } target)
        {
            services.GetService<IDialogService>()?.ShowInfo(Translator.T("Cortar e ampliar"),
                Translator.T("Selecione um clipe de vídeo ou imagem na linha do tempo."));
            return;
        }
        var (clip, asset) = target;
        var projects = services.GetRequiredService<MeuEditorDeVideo.Application.Projects.IProjectService>();
        var factory = services.GetRequiredService<Core.Media.IVideoFrameSourceFactory>();
        string? path = null;
        projects.Current?.Media.AvailablePaths.TryGetValue(asset.Id, out path);
        var settings = timeline.Project!.Settings;
        var fps = asset.FrameRate ?? timeline.FrameRate;
        var frameDuration = TimeSpan.FromSeconds(fps.FrameDuration.TotalSeconds);
        var playhead = timeline.Playhead >= clip.Start && timeline.Playhead < clip.End ? timeline.Playhead - clip.Start : Domain.Time.MediaTime.Zero;

        using var source = factory.Create(false);
        var gate = new object();
        Core.Media.BgraImage? Frame(TimeSpan offset)
        {
            if (path is null) return null;
            var at = clip.Start + Domain.Time.MediaTime.FromSecondsApprox(offset.TotalSeconds);
            var sourceTime = clip.FreezeAt ?? clip.SourceTimeAt(at);
            lock (gate) return source.GetFrame(asset, path, sourceTime, 1280, 1280);
        }

        var t = clip.Transform;
        var dialog = new CropZoomWindow(Frame, TimeSpan.FromSeconds(clip.Duration.TotalSeconds), frameDuration,
            TimeSpan.FromSeconds(playhead.TotalSeconds), (asset.Width, asset.Height), (settings.Width, settings.Height),
            (t.CropLeft, t.CropTop, t.CropRight, t.CropBottom));
        if (DialogHost.ShowModal(dialog) != true) return;
        var (l, tp, r, b) = dialog.Crop;
        inspector.ApplyCropZoom(clip.Id, l, tp, r, b);
    }

    // ---------------------------------------------------------------- monitor

    private void TelaCheia_Click(object? sender, RoutedEventArgs e) => AbrirTelaCheia();

    private void AbrirTelaCheia()
    {
        if (DataContext is not EditorViewModel { Preview: { HasContent: true } preview }) return;
        DialogHost.ShowModal(new FullScreenPreviewWindow(preview));
    }

    /// <summary>Duplo clique no monitor abre a tela cheia.</summary>
    private void Monitor_DoubleTapped(object? sender, TappedEventArgs e) => AbrirTelaCheia();

    // ---- Comparar antes/depois: arrastar a linha divisória

    private void PosicionarAlcaComparacao()
    {
        var split = DataContext is EditorViewModel { Preview: { } p } ? p.CompareSplit : 0.5;
        Canvas.SetLeft(AlcaComparacao, split * ImagemMonitor.Bounds.Width - AlcaComparacao.Width / 2);
    }

    private void AlcaComparacao_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(AlcaComparacao);
        e.Handled = true; // não abre a tela cheia nem edita máscaras
    }

    private void AlcaComparacao_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer.Captured, AlcaComparacao) || ImagemMonitor.Bounds.Width <= 0) return;
        if (DataContext is not EditorViewModel { Preview: { } preview }) return;
        preview.CompareSplit = Math.Clamp(e.GetPosition(CamadaComparacao).X / ImagemMonitor.Bounds.Width, 0, 1);
        PosicionarAlcaComparacao();
    }

    private void AlcaComparacao_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(e.Pointer.Captured, AlcaComparacao)) e.Pointer.Capture(null);
    }

    // ---------------------------------------------------------------- biblioteca

    private void ModelosTexto_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: TextPresetItem item } && DataContext is EditorViewModel { TextLibrary: { } library })
            library.AddPresetCommand.Execute(item);
    }

    private void ElementosGraficos_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is EditorViewModel { GraphicsLibrary: { } g } && g.AddPresetCommand.CanExecute(null)) g.AddPresetCommand.Execute(null);
    }

    /// <summary>Duplo clique num item do catálogo de transições/efeitos/cor aplica ao clipe selecionado.</summary>
    private void ListaEfeitos_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is EditorViewModel vm && ListaEfeitos.SelectedItem is Presentation.ViewModels.Inspector.LibraryEffectItem item)
            vm.ApplySectionItemCommand.Execute(item);
    }

    /// <summary>Arrastar arquivos sobre a biblioteca (RF027): aceita somente arquivos do gerenciador de arquivos.</summary>
    private void BibliotecaArea_DragOver(object? sender, DragEventArgs e)
    {
        var arquivos = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = arquivos ? DragDropEffects.Copy : DragDropEffects.None;
        DicaSoltar.IsVisible = arquivos;
        e.Handled = true;
    }

    private void BibliotecaArea_DragLeave(object? sender, DragEventArgs e) => DicaSoltar.IsVisible = false;

    private async void BibliotecaArea_Drop(object? sender, DragEventArgs e)
    {
        DicaSoltar.IsVisible = false;
        e.Handled = true;
        if (DataContext is not EditorViewModel { Library: { } lib }) return;
        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        if (files is { Length: > 0 }) await lib.ImportPathsAsync(files);
    }

    private Point _arrasteInicio;
    private MediaItemViewModel? _arrasteItem;
    private PointerPressedEventArgs? _arrastePressionado;

    private static MediaItemViewModel? ItemSob(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as MediaItemViewModel;

    /// <summary>Guarda o item sob o mouse para iniciar o arraste até a linha do tempo (RF044); o clique direito também o seleciona.</summary>
    private void Biblioteca_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var item = ItemSob(e.Source);
        var ponto = e.GetCurrentPoint(this);
        if (ponto.Properties.IsRightButtonPressed)
        {
            if (item is not null && DataContext is EditorViewModel { Library: { } lib }) lib.SelectedItem = item;
            return;
        }
        if (!ponto.Properties.IsLeftButtonPressed) return;
        _arrasteInicio = ponto.Position;
        _arrasteItem = item;
        _arrastePressionado = e;
    }

    private void Biblioteca_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_arrasteItem is not { IsMissing: false } item || _arrastePressionado is not { } pressionado
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _arrasteInicio.X) < 4 && Math.Abs(p.Y - _arrasteInicio.Y) < 4) return;
        _arrasteItem = null;
        _arrastePressionado = null;
        var dados = new DataTransfer();
        dados.Add(DataTransferItem.Create(TimelineSurface.MediaDataFormat, item.Id.ToString()));
        _ = IniciarArrasteAsync(pressionado, dados, DragDropEffects.Copy);
    }

    private static async Task IniciarArrasteAsync(PointerPressedEventArgs pressionado, DataTransfer dados, DragDropEffects efeitos)
    {
        try
        {
            await DragDrop.DoDragDropAsync(pressionado, dados, efeitos);
        }
        catch (InvalidOperationException)
        {
            // O botão foi solto antes de o sistema iniciar o arraste: nada a fazer.
        }
    }

    /// <summary>Clique duplo na mídia: adiciona ao fim da linha do tempo.</summary>
    private void Biblioteca_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemSob(e.Source) is null) return;
        if (DataContext is EditorViewModel { Timeline: { } tl } && tl.AddSelectedMediaCommand.CanExecute(null))
            tl.AddSelectedMediaCommand.Execute(null);
    }

    // ---------------------------------------------------------------- linha do tempo

    private Point? _inicioArrasteTrilha;
    private PointerPressedEventArgs? _pressionadoTrilha;

    /// <summary>Reordenar trilhas arrastando o cabeçalho; os botões (ocultar, silenciar, bloquear) continuam só clicáveis.</summary>
    private void Trilha_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var sobreBotao = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null;
        if (sobreBotao || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _inicioArrasteTrilha = null;
            return;
        }
        _inicioArrasteTrilha = e.GetPosition(this);
        _pressionadoTrilha = e;
    }

    private void Trilha_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_inicioArrasteTrilha is not { } inicio || _pressionadoTrilha is not { } pressionado || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || sender is not Control { DataContext: TrackHeaderViewModel trilha }) return;
        if (Math.Abs(e.GetPosition(this).Y - inicio.Y) < 4) return;
        _inicioArrasteTrilha = null;
        _pressionadoTrilha = null;
        var dados = new DataTransfer();
        dados.Add(DataTransferItem.Create(TrilhaFormat, trilha.Id.ToString()));
        _ = IniciarArrasteAsync(pressionado, dados, DragDropEffects.Move);
    }

    private void Trilha_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(TrilhaFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Trilha_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(TrilhaFormat) is not { } texto || !Guid.TryParse(texto, out var id) || _linhaDoTempo is not { } tl
            || sender is not Control { DataContext: TrackHeaderViewModel alvo }) return;
        e.Handled = true;
        var destino = tl.Tracks.IndexOf(alvo);
        var origem = tl.Tracks.ToList().FindIndex(t => t.Id == id);
        if (origem < 0 || destino < 0 || origem == destino) return;
        tl.MoveTrack(id, destino);
    }

    private void RenomearMarcador_Click(object? sender, RoutedEventArgs e)
    {
        if (Regua.ContextMarker is { } id && DataContext is EditorViewModel { Timeline: { } tl }) tl.RenameMarker(id);
    }

    private void RemoverMarcador_Click(object? sender, RoutedEventArgs e)
    {
        if (Regua.ContextMarker is { } id && DataContext is EditorViewModel { Timeline: { } tl }) tl.RemoveMarker(id);
    }
}
