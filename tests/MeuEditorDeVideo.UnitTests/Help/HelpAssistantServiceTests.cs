using System.Runtime.CompilerServices;
using MeuEditorDeVideo.AIEngine.Assistant;
using MeuEditorDeVideo.Application.Help;
using MeuEditorDeVideo.Core.Help;

namespace MeuEditorDeVideo.UnitTests.Help;

/// <summary>Assistente virtual: busca com contexto da conversa, montagem das mensagens para o modelo e fontes citadas.</summary>
public sealed class HelpAssistantServiceTests
{
    private static HelpSection Section(int line, string title, string text = "Texto da seção.", string chapter = "Capítulo") =>
        new("pt-BR", line, 3, chapter, title, text);

    private sealed class FakeKnowledge(Func<string, IReadOnlyList<HelpSearchHit>> search) : IHelpKnowledgeBase
    {
        public List<string> Queries { get; } = [];
        public IReadOnlyList<HelpSection> Sections(string language) => [];

        public IReadOnlyList<HelpSearchHit> Search(string question, string language, int max = 4)
        {
            Queries.Add(question);
            return search(question);
        }
    }

    private sealed class FakeGenerator(params string[] pieces) : IHelpAnswerGenerator
    {
        public IReadOnlyList<HelpChatMessage>? Received { get; private set; }
        public bool IsAvailable => true;

        public async IAsyncEnumerable<string> GenerateAsync(IReadOnlyList<HelpChatMessage> messages, int maxTokens = 512,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Received = messages;
            foreach (var p in pieces)
            {
                await Task.Yield();
                yield return p;
            }
        }

        public void Unload() { }
    }

    [Fact]
    public void Pergunta_de_continuacao_sem_resultado_usa_a_pergunta_anterior()
    {
        var hit = new HelpSearchHit(Section(190, "Velocidade do clipe"), 5);
        var kb = new FakeKnowledge(q => q.Contains("velocidade") ? [hit] : []);
        var service = new HelpAssistantService(kb, new FakeGenerator());

        var hits = service.Find("e para acelerar?", "pt-BR", [new HelpExchange("Como mudo a velocidade?", "Use a seção Velocidade.")]);

        Assert.Equal([hit], hits);
        Assert.Equal(["e para acelerar?", "Como mudo a velocidade? e para acelerar?"], kb.Queries);
        Assert.Empty(service.Find("e para acelerar?", "pt-BR"));
    }

    [Fact]
    public void Mensagens_tem_instrucoes_no_idioma_conversa_recente_e_trechos_com_a_fonte()
    {
        var hits = new[] { new HelpSearchHit(Section(190, "Velocidade do clipe", "Use **Velocidade** de 0,25× a 4×.", "Editar"), 9) };
        var history = Enumerable.Range(1, 3).Select(i => new HelpExchange($"Pergunta {i}", $"Resposta {i}")).ToList();

        var pt = HelpAssistantService.BuildMessages("Qual a menor velocidade?", "pt-BR", hits, history);
        var en = HelpAssistantService.BuildMessages("What is the slowest speed?", "en", hits, []);

        Assert.Equal(HelpChatRole.System, pt[0].Role);
        Assert.Contains("português do Brasil", pt[0].Content);
        Assert.Contains("Answer in English", en[0].Content);
        // Só as duas últimas trocas da conversa.
        Assert.Equal(["Pergunta 2", "Resposta 2", "Pergunta 3", "Resposta 3"], pt.Skip(1).Take(4).Select(m => m.Content));
        var user = pt[^1];
        Assert.Equal(HelpChatRole.User, user.Role);
        Assert.Contains("### Editar › Velocidade do clipe", user.Content);
        Assert.Contains("0,25×", user.Content);
        Assert.EndsWith("Qual a menor velocidade?", user.Content);
        Assert.StartsWith("Manual excerpts:", en[^1].Content);
    }

    [Fact]
    public void Trechos_longos_sao_cortados_para_caber_no_modelo()
    {
        var longo = new string('a', 10_000);
        var hits = Enumerable.Range(0, 4).Select(i => new HelpSearchHit(Section(i + 1, $"Seção {i}", longo), 4 - i)).ToList();

        var user = HelpAssistantService.BuildMessages("?", "pt-BR", hits, [])[^1].Content;

        Assert.True(user.Length < 7_500, $"{user.Length} caracteres");
        Assert.Contains("Seção 0", user);
        Assert.Contains("…", user);
    }

    [Fact]
    public void Fontes_sao_as_secoes_proximas_da_melhor()
    {
        var hits = new[]
        {
            new HelpSearchHit(Section(1, "A"), 10), new HelpSearchHit(Section(2, "B"), 7), new HelpSearchHit(Section(3, "C"), 5.9),
        };
        Assert.Equal(["A", "B"], HelpAssistantService.Sources(hits).Select(s => s.Title));
        Assert.Empty(HelpAssistantService.Sources([]));
    }

    [Fact]
    public async Task Resposta_vem_do_gerador_em_pedacos()
    {
        var generator = new FakeGenerator("Use ", "**Dividir** ", "(S).");
        var service = new HelpAssistantService(new FakeKnowledge(_ => []), generator);
        var hits = new[] { new HelpSearchHit(Section(152, "Mover, cortar e dividir"), 3) };

        var parts = new List<string>();
        await foreach (var p in service.AnswerAsync("Como divido?", "pt-BR", hits)) parts.Add(p);

        Assert.Equal("Use **Dividir** (S).", string.Concat(parts));
        Assert.Contains("Mover, cortar e dividir", generator.Received![^1].Content);
    }

    [Fact]
    public async Task Resposta_em_laco_para_no_paragrafo_repetido()
    {
        const string p1 = "Selecione o clipe e posicione o cursor no ponto do corte.", p2 = "Depois pressione S ou clique em Dividir na barra.";
        var generator = new FakeGenerator(p1, "\n\n", p2, "\n\n", "Selecione o clipe e posicione ", "o cursor no ponto do corte.", "\n\n", p2);
        var service = new HelpAssistantService(new FakeKnowledge(_ => []), generator);

        var parts = new List<string>();
        await foreach (var p in service.AnswerAsync("?", "pt-BR", [])) parts.Add(p);

        Assert.Equal($"{p1}\n\n{p2}\n\n", string.Concat(parts));
    }

    [Fact]
    public void Resposta_aparece_sem_marcas_de_Markdown()
    {
        Assert.Equal("Resumo\nClique em Dividir (S).", HelpAssistantService.PlainText("## Resumo\nClique em **Dividir** (S)."));
    }

    [Fact]
    public void Formato_de_conversa_do_Phi()
    {
        var prompt = GenAiAnswerGenerator.PhiPrompt(
        [
            new(HelpChatRole.System, "Regras"), new(HelpChatRole.User, "Oi"), new(HelpChatRole.Assistant, "Olá"), new(HelpChatRole.User, "E aí?"),
        ]);
        Assert.Equal("<|system|>Regras<|end|><|user|>Oi<|end|><|assistant|>Olá<|end|><|user|>E aí?<|end|><|assistant|>", prompt);
    }

    [Fact]
    public void Breadcrumb_do_capitulo_e_da_secao()
    {
        Assert.Equal("Áudio › Mixer", new HelpSection("pt-BR", 1, 3, "Áudio", "Mixer", "x").Breadcrumb);
        Assert.Equal("Privacidade", new HelpSection("pt-BR", 1, 2, "Privacidade", "Privacidade", "x").Breadcrumb);
    }
}
