using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.DesktopTests.Support;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>O texto de todos os botões fica no centro (as capturas dos outros testes conferem o mesmo, com dados).</summary>
public class BotoesTests
{
    [Fact]
    public void Texto_curto_e_texto_que_quebra_ficam_no_centro()
    {
        Ui.Run(() =>
        {
            const string longo = "Tastenkombinationen der Bildschirmaufnahme auf die Standardwerte zurücksetzen";
            var painel = new StackPanel
            {
                Width = 260,
                Children =
                {
                    new Button { Content = "Sim", MinWidth = 88 },
                    new Button { Content = "Não", MinWidth = 88, Classes = { "med-accent" } },
                    new Button { Content = "OK", Width = 180, Height = 60 },
                    new ToggleButtonLargo(),
                    new HyperlinkButton { Content = "Link", Width = 180 },
                    new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Content = new TextBlock { Text = longo, TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
                },
            };
            var window = Ui.Host(painel, 300, 420);
            Ui.Capture(window, "botoes-centralizados");
            window.Close();
        });
    }

    private sealed class ToggleButtonLargo : Avalonia.Controls.Primitives.ToggleButton
    {
        public ToggleButtonLargo()
        {
            Content = "Ímã";
            Width = 180;
        }

        protected override Type StyleKeyOverride => typeof(Avalonia.Controls.Primitives.ToggleButton);
    }

    /// <summary>Cada janela e painel do aplicativo, em todas as abas e com as partes ocultas à mostra.</summary>
    [Fact]
    public void Todas_as_janelas_e_paineis_tem_o_texto_dos_botoes_no_centro()
    {
        var tipos = typeof(MeuEditorDeVideo.Desktop.App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsPublic && (t.IsAssignableTo(typeof(Window)) || t.IsAssignableTo(typeof(UserControl)))
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();
        Assert.True(tipos.Count >= 15, $"só {tipos.Count} telas");

        var erros = Ui.Run(() =>
        {
            var achados = new List<string>();
            foreach (var tipo in tipos)
            {
                var tela = (Control)Activator.CreateInstance(tipo)!;
                var window = tela as Window ?? Ui.Host(tela, 1600, 900);
                window.Show();
                Conferir();
                foreach (var abas in window.GetVisualDescendants().OfType<TabControl>().ToList())
                    for (var i = 0; i < abas.ItemCount; i++)
                    {
                        abas.SelectedIndex = i;
                        Conferir();
                    }
                window.Close();

                void Conferir()
                {
                    Dispatcher.UIThread.RunJobs();
                    foreach (var c in window.GetLogicalDescendants().OfType<Control>().ToList())
                        if (c is Panel or Button or Border or ContentControl and not Window) c.IsVisible = true;
                    Dispatcher.UIThread.RunJobs();
                    achados.AddRange(Centralizacao.Desvios(window).Select(e => $"{tipo.Name}: {e}"));
                }
            }
            return achados.Distinct().ToList();
        });
        Assert.True(erros.Count == 0, string.Join('\n', erros));
    }
}
