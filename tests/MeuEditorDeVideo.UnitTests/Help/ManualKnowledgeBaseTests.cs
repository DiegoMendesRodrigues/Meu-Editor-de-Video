using System.IO;
using MeuEditorDeVideo.Application.Help;
using Xunit.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Help;

/// <summary>Base de conhecimento do assistente virtual: divisão dos manuais em seções e qualidade da busca.</summary>
public sealed class ManualKnowledgeBaseTests(ITestOutputHelper output)
{
    private static readonly ManualKnowledgeBase Manuals = new(Path.Combine(RepoRoot(), "documentation"));

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MeuEditorDeVideo.sln"))) return d.FullName;
        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    /// <summary>Perguntas de usuários (as mesmas do teste de modelos de linguagem) e a seção do manual (pt-BR) que responde.</summary>
    public static TheoryData<string, string, string> Perguntas() => new()
    {
        { "Abrir um projeto existente", "Como eu abro um projeto que salvei ontem?", "How do I open a project I saved yesterday?" },
        { "Como importar", "Qual o atalho para importar vídeos para o programa?", "What is the shortcut to import videos into the program?" },
        { "Proxies (versões leves para edição)", "O que é um proxy e como eu crio um?", "What is a proxy and how do I create one?" },
        { "Mover, cortar e dividir", "Como corto um clipe em duas partes?", "How do I cut a clip into two parts?" },
        { "Mover, cortar e dividir", "Quero apagar um clipe sem deixar um buraco na trilha. Como faço?", "I want to delete a clip without leaving a gap on the track. How?" },
        { "Ímã e edição em cascata", "Para que serve o ímã e qual a tecla?", "What is the magnet for and what is its key?" },
        { "Velocidade do clipe", "Como deixo um clipe em câmera lenta? Qual a menor velocidade?", "How do I put a clip in slow motion? What is the slowest speed?" },
        { "Desfazer e refazer", "Fiz besteira. Como desfaço e refaço?", "I made a mistake. How do I undo and redo?" },
        { "Máscaras, rastreamento e estabilização", "Meu vídeo está tremido. Tem como estabilizar?", "My video is shaky. Can I stabilize it?" },
        { "Inserir textos", "Como coloco o nome e o cargo de uma pessoa no canto do vídeo?", "How do I put a person's name and job title in the corner of the video?" },
        { "Legendas", "Consigo importar um arquivo SRT de legendas?", "Can I import an SRT subtitle file?" },
        { "Remover fundo", "Como tiro o fundo atrás de uma pessoa no vídeo?", "How do I remove the background behind a person in a video?" },
        { "Gerar legendas", "Como gerar legendas automáticas a partir da fala?", "How do I generate automatic subtitles from speech?" },
        { "Remover trechos sem fala", "Dá para cortar automaticamente as pausas em que ninguém fala?", "Can I automatically cut the pauses where nobody speaks?" },
        { "Parâmetros", "Na exportação com qualidade constante, que valor devo usar?", "When exporting with constant quality, what value should I use?" },
        { "Fila de renderização", "Minha exportação foi interrompida. Preciso começar do zero?", "My export was interrupted. Do I have to start from scratch?" },
        { "Salvamento automático", "De quanto em quanto tempo o programa salva sozinho? Posso mudar?", "How often does the program save automatically? Can I change it?" },
        { "Mover, cortar e dividir", "Como junto de novo as duas partes de um clipe que dividi?", "How do I join the two parts of a split clip back together?" },
        { "A pré-visualização está travando ou lenta", "A pré-visualização está travando. O que eu faço?", "The preview is stuttering. What should I do?" },
    };

    [Fact]
    public void Os_seis_manuais_tem_as_mesmas_secoes_nas_mesmas_linhas()
    {
        var pt = Manuals.Sections("pt-BR");
        Assert.True(pt.Count > 60, $"{pt.Count} seções");
        foreach (var lang in new[] { "en", "es", "fr", "de", "pt-PT" })
            Assert.Equal(pt.Select(s => s.Line), Manuals.Sections(lang).Select(s => s.Line));
        var velocidade = pt.Single(s => s.Title == "Velocidade do clipe");
        Assert.Equal("Editar na linha do tempo › Velocidade do clipe", velocidade.Breadcrumb);
        Assert.DoesNotContain("###", velocidade.Text);
    }

    [Fact]
    public void Busca_encontra_a_secao_certa_entre_as_quatro_primeiras_em_portugues_e_ingles()
    {
        var pt = Manuals.Sections("pt-BR");
        int total = 0, top1 = 0, top4 = 0;
        foreach (var row in Perguntas())
        {
            var (titulo, perguntaPt, perguntaEn) = ((string)row[0], (string)row[1], (string)row[2]);
            var linha = pt.First(s => s.Title == titulo).Line;
            foreach (var (lang, pergunta) in new[] { ("pt-BR", perguntaPt), ("en", perguntaEn) })
            {
                var hits = Manuals.Search(pergunta, lang, 4);
                var pos = hits.ToList().FindIndex(h => h.Section.Line == linha);
                total++;
                if (pos == 0) top1++;
                if (pos >= 0) top4++;
                if (pos != 0)
                    output.WriteLine($"[{lang}] {pergunta} → esperado '{titulo}' (posição {pos + 1}); veio: {string.Join(" | ", hits.Select(h => $"{h.Section.Title} {h.Score:0.0}"))}");
            }
        }
        output.WriteLine($"1º lugar: {top1}/{total}; entre os 4: {top4}/{total}");
        Assert.True(top4 >= total * 0.9, $"A seção certa ficou entre as 4 primeiras em só {top4} de {total} perguntas.");
    }

    [Fact]
    public void Busca_ignora_acentos_e_maiusculas_e_nao_acha_nada_sem_termos()
    {
        Assert.Equal(Manuals.Search("legendas automáticas", "pt-BR")[0].Section.Line, Manuals.Search("LEGENDAS AUTOMATICAS", "pt-BR")[0].Section.Line);
        Assert.Empty(Manuals.Search("como o de para", "pt-BR"));
        Assert.Empty(Manuals.Search("   ", "pt-BR"));
    }

    [Fact]
    public void Idioma_sem_manual_usa_o_portugues()
    {
        Assert.EndsWith("MANUAL_DO_USUARIO.md", Manuals.ManualPath("ja"));
        Assert.EndsWith("MANUAL_DO_USUARIO.de.md", Manuals.ManualPath("de"));
    }
}
