using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Views;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Presentation.Media;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Tela do editor (EditorView), painéis do inspetor e assistente virtual: menus, layout, atalhos e capturas.</summary>
public class EditorViewTests
{
    /// <summary>Roda o teste na thread da interface com um editor novo; o editor é liberado na mesma thread (os controles escutam o ViewModel).</summary>
    private static T Run<T>(Func<EditorFixture, T> body) => Ui.Run(() =>
    {
        using var fx = new EditorFixture();
        return body(fx);
    });

    private static void Run(Action<EditorFixture> body) => Run<object?>(fx =>
    {
        body(fx);
        return null;
    });

    private static (Window Window, EditorView View) Host(EditorFixture fx)
    {
        var view = new EditorView { Services = fx.Services, DataContext = fx.Editor };
        var window = new Window { Width = 1600, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static string Capture(Window window, string name)
    {
        // O motor de prévia desenha um quadro preto (as mídias de exemplo não existem em disco): põe um quadro de exemplo.
        if (window.Content is EditorView { DataContext: EditorViewModel { Preview: { } preview } })
            preview.Image = new DisplayFrame(TimelineFixture.Gradient(1280, 720, 0));
        var path = Ui.Capture(window, name);
        Assert.True(new FileInfo(path).Length > 5000, path);
        return path;
    }

    private static void Theme(ThemeVariant variant) => Avalonia.Application.Current!.RequestedThemeVariant = variant;

    private static void SelectTab(EditorView view, int index)
    {
        view.FindControl<TabControl>("AbasPropriedades")!.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Editor_escuro_e_claro_desenham_biblioteca_monitor_linha_do_tempo_e_propriedades()
    {
        var paths = Run(fx =>
        {
            var (window, view) = Host(fx);
            Assert.NotNull(view.FindControl<TimelineControl>("LinhaDoTempo"));
            Assert.Contains(fx.Editor.Library!.ItemsView, i => i.Name == "Praia.mp4");
            Theme(ThemeVariant.Dark);
            var dark = Capture(window, "editor-escuro");
            Theme(ThemeVariant.Light);
            var light = Capture(window, "editor-claro");
            Theme(ThemeVariant.Dark);
            window.Close();
            return new[] { dark, light };
        });
        Assert.All(paths, p => Assert.True(File.Exists(p)));
    }

    [Fact]
    public void Menus_do_topo_tem_todos_os_itens()
    {
        Run(fx =>
        {
            var (window, view) = Host(fx);
            var menu = view.FindControl<Menu>("MenuPrincipal")!;
            var topo = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["Arquivo", "Editar", "Visualizar", "Projeto", "Ferramentas", "Ajuda"], topo.Select(m => ((string)m.Header!).Replace("_", "")));

            string[] Nomes(MenuItem m) => m.Items.OfType<MenuItem>().Select(i => ((string)i.Header!).Replace("_", "")).ToArray();
            Assert.Contains("Novo projeto…", Nomes(topo[0]));
            Assert.Contains("Abrir…", Nomes(topo[0]));
            Assert.Contains("Exportar…", Nomes(topo[0]));
            Assert.Contains("Desfazer", Nomes(topo[1]));
            Assert.Contains("Refazer", Nomes(topo[1]));
            Assert.Contains("Dividir", Nomes(topo[1]));
            Assert.Contains("Modo do layout", Nomes(topo[2]).Concat(topo[2].Items.OfType<MenuItem>().Select(i => AutomationProperties.GetName(i))));
            Assert.Equal(["Layout padrão", "Layout linha do tempo", "Layout vídeo"],
                view.FindControl<MenuItem>("MenuModoLayout")!.Items.OfType<MenuItem>().Select(i => AutomationProperties.GetName(i)));
            Assert.Equal(8, view.FindControl<MenuItem>("MenuZoomVideo")!.Items.Count);
            Assert.True(topo[3].Items.Count >= 8, "menu Projeto");
            Assert.Contains("Mixer de áudio", Nomes(topo[4]));
            Assert.Equal(["Manual do usuário", "Assistente virtual", "Atalhos de teclado…"], Nomes(topo[5]).Take(3));
            Assert.Contains(topo[5].Items.OfType<MenuItem>(), i => AutomationProperties.GetName(i) == "Verificar atualizações");
            // Tela dividida: um item por layout do projeto.
            var tela = view.FindControl<MenuItem>("MenuTelaDividida")!;
            Assert.Equal(fx.Editor.Timeline!.SplitScreenLayouts.Count, tela.Items.Count);
            window.Close();
        });
    }

    [Fact]
    public void Projetos_recentes_entram_no_menu_Arquivo_ao_abrir()
    {
        Run(fx =>
        {
            var (window, view) = Host(fx);
            var arquivo = view.FindControl<MenuItem>("MenuArquivo")!;
            var antes = arquivo.Items.Count;
            // Sem projetos recentes, o menu fica como foi declarado; abrir o submenu não deve quebrar nem duplicar itens.
            arquivo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.SubmenuOpenedEvent) { Source = arquivo });
            arquivo.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.SubmenuOpenedEvent) { Source = arquivo });
            var recentes = fx.Editor.RecentProjectsForMenu().Count;
            Assert.Equal(antes + (recentes == 0 ? 0 : recentes + 1), arquivo.Items.Count);
            window.Close();
        });
    }

    [Fact]
    public void Modos_do_layout_voltam_ao_padrao_e_o_monitor_maximizado_restaura()
    {
        var paths = Run(fx =>
        {
            var (window, view) = Host(fx);
            var monitor = view.FindControl<Border>("PainelMonitor")!;
            var propriedades = view.FindControl<Border>("PainelPropriedades")!;
            var central = view.FindControl<Grid>("AreaCentral")!;
            var grade = view.FindControl<Grid>("GradeEditor")!;
            var inferior = view.FindControl<Grid>("AreaInferior")!;
            var biblioteca = view.FindControl<Border>("PainelBiblioteca")!;
            Assert.Same(central, monitor.Parent);
            Assert.Same(grade, propriedades.Parent);

            view.AplicarLayout("LinhaDoTempo");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Grid.GetColumnSpan(inferior));
            Assert.Equal(1, Grid.GetRowSpan(propriedades));
            var tempo = Capture(window, "editor-layout-linha-do-tempo");

            view.AplicarLayout("Video");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(grade, monitor.Parent);
            Assert.Same(central, propriedades.Parent);
            Assert.Equal(1, Grid.GetColumnSpan(inferior));
            var video = Capture(window, "editor-layout-video");

            // Volta ao padrão: cada painel no lugar de origem.
            view.AplicarLayout("Padrao");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(central, monitor.Parent);
            Assert.Same(grade, propriedades.Parent);
            Assert.Equal(2, Grid.GetColumn(monitor));
            Assert.Equal(3, Grid.GetRowSpan(propriedades));
            Assert.Equal(1, Grid.GetColumnSpan(inferior));
            var padrao = Capture(window, "editor-layout-padrao");

            // Maximizar o monitor sai de qualquer modo do layout, esconde biblioteca e linha do tempo e restaura depois.
            view.AplicarLayout("Video");
            var botao = view.FindControl<ToggleButton>("BotaoMaximizarMonitor")!;
            botao.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Padrao", view.ModoLayoutAtual);
            Assert.Same(central, monitor.Parent);
            Assert.False(biblioteca.IsVisible);
            Assert.False(inferior.IsVisible);
            var maximizado = Capture(window, "editor-monitor-maximizado");
            botao.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(biblioteca.IsVisible);
            Assert.True(inferior.IsVisible);
            window.Close();
            return new[] { tempo, video, padrao, maximizado };
        });
        Assert.All(paths, p => Assert.True(File.Exists(p)));
    }

    [Fact]
    public void Atalho_Ctrl_Z_desfaz_pela_janela_e_Espaco_alterna_a_reproducao()
    {
        Run(fx =>
        {
            var (window, view) = Host(fx);
            var timeline = fx.Editor.Timeline!;
            var marcadores = timeline.Sequence!.Markers.Count;
            timeline.AddMarkerCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(marcadores + 1, timeline.Sequence!.Markers.Count);

            // O atalho é tratado pela janela que hospeda o editor, mesmo com o foco no corpo da janela.
            window.KeyPress(Key.Z, Ui.Principal, PhysicalKey.Z, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(marcadores, timeline.Sequence!.Markers.Count);

            window.KeyPress(Key.Y, Ui.Principal, PhysicalKey.Y, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(marcadores + 1, timeline.Sequence!.Markers.Count);

            var glifoAntes = fx.Editor.Preview!.PlayGlyph;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            var glifoDepois = fx.Editor.Preview.PlayGlyph;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(glifoAntes, glifoDepois);
            Assert.Equal(glifoAntes, fx.Editor.Preview.PlayGlyph);
            window.Close();
        });
    }

    private static IEnumerable<Control> Interativos(Visual raiz) =>
        raiz.GetVisualDescendants().OfType<Control>().Where(c => c is Button or ToggleButton or MenuItem)
            .Concat(raiz.GetLogicalDescendants().OfType<MenuItem>()).Distinct();

    private static bool TemNome(Control c)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(c))) return true;
        return c switch
        {
            ContentControl { Content: string s } => !string.IsNullOrWhiteSpace(s),
            HeaderedSelectingItemsControl { Header: string h } => !string.IsNullOrWhiteSpace(h),
            ContentControl { Content: Visual v } => v.GetVisualDescendants().OfType<TextBlock>().Any(t => !string.IsNullOrWhiteSpace(t.Text) && !t.Classes.Contains("med-icon")),
            _ => false,
        };
    }

    [Fact]
    public void Todo_botao_e_item_de_menu_tem_nome_acessivel()
    {
        Run(fx =>
        {
            var (window, view) = Host(fx);
            fx.Editor.Timeline!.Select(fx.BeachClip, additive: false);
            Dispatcher.UIThread.RunJobs();
            var semNome = new List<string>();
            var abas = view.FindControl<TabControl>("AbasPropriedades")!;
            var total = 0;
            // Cada aba do inspetor é percorrida: só a aba selecionada materializa o conteúdo.
            for (var i = 0; i < abas.ItemCount; i++)
            {
                SelectTab(view, i);
                foreach (var c in Interativos(view))
                {
                    total++;
                    if (!(c.Name?.StartsWith("PART_", StringComparison.Ordinal) ?? false) && !TemNome(c)) semNome.Add($"aba {i}: {c.GetType().Name} {c.Name} {(c as MenuItem)?.Header} {(c as ContentControl)?.Content}");
                }
            }
            Assert.True(total > 150, $"só {total} controles interativos encontrados");
            Assert.Empty(semNome.Distinct());
            window.Close();
        });
    }

    [Fact]
    public void Inspetor_mostra_cada_painel_com_o_clipe_selecionado()
    {
        var paths = Run(fx =>
        {
            var (window, view) = Host(fx);
            var timeline = fx.Editor.Timeline!;
            var list = new List<string>();
            timeline.Select(fx.BeachClip, additive: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(fx.Editor.Inspector!.HasClip);

            SelectTab(view, 0);
            list.Add(Capture(window, "editor-painel-projeto"));
            SelectTab(view, 2);
            list.Add(Capture(window, "editor-painel-video"));
            SelectTab(view, 4);
            list.Add(Capture(window, "editor-painel-composicao"));
            SelectTab(view, 5);
            list.Add(Capture(window, "editor-painel-ia"));

            // Efeitos: aplica dois efeitos para ver os parâmetros (controles deslizantes, cor, escolhas) da pilha.
            fx.Editor.Inspector.AddEffectCommand.Execute(EffectCatalog.Blur);
            fx.Editor.Inspector.AddEffectCommand.Execute(EffectCatalog.Sharpen);
            Dispatcher.UIThread.RunJobs();
            SelectTab(view, 7);
            list.Add(Capture(window, "editor-painel-efeitos"));
            Assert.NotEmpty(fx.Editor.Inspector.OtherEffects);

            fx.Editor.Inspector.AddEffectCommand.Execute(EffectCatalog.ColorAdjust);
            Dispatcher.UIThread.RunJobs();
            SelectTab(view, 8);
            list.Add(Capture(window, "editor-painel-cor"));

            // Áudio: clipe de áudio da Floresta Amazônica.
            timeline.Select(fx.ForestAudioClip, additive: false);
            Dispatcher.UIThread.RunJobs();
            SelectTab(view, 6);
            list.Add(Capture(window, "editor-painel-audio"));

            // Texto: o modelo simples da biblioteca de textos vai para o cursor e fica selecionado.
            var texto = fx.Editor.TextLibrary!;
            texto.AddPresetCommand.Execute(texto.Presets[0]);
            Dispatcher.UIThread.RunJobs();
            SelectTab(view, 3);
            list.Add(Capture(window, "editor-painel-texto"));

            SelectTab(view, 9);
            list.Add(Capture(window, "editor-painel-historico"));
            window.Close();
            return list;
        });
        Assert.Equal(9, paths.Count);
    }

    [Fact]
    public void Secoes_da_biblioteca_alternam_e_o_item_ativo_fica_destacado()
    {
        var paths = Run(fx =>
        {
            var (window, view) = Host(fx);
            var selecionados = () => view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("editor-nav") && b.Classes.Contains("selected")).ToList();
            Assert.Single(selecionados());
            var list = new List<string>();
            foreach (var secao in new[] { LibrarySection.Texts, LibrarySection.Graphics, LibrarySection.Effects, LibrarySection.Color, LibrarySection.AI })
            {
                fx.Editor.SelectSectionCommand.Execute(secao);
                Dispatcher.UIThread.RunJobs();
                Assert.Single(selecionados());
                list.Add(Capture(window, "editor-biblioteca-" + secao.ToString().ToLowerInvariant()));
            }
            window.Close();
            return list;
        });
        Assert.Equal(5, paths.Count);
    }

    [Fact]
    public void Assistente_virtual_abre_na_coluna_a_direita_e_fecha()
    {
        var path = Run(fx =>
        {
            var (window, view) = Host(fx);
            var painel = view.FindControl<Border>("PainelAssistente")!;
            Assert.False(painel.IsVisible);
            view.MostrarAssistente(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(painel.IsVisible);
            Assert.True(view.AssistenteAberto);
            Assert.True(view.FindControl<MenuItem>("MenuAssistente")!.IsChecked);
            Assert.IsType<AssistantViewModel>(view.FindControl<Control>("Assistente")!.DataContext);
            var file = Capture(window, "editor-assistente");
            view.MostrarAssistente(false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(painel.IsVisible);
            Assert.False(view.FindControl<MenuItem>("MenuAssistente")!.IsChecked);
            window.Close();
            return file;
        });
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Regua_do_monitor_liga_pelo_menu_Visualizar()
    {
        var path = Run(fx =>
        {
            var (window, view) = Host(fx);
            var regua = view.FindControl<MonitorRuler>("ReguaMonitor")!;
            Assert.False(regua.IsVisible);
            view.FindControl<MenuItem>("MenuRegua")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(regua.IsVisible);
            var file = Capture(window, "editor-monitor-regua");
            window.Close();
            return file;
        });
        Assert.True(File.Exists(path));
    }
}
