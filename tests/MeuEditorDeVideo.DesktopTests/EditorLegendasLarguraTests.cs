using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Presentation.Localization;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Na largura mínima, nenhum controle do Editor de legendas sai da janela nem cobre outro, em nenhum idioma.</summary>
public class EditorLegendasLarguraTests
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("pt-PT")]
    public void Controles_cabem_na_largura_minima(string idioma)
    {
        // Lê o .tsv em vez de ligar o tradutor, que é estático.
        var textos = new Dictionary<string, string>();
        if (idioma != "pt-BR")
        {
            var raiz = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(raiz.FullName, "MeuEditorDeVideo.sln"))) raiz = raiz.Parent!;
            foreach (var linha in File.ReadLines(Path.Combine(raiz.FullName, "resources", "i18n", idioma + ".tsv")))
                textos[linha[..8]] = linha[9..];
        }

        var erros = Ui.Run(() =>
        {
            var janela = new SubtitleEditorWindow();
            janela.Width = janela.MinWidth;
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var c in janela.GetLogicalDescendants().OfType<Control>().ToList())
            {
                if (c is TextBlock { Text: { } t } bloco && textos.TryGetValue(Translator.IdOf(t), out var texto)) bloco.Text = texto;
                if (c is ContentControl { Content: string s } conteudo && textos.TryGetValue(Translator.IdOf(s), out var novo)) conteudo.Content = novo;
            }
            Dispatcher.UIThread.RunJobs();

            var areas = new List<(string Nome, Rect Area)>();
            foreach (var c in janela.GetLogicalDescendants().OfType<Control>())
            {
                if (c.TemplatedParent is not null || c is not (Button or TextBox or ComboBox) || !c.IsEffectivelyVisible) continue;
                var nome = (c as Button)?.Content as string ?? c.GetType().Name;
                areas.Add((nome, new Rect(c.TranslatePoint(default, janela)!.Value, c.Bounds.Size)));
            }

            var achados = new List<string>();
            foreach (var (nome, area) in areas)
                if (area.X < 0 || area.Right > janela.Bounds.Width || area.Width < 30) achados.Add($"{nome}: {area}");
            for (var i = 0; i < areas.Count; i++)
                for (var j = i + 1; j < areas.Count; j++)
                    if (areas[i].Area.Intersects(areas[j].Area)) achados.Add($"{areas[i].Nome} cobre {areas[j].Nome}");

            Assert.True(areas.Count >= 8, $"só {areas.Count} controles");
            Ui.Capture(janela, "editor-legendas-estreito-" + idioma);
            janela.Close();
            return achados;
        });
        Assert.True(erros.Count == 0, string.Join('\n', erros));
    }
}
