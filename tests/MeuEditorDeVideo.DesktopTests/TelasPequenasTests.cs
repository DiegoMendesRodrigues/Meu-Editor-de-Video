// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Views;
using MeuEditorDeVideo.DesktopTests.Support;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>O editor inteiro cabe em telas pequenas e em notebooks com escala alta do Windows.</summary>
public class TelasPequenasTests
{
    private static readonly string[] Paineis = ["PainelBiblioteca", "PainelMonitor", "PainelPropriedades", "AreaInferior"];

    [Theory]
    [InlineData(640, 400)]
    [InlineData(911, 512)] // 1366 × 768 a 150%
    [InlineData(1024, 680)]
    [InlineData(1180, 680)]
    [InlineData(1280, 680)]
    [InlineData(1366, 728)]
    [InlineData(1920, 1032)]
    public void Editor_cabe_na_janela_em_todos_os_layouts_com_e_sem_assistente(int largura, int altura) => Ui.Run(() =>
    {
        using var fx = new EditorFixture();
        var view = new EditorView { Services = fx.Services, DataContext = fx.Editor };
        var ajuste = new AjusteDeEscala { Child = view };
        var window = new Window { Width = largura, Height = altura, Content = ajuste };
        window.Show();
        try
        {
            foreach (var assistente in new[] { false, true })
            {
                view.MostrarAssistente(assistente);
                foreach (var modo in new[] { "LinhaDoTempo", "Video", "Padrao" })
                {
                    view.AplicarLayout(modo);
                    Dispatcher.UIThread.RunJobs();
                    var caso = $"{modo}, assistente={assistente}";
                    Ui.Capture(window, $"pequena-{largura}x{altura}-{modo}{(assistente ? "-assistente" : "")}");

                    var exportar = view.GetVisualDescendants().OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == "Exportar" && b.Classes.Contains("editor-acao"));
                    DentroDe(window, exportar, caso + ", Exportar");
                    var direita = Retangulo(window, exportar).Right;
                    Assert.True(direita <= largura - MainWindow.CaptionButtonsWidth.Value + 1, caso + ": Exportar sob os botões da janela");
                    Assert.True(view.FindControl<Border>("BarraTitulo")!.Bounds.Height * ajuste.Escala >= 39, caso + ": faixa superior");

                    var nomes = assistente ? Paineis.Append("PainelAssistente") : Paineis;
                    var areas = nomes.Select(n => (Nome: n, Area: DentroDe(window, view.FindControl<Control>(n)!, $"{caso}, {n}"))).ToList();
                    // Um painel que transborda da coluna passa por baixo do vizinho sem sair da janela.
                    for (var i = 0; i < areas.Count; i++)
                        for (var j = i + 1; j < areas.Count; j++)
                        {
                            var comum = areas[i].Area.Intersect(areas[j].Area);
                            Assert.False(comum.Width > 1 && comum.Height > 1, $"{caso}: {areas[i].Nome} sobre {areas[j].Nome}");
                        }
                }
            }

            view.MostrarAssistente(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Math.Min(1, Math.Min(largura / AjusteDeEscala.LarguraBase, altura / AjusteDeEscala.AlturaBase)), ajuste.Escala, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Clique_acerta_o_botao_no_conteudo_reduzido() => Ui.Run(() =>
    {
        var cliques = 0;
        var botao = new Button { Content = "Alvo", Width = 100, Height = 40 };
        botao.Click += (_, _) => cliques++;
        var window = new Window { Width = 590, Height = 400, Content = new AjusteDeEscala { Child = new Panel { Children = { botao } } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var centro = Retangulo(window, botao).Center;
            window.MouseDown(centro, MouseButton.Left);
            window.MouseUp(centro, MouseButton.Left);
            Assert.Equal(1, cliques);
        }
        finally
        {
            window.Close();
        }
    });

    private static Rect Retangulo(Window window, Visual alvo) =>
        new Rect(alvo.Bounds.Size).TransformToAABB(alvo.TransformToVisual(window)!.Value);

    private static Rect DentroDe(Window window, Visual alvo, string caso)
    {
        var r = Retangulo(window, alvo);
        Assert.True(r.Width > 1 && r.X >= -1 && r.Y >= -1 && r.Right <= window.ClientSize.Width + 1 && r.Bottom <= window.ClientSize.Height + 1,
            $"{caso}: {r} fora de {window.ClientSize}");
        return r;
    }
}
