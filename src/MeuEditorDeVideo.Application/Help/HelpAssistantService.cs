using System.Runtime.CompilerServices;
using System.Text;
using MeuEditorDeVideo.Core.Help;

namespace MeuEditorDeVideo.Application.Help;

/// <summary>Pergunta e resposta anteriores da conversa (contexto para perguntas de continuação).</summary>
public sealed record HelpExchange(string Question, string Answer);

/// <summary>
/// Assistente virtual (Ajuda › Assistente virtual): busca no manual as seções que respondem à pergunta (etapas 1 e 2) e,
/// com o modelo de linguagem instalado, pede a ele uma resposta curta baseada só nesses trechos, no idioma do programa
/// (etapa 3). Sem o modelo, quem chama mostra as seções encontradas.
/// </summary>
public sealed class HelpAssistantService(IHelpKnowledgeBase knowledge, IHelpAnswerGenerator generator)
{
    /// <summary>Seções enviadas ao modelo.</summary>
    public const int ContextSections = 4;

    /// <summary>Limite da resposta: respostas longas de modelos pequenos tendem a divagar além dos trechos.</summary>
    public const int MaxAnswerTokens = 384;

    /// <summary>Limite de caracteres de cada seção e do conjunto (mantém a pergunta em ~3 mil tokens).</summary>
    private const int MaxSectionChars = 2400;
    private const int MaxContextChars = 7000;

    /// <summary>Respostas anteriores reenviadas ao modelo (perguntas de continuação, ex.: "e para acelerar?").</summary>
    private const int HistoryExchanges = 2;
    private const int MaxHistoryAnswerChars = 600;

    /// <summary>O modelo de linguagem está instalado: as respostas são escritas por ele.</summary>
    public bool HasLanguageModel => generator.IsAvailable;

    /// <summary>
    /// Seções que respondem à pergunta. Numa pergunta de continuação com poucos termos ("e no inglês?"), a pergunta anterior
    /// entra na busca quando a atual sozinha não encontra nada.
    /// </summary>
    public IReadOnlyList<HelpSearchHit> Find(string question, string language, IReadOnlyList<HelpExchange>? history = null)
    {
        var hits = knowledge.Search(question, language, ContextSections);
        if (hits.Count == 0 && history is { Count: > 0 })
            hits = knowledge.Search(history[^1].Question + " " + question, language, ContextSections);
        return hits;
    }

    /// <summary>Seções mostradas como fonte: a melhor e as que pontuaram pelo menos 60% dela (até 3).</summary>
    public static IReadOnlyList<HelpSection> Sources(IReadOnlyList<HelpSearchHit> hits)
    {
        if (hits.Count == 0) return [];
        var best = hits[0].Score;
        return hits.Where(h => h.Score >= best * 0.6).Take(3).Select(h => h.Section).ToList();
    }

    /// <summary>Escreve a resposta com o modelo de linguagem, em pedaços (streaming).</summary>
    public async IAsyncEnumerable<string> AnswerAsync(string question, string language, IReadOnlyList<HelpSearchHit> hits,
        IReadOnlyList<HelpExchange>? history = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = BuildMessages(question, language, hits, history ?? []);
        var text = new StringBuilder();
        var paragraphs = new HashSet<string>(StringComparer.Ordinal);
        var closed = 0;
        var pending = "";
        // Modelos pequenos às vezes entram em laço e repetem parágrafos inteiros. Enquanto o parágrafo em aberto começa
        // igual a um anterior, os pedaços ficam retidos; se a repetição se confirma (40 caracteres ou o parágrafo
        // inteiro), a resposta termina sem mostrá-la.
        await foreach (var piece in generator.GenerateAsync(messages, MaxAnswerTokens, cancellationToken).ConfigureAwait(false))
        {
            text.Append(piece);
            var all = text.ToString();
            var end = all.LastIndexOf("\n\n", StringComparison.Ordinal);
            if (end > closed)
            {
                foreach (var p in all[closed..end].Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (p.Length >= 20 && !paragraphs.Add(p)) yield break;
                closed = end;
            }
            var open = all[closed..].Trim();
            if (open.Length > 0 && paragraphs.Any(p => p.StartsWith(open, StringComparison.Ordinal)))
            {
                if (open.Length >= 40) yield break;
                pending += piece;
                continue;
            }
            yield return pending + piece;
            pending = "";
        }
        if (pending.Length > 0) yield return pending;
    }

    /// <summary>A resposta aparece como texto simples: tira as marcas de negrito e de título do Markdown que o modelo escreve.</summary>
    public static string PlainText(string markdown) =>
        System.Text.RegularExpressions.Regex.Replace(markdown.Replace("**", "").Replace("__", ""), @"(?m)^#{1,6}\s+", "");

    /// <summary>Mensagens enviadas ao modelo: instruções no idioma, conversa recente e a pergunta com os trechos do manual.</summary>
    public static IReadOnlyList<HelpChatMessage> BuildMessages(string question, string language, IReadOnlyList<HelpSearchHit> hits,
        IReadOnlyList<HelpExchange> history)
    {
        var text = Texts(language);
        var messages = new List<HelpChatMessage> { new(HelpChatRole.System, text.System) };
        foreach (var exchange in history.TakeLast(HistoryExchanges))
        {
            messages.Add(new(HelpChatRole.User, exchange.Question));
            messages.Add(new(HelpChatRole.Assistant, Trim(exchange.Answer, MaxHistoryAnswerChars)));
        }

        var context = new StringBuilder();
        // Só as seções próximas da melhor: trechos pouco relacionados fazem o modelo misturar assuntos.
        var best = hits.Count > 0 ? hits.Max(h => h.Score) : 0;
        foreach (var hit in hits.Where(h => h.Score >= best * 0.5).Take(ContextSections))
        {
            var s = hit.Section;
            var block = $"### {s.Breadcrumb}\n{Trim(s.Text, MaxSectionChars)}";
            if (context.Length > 0 && context.Length + block.Length > MaxContextChars) break;
            if (context.Length > 0) context.Append("\n\n");
            context.Append(block);
        }
        var user = context.Length == 0
            ? $"{text.Excerpts}:\n\n{text.None}\n\n---\n\n{question.Trim()}"
            : $"{text.Excerpts}:\n\n{context}\n\n---\n\n{question.Trim()}";
        messages.Add(new(HelpChatRole.User, user));
        return messages;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + " …";

    private sealed record PromptTexts(string System, string Excerpts, string None);

    /// <summary>Instruções do modelo em cada idioma do aplicativo (a resposta sai no idioma do programa).</summary>
    private static PromptTexts Texts(string language) => language switch
    {
        "en" => new(
            "You are the virtual assistant of the Meu Editor de Vídeo program. Answer in English, briefly and practically, using " +
            "ONLY the manual excerpts below. Quote menu, button and shortcut names exactly as they appear. If the answer is not in " +
            "the excerpts, say only that the manual does not describe that feature and do not make anything up; if it is, just " +
            "answer, without remarks about what the manual leaves out. Write everything in English.",
            "Manual excerpts", "(no excerpt found)"),
        "es" => new(
            "Eres el asistente virtual del programa Meu Editor de Vídeo. Responde en español, de forma breve y práctica, usando " +
            "SOLO los fragmentos del manual de abajo. Cita los nombres de menús, botones y atajos tal como aparecen. Si la respuesta " +
            "no está en los fragmentos, di solo que el manual no describe esa función y no inventes nada; si está, responde sin " +
            "comentar lo que el manual no explica. Escribe todo en español, sin palabras en inglés.",
            "Fragmentos del manual", "(ningún fragmento encontrado)"),
        "fr" => new(
            "Tu es l'assistant virtuel du logiciel Meu Editor de Vídeo. Réponds en français, de façon courte et pratique, en " +
            "utilisant UNIQUEMENT les extraits du manuel ci-dessous. Cite les noms des menus, boutons et raccourcis tels qu'ils " +
            "apparaissent. Si la réponse n'est pas dans les extraits, dis seulement que le manuel ne décrit pas cette fonction et " +
            "n'invente rien ; si elle y est, réponds sans commenter ce que le manuel n'explique pas. Écris tout en français, " +
            "sans mots anglais.",
            "Extraits du manuel", "(aucun extrait trouvé)"),
        "de" => new(
            "Du bist der virtuelle Assistent des Programms Meu Editor de Vídeo. Antworte auf Deutsch, kurz und praktisch, und " +
            "verwende NUR die folgenden Auszüge aus dem Handbuch. Nenne Menüs, Schaltflächen und Tastenkürzel genau so, wie sie " +
            "dort stehen. Steht die Antwort nicht in den Auszügen, sag nur, dass das Handbuch diese Funktion nicht beschreibt, und " +
            "erfinde nichts; steht sie darin, antworte ohne Bemerkungen darüber, was das Handbuch nicht erklärt. Schreibe alles " +
            "auf Deutsch, ohne englische Wörter.",
            "Auszüge aus dem Handbuch", "(kein Auszug gefunden)"),
        "pt-PT" => new(
            "És o assistente virtual do programa Meu Editor de Vídeo. Responde em português de Portugal, de forma curta e prática, " +
            "usando APENAS os excertos do manual abaixo. Cita os nomes de menus, botões e atalhos tal como aparecem. Se a resposta " +
            "não estiver nos excertos, diz apenas que o manual não descreve essa funcionalidade e não inventes; se estiver, responde " +
            "sem comentar o que o manual não explica. Escreve tudo em português, sem palavras em inglês.",
            "Excertos do manual", "(nenhum excerto encontrado)"),
        _ => new(
            "Você é o assistente virtual do programa Meu Editor de Vídeo. Responda em português do Brasil, de forma curta e " +
            "prática, usando SOMENTE os trechos do manual abaixo. Cite os nomes de menus, botões e atalhos como aparecem. Se a " +
            "resposta não estiver nos trechos, diga apenas que o manual não descreve esse recurso e não invente; se estiver, " +
            "responda sem comentar o que o manual não explica. Escreva tudo em português, sem palavras em inglês.",
            "Trechos do manual", "(nenhum trecho encontrado)"),
    };
}
