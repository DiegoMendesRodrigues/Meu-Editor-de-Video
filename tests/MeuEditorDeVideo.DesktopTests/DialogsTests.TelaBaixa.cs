using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Presentation.Localization;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Export;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Telas baixas: todo diálogo cabe na área útil, com os botões de fora da rolagem à vista e clicáveis.</summary>
public partial class DialogsTests
{
    private static List<(string Nome, Func<Window> Criar)> TodosOsDialogos() =>
    [
        ("sobre", () => new AboutWindow(new AboutViewModel())),
        ("exportar", () => new ExportWindow(ExportVm())),
        ("ajuda", () => new HelpWindow(Manual())),
        ("informacoes", () => new InfoDocumentWindow("Informações", "# Sistema\n\n" + string.Join("\n", Enumerable.Range(1, 60).Select(i => $"- Item {i}")), "copiar")),
        ("midias-ausentes", () => new MissingMediaWindow(MidiasAusentesVm())),
        ("mixer", () => new MixerWindow()),
        ("catalogo", () => new ModelCatalogWindow(CatalogViewModel())),
        ("modelos", () => new ModelManagerWindow(new ModelManagerViewModel(Registry(), new DialogsFakes.MemorySettings(), Stub.Of<IDialogService>()))),
        ("novo-projeto", () => new NewProjectWindow(new NewProjectViewModel(Stub.Of<IDialogService>(), new DialogsFakes.MemorySettings()))),
        ("fila", () => new RenderQueueWindow(new RenderQueueViewModel(Stub.Of<IRenderQueueService>()))),
        ("configuracoes", () => new SettingsWindow(SettingsVm())),
        ("legendas", () => new SubtitleEditorWindow()),
        ("escolha", () => new ChoiceWindow("Visualizador de áudio", "Música a visualizar", ["Praia.mp3", "Floresta.mp3"])),
        ("fala", () => new TextToSpeechWindow([])),
        ("pacotes", () => new ContentPacksWindow(new FakeContentPackStore(), new FakeDialogService())),
        ("recorte", () => new CropZoomWindow(_ => TimelineFixture.Gradient(320, 180, 0), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1 / 30.0),
            TimeSpan.FromSeconds(2), (1920, 1080), (1080, 1920), (0, 0, 0, 0))),
        ("mensagem", () => new MessageWindow("Salvar", "Deseja salvar as alterações?", MessageKind.Warning, ["Salvar", "Não salvar", "Cancelar"], 0, 2)),
        ("mensagem-longa", () => new MessageWindow("Erro", string.Join("\n", Enumerable.Range(1, 80).Select(i => $"Detalhe {i} do erro")), MessageKind.Error, ["OK"])),
        ("texto", () => new PromptWindow("Renomear", "Novo nome", "Praia")),
        ("reenquadrar", () => new ReframeWindow("1920 × 1080", false)),
        ("gravar", () => new ScreenRecordWindow(new FakeScreenRecorder(), "C:\\Vídeos\\Gravação.mp4")),
        ("atualizacao", () => UpdateWindow.CreateNewVersion(new UpdateInfo("1.5.0", "Alemão, Espanhol, Francês, Inglês, Português",
            "Windows, Linux e macOS", "Autor de teste", "https://apps.microsoft.com/detail/exemplo", "https://aplicativosia.online/instalar"))),
        ("boas-vindas", () => new WelcomeWindow(new UserPreferences(null, null, null, null, null))),
    ];

    private static MissingMediaViewModel MidiasAusentesVm()
    {
        var (project, asset) = DialogsFakes.SampleProject();
        var extras = Enumerable.Range(1, 12).Select(i => new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = MediaKind.Audio, Name = $"Som {i}.wav", OriginalPath = $"D:\\Áudios\\Som {i}.wav",
        }).ToList();
        project = new MeuEditorDeVideo.Domain.Editing.AddMediaAssetsCommand(extras).Apply(project);
        var ausentes = extras.Append(asset).ToDictionary(a => a.Id, a => new MediaLocation(a.Id, MediaStatus.Missing, null));
        return new MissingMediaViewModel(DialogsFakes.ProjectsWith(project, new MediaResolution(ausentes)), Stub.Of<IDialogService>());
    }

    [Fact]
    public void Tela_baixa_testa_todos_os_dialogos()
    {
        var testados = Ui.Run(() => TodosOsDialogos().Select(d =>
        {
            var janela = d.Criar();
            var tipo = janela.GetType();
            janela.Close();
            return tipo;
        }).ToHashSet());
        var faltam = typeof(MessageWindow).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MessageWindow).Namespace && typeof(Window).IsAssignableFrom(t) && !t.IsAbstract && !testados.Contains(t));
        Assert.Empty(faltam);
    }

    [Theory]
    [InlineData(640, 400, "pt-BR")]
    [InlineData(911, 512, "pt-BR")]
    [InlineData(1024, 680, "pt-BR")]
    [InlineData(911, 464, "pt-BR")] // 1366 × 768 a 150%, sem a barra de tarefas
    [InlineData(911, 464, "en")]
    [InlineData(911, 464, "es")]
    [InlineData(911, 464, "fr")]
    [InlineData(911, 464, "de")]
    [InlineData(911, 464, "pt-PT")]
    public void Todo_dialogo_cabe_em_tela_baixa_com_os_botoes_clicaveis(int largura, int altura, string idioma)
    {
        // Lê o .tsv em vez de ligar o tradutor, que é estático.
        var textos = idioma == "pt-BR" ? [] : File.ReadLines(Path.Combine(RepoRoot, "resources", "i18n", idioma + ".tsv"))
            .Select(l => l.Split('\t', 2)).ToDictionary(p => p[0], p => p[1]);

        var falhas = Ui.Run(() =>
        {
            var original = JanelasNaTela.AreaUtil;
            JanelasNaTela.AreaUtil = _ => new Size(largura, altura);
            var host = new Window { Width = largura, Height = altura };
            host.Show();
            var achados = new List<string>();
            try
            {
                foreach (var (nome, criar) in TodosOsDialogos())
                {
                    var janela = criar();
                    _ = janela.ShowDialog<bool?>(host); // o mesmo caminho do aplicativo
                    Dispatcher.UIThread.RunJobs();
                    foreach (var c in janela.GetLogicalDescendants().OfType<Control>().ToList())
                    {
                        if (c is TextBlock { Text: { Length: > 0 } t } bloco && textos.TryGetValue(Translator.IdOf(t), out var texto)) bloco.Text = texto;
                        if (c is ContentControl { Content: string s } conteudo && textos.TryGetValue(Translator.IdOf(s), out var novo)) conteudo.Content = novo;
                    }
                    Ui.Capture(janela, $"dialogo-baixo-{largura}x{altura}-{idioma}-{nome}");
                    achados.AddRange(Problemas(janela, new Size(largura, altura)).Select(p => $"{nome}: {p}"));
                    janela.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally
            {
                host.Close();
                JanelasNaTela.AreaUtil = original;
            }
            return achados;
        });
        Assert.True(falhas.Count == 0, string.Join('\n', falhas));
    }

    private static IEnumerable<string> Problemas(Window janela, Size tela)
    {
        var cliente = janela.ClientSize;
        if (cliente.Width > tela.Width - 15 || cliente.Height > tela.Height - 39) yield return $"janela de {cliente}";
        if (janela.Content is AjusteDeEscala escalado)
        {
            Control raiz = escalado.Child!;
            while (raiz is Border { Margin: var m, Child: { } filho } && m == default) raiz = filho;
            var area = new Rect(raiz.Bounds.Size).Inflate(raiz.Margin);
            var naTela = area.TransformToAABB(raiz.TransformToVisual(janela)!.Value);
            if (naTela.X < -1 || naTela.Y < -1 || naTela.Right > cliente.Width + 1 || naTela.Bottom > cliente.Height + 1)
                yield return $"margem cortada ({naTela} em {cliente})";
        }
        if (janela.Content is AjusteDeEscala { Escala: < 0.55 } ajuste) yield return $"ilegível, escala {ajuste.Escala:0.00}";

        foreach (var botao in janela.GetVisualDescendants().OfType<Button>())
        {
            if (botao.TemplatedParent is not null || !botao.IsEffectivelyVisible) continue;
            var dica = botao.Content as string ?? botao.Name ?? "botão sem texto";
            // O que está dentro de uma rolagem se alcança rolando.
            if (botao.GetVisualAncestors().OfType<ScrollViewer>().Any()) continue;
            var r = new Rect(botao.Bounds.Size).TransformToAABB(botao.TransformToVisual(janela)!.Value);
            if (r.X < -1 || r.Y < -1 || r.Right > cliente.Width + 1 || r.Bottom > cliente.Height + 1)
            {
                yield return $"\"{dica}\" fora da janela ({r})";
                continue;
            }
            if (!botao.IsEffectivelyEnabled) continue;
            // As pontas também: um botão cortado por um pai ou coberto por um vizinho responde só no meio.
            foreach (var ponto in new[] { new Point(r.X + 4, r.Center.Y), r.Center, new Point(r.Right - 4, r.Center.Y) })
                if (janela.InputHitTest(ponto) is not Visual alvo || (alvo != botao && !botao.IsVisualAncestorOf(alvo)))
                {
                    yield return $"\"{dica}\" cortado ou coberto em {ponto}";
                    break;
                }
        }
    }

    [Fact]
    public void Dialogo_que_cabe_na_tela_abre_do_tamanho_de_sempre() => Ui.Run(() =>
    {
        var original = JanelasNaTela.AreaUtil;
        JanelasNaTela.AreaUtil = _ => new Size(1920, 1032);
        try
        {
            var janela = new ExportWindow(ExportVm());
            janela.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Size(980, 760), janela.ClientSize);
            Assert.IsNotType<AjusteDeEscala>(janela.Content);
            janela.Close();
        }
        finally
        {
            JanelasNaTela.AreaUtil = original;
        }
    });

    [Theory]
    [InlineData(500, 300, 500, 100)] // passa de baixo: sobe
    [InlineData(-30, -20, 0, 0)]
    [InlineData(1100, 10, 966, 10)]
    [InlineData(200, 50, 200, 50)]
    public void Dialogo_e_trazido_para_dentro_da_tela(int x, int y, int xEsperado, int yEsperado) =>
        Assert.Equal(new PixelPoint(xEsperado, yEsperado),
            JanelasNaTela.DentroDaTela(new PixelRect(x, y, 400, 620), new PixelRect(0, 0, 1366, 720)));
}
