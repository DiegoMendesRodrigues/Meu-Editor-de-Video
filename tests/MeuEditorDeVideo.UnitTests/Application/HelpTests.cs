using System.IO;
using System.Windows.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.Services.Help;
using MeuEditorDeVideo.App.Views.Dialogs;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;

namespace MeuEditorDeVideo.UnitTests.Application;

/// <summary>Ajuda offline (F12): leitura do manual, atalhos e informações do sistema.</summary>
public class HelpTests
{
    [Fact]
    public void Markdown_reconhece_titulos_paragrafos_listas_tabelas_e_notas()
    {
        const string md = """
            # Manual

            Primeira linha
            continua no mesmo parágrafo.

            ## Exportar o vídeo
            - item **um**
            - item `dois`

            1. passo
            2. outro passo

            > Dica: salve sempre.

            | Ação | Atalho |
            |---|:---:|
            | Salvar | Ctrl+S |
            | Abrir | Ctrl+O |
            """;

        var blocks = MarkdownDocument.Parse(md);

        Assert.Collection(blocks,
            b => Assert.Equal(new MdHeading(1, "Manual", "manual"), b),
            b => Assert.Equal("Primeira linha continua no mesmo parágrafo.", Assert.Single(Assert.IsType<MdParagraph>(b).Inlines).Text),
            b => Assert.Equal("exportar-o-vídeo", Assert.IsType<MdHeading>(b).Anchor),
            b =>
            {
                var list = Assert.IsType<MdList>(b);
                Assert.False(list.Numbered);
                Assert.Equal(2, list.Items.Count);
                Assert.Contains(list.Items[0], i => i is { Text: "um", Bold: true });
                Assert.Contains(list.Items[1], i => i is { Text: "dois", Code: true });
            },
            b => Assert.True(Assert.IsType<MdList>(b).Numbered),
            b => Assert.Equal("Dica: salve sempre.", Assert.Single(Assert.IsType<MdNote>(b).Inlines).Text),
            b =>
            {
                var table = Assert.IsType<MdTable>(b);
                Assert.Equal("Atalho", Assert.Single(table.Header[1]).Text);
                Assert.Equal(2, table.Rows.Count);
                Assert.Equal("Ctrl+O", Assert.Single(table.Rows[1][1]).Text);
            });
    }

    [Fact]
    public void Marcadores_sem_par_ficam_como_texto_e_ancoras_repetidas_sao_unicas()
    {
        var inlines = MarkdownDocument.Inlines("custa 2 ** 3 e `abre");
        Assert.Equal("custa 2 ** 3 e `abre", string.Concat(inlines.Select(i => i.Text)));
        Assert.All(inlines, i => Assert.False(i.Bold || i.Code));

        var headings = MarkdownDocument.Parse("## Áudio\n\n## Áudio\n").OfType<MdHeading>().Select(h => h.Anchor).ToList();
        Assert.Equal(["áudio", "áudio-2"], headings);
    }

    [Fact]
    public void Manual_do_repositorio_tem_as_secoes_principais()
    {
        var path = Path.Combine(RepoRoot(), "documentation", "MANUAL_DO_USUARIO.md");
        if (!File.Exists(path)) return; // gerado na F12; o teste de empacotamento garante a cópia para a pasta "ajuda".
        var titles = MarkdownDocument.Parse(File.ReadAllText(path)).OfType<MdHeading>().Where(h => h.Level == 2).Select(h => h.Text).ToList();
        Assert.Contains(titles, t => t.Contains("Primeiros passos", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("Atalhos", StringComparison.Ordinal));
        Assert.Contains(titles, t => t.Contains("Privacidade", StringComparison.Ordinal));
    }

    [Fact]
    public void Tabela_de_atalhos_usa_os_atalhos_em_uso_e_mostra_traco_sem_atalho()
    {
        var service = new ShortcutService();
        var md = InfoDocumentWindow.ShortcutsMarkdown(ShortcutCatalog.All,
            id => id == ShortcutCatalog.Marker ? [] : service.ChordsFor(id));

        Assert.Contains("| Manual do usuário | F1 |", md);
        Assert.Contains("| Refazer | Ctrl+Y ou Ctrl+Shift+Z |", md);
        Assert.Contains("| Adicionar marcador | — |", md);
        Assert.Equal(ShortcutCatalog.All.Select(a => a.Category).Distinct().Count(),
            MarkdownDocument.Parse(md).OfType<MdTable>().Count());
    }

    [Fact]
    public void F1_abre_o_manual()
    {
        Assert.Equal(ShortcutCatalog.Help, new ShortcutService().Find(Key.F1, ModifierKeys.None));
    }

    [Fact]
    public void Informacoes_do_sistema_listam_hardware_modelos_e_pastas()
    {
        var hardware = new HardwareInfo
        {
            CpuName = "CPU de teste", LogicalProcessors = 8,
            TotalMemoryBytes = 16L << 30, AvailableMemoryBytes = 8L << 30,
            Gpus = [new GpuInfo("Placa | teste", 8L << 30, true)],
            Disks = [new DiskInfo("Cache", @"C:\", 100L << 30, 500L << 30)],
            HardwareEncoders = ["NVENC"], HardwareDecoding = true,
        };
        var model = new InstalledModel(new ModelManifest
        {
            Id = "modnet", Name = "MODNet", Version = "1.0", Source = "teste", Capability = ModelCapability.PersonSegmentation,
            Purpose = "Remover fundo", Format = "onnx", File = "modnet.onnx", License = "Apache-2.0", Sha256 = new string('0', 64),
        }, @"C:\modelos\modnet", BuiltIn: true);

        var report = SystemInfoReport.Build("1.0.0", hardware, [model], new FakePaths(), "Windows de teste", ".NET de teste");

        Assert.Contains("Versão: 1.0.0", report);
        Assert.Contains("Processador: CPU de teste (8 núcleos lógicos)", report);
        Assert.Contains("Memória: 16,0 GB (livre: 8,0 GB)", report);
        Assert.Contains("Placa de vídeo: Placa | teste — 8,0 GB (dedicada)", report);
        Assert.Contains("Codificação por hardware: NVENC", report);
        Assert.Contains("  - MODNet 1.0 (incluído)", report);
        Assert.Contains(@"Logs: C:\dados\logs", report);

        var md = InfoDocumentWindow.SystemInfoMarkdown(report);
        Assert.Contains("| Versão | 1.0.0 |", md);
        Assert.Contains("| Placa de vídeo | Placa / teste — 8,0 GB (dedicada) |", md); // "|" não quebra a tabela
        Assert.Contains("- MODNet 1.0 (incluído)", md);
        Assert.True(MarkdownDocument.Parse(md).OfType<MdTable>().Count() >= 3);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MeuEditorDeVideo.sln"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private sealed class FakePaths : IAppPaths
    {
        public string RoamingData => @"C:\roaming";
        public string LocalData => @"C:\dados";
        public string Logs => @"C:\dados\logs";
        public string Cache => @"C:\dados\cache";
        public string Autosave => @"C:\dados\autosave";
        public string AiModels => @"C:\dados\ai-models";
        public string Backups => @"C:\dados\backups";
        public string SettingsFile => @"C:\roaming\settings.json";
    }
}
