// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Desktop.Views;
using MeuEditorDeVideo.DesktopTests.Support;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Faixa superior do editor (arrastar a janela) e tamanho do Slider nos painéis de propriedades.</summary>
public class BarraTituloTests
{
    [Fact]
    public void So_a_parte_livre_da_faixa_arrasta_a_janela_e_os_itens_de_menu_nao()
    {
        Ui.Run(() =>
        {
            var item = new MenuItem { Header = "Gravar tela" };
            var menu = new Menu { Items = { new MenuItem { Header = "Ferramentas", Items = { item } } } };
            var botao = new Button { Content = new TextBlock { Text = "Exportar" } };
            var livre = new Border { Width = 200, Height = 40 };
            var window = new Window
            {
                Width = 600, Height = 300,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { livre, menu, botao } },
            };
            window.Show();
            var topo = (MenuItem)menu.Items[0]!;
            topo.IsSubMenuOpen = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // O item aberto fica num popup (no Windows, outra janela; sem tela, a camada de sobreposição): o clique
            // nele não pode virar arraste da janela.
            Assert.False(EditorView.IniciaArrasteDaJanela(item, window));
            Assert.False(EditorView.IniciaArrasteDaJanela(topo, window));
            Assert.False(EditorView.IniciaArrasteDaJanela((Visual)botao.Content!, window));
            Assert.False(EditorView.IniciaArrasteDaJanela(null, window));
            Assert.True(EditorView.IniciaArrasteDaJanela(livre, window));
            topo.IsSubMenuOpen = false;
            window.Close();
        });
    }

    [Fact]
    public void Titulo_e_botao_de_tela_cheia_desenhados_pelo_Avalonia_ficam_escondidos()
    {
        // O botão de tela cheia da barra desenhada ficava à esquerda do minimizar, por cima do botão Exportar.
        Assert.Contains("PART_FullScreenButton", MainWindow.HiddenTitleBarParts);
        Assert.Contains("PART_TitleTextPanel", MainWindow.HiddenTitleBarParts);
    }

    [Fact]
    public void Bolinha_do_slider_do_inspetor_cabe_inteira_na_linha()
    {
        var path = Ui.Run(() =>
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, VerticalAlignment = VerticalAlignment.Center };
            var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,56"), Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetColumn(slider, 1);
            linha.Children.Add(new TextBlock { Text = "Escala" });
            linha.Children.Add(slider);
            var painel = new StackPanel { Width = 420, Classes = { "inspetor" }, Children = { linha } };
            var window = new Window { Width = 460, Height = 120, Content = painel };
            var file = Ui.Capture(window, "slider-inspetor");

            var thumb = slider.GetVisualDescendants().OfType<Thumb>().First();
            var origem = thumb.TranslatePoint(new Point(0, 0), slider)!.Value;
            Assert.True(thumb.Bounds.Height <= 16, $"bolinha com {thumb.Bounds.Height} px");
            Assert.True(origem.Y >= 0 && origem.Y + thumb.Bounds.Height <= slider.Bounds.Height,
                $"bolinha em y={origem.Y} (altura {thumb.Bounds.Height}) num slider de {slider.Bounds.Height} px");
            window.Close();
            return file;
        });
        Assert.True(File.Exists(path), path);
    }
}
