// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
namespace MeuEditorDeVideo.Core.Help;

/// <summary>
/// Seção do manual do usuário (título <c>##</c> ou <c>###</c> e o texto até o próximo título), unidade da base de
/// conhecimento do assistente virtual.
/// </summary>
/// <param name="Line">Linha (1 = primeira) do título no arquivo. Os manuais dos seis idiomas têm a mesma estrutura, então a
/// linha identifica a mesma seção em qualquer idioma.</param>
/// <param name="Chapter">Título do capítulo (<c>##</c>) que contém a seção; igual a <paramref name="Title"/> no próprio capítulo.</param>
/// <param name="Level">2 para capítulo, 3 para seção.</param>
/// <param name="Text">Texto da seção em Markdown, sem a linha do título.</param>
public sealed record HelpSection(string Language, int Line, int Level, string Chapter, string Title, string Text)
{
    /// <summary>Caminho mostrado como fonte: "Capítulo › Seção" (ou só o capítulo).</summary>
    public string Breadcrumb => Level <= 2 || Chapter == Title ? Title : $"{Chapter} › {Title}";
}

/// <summary>Seção encontrada pela busca, com a pontuação de relevância (maior = mais relevante).</summary>
public sealed record HelpSearchHit(HelpSection Section, double Score);

/// <summary>
/// Base de conhecimento do assistente virtual (etapas 1 e 2): os manuais em seis idiomas divididos em seções e a busca
/// das seções que respondem a uma pergunta. Tudo local, sem modelo de IA.
/// </summary>
public interface IHelpKnowledgeBase
{
    /// <summary>Seções do manual no idioma (código do aplicativo, ex.: <c>pt-BR</c>, <c>en</c>); o pt-BR é o padrão.</summary>
    IReadOnlyList<HelpSection> Sections(string language);

    /// <summary>As seções mais relevantes para a pergunta, da mais para a menos relevante; vazio se nada combinar.</summary>
    IReadOnlyList<HelpSearchHit> Search(string question, string language, int max = 4);
}

/// <summary>Papel de uma mensagem enviada ao modelo de linguagem.</summary>
public enum HelpChatRole
{
    System,
    User,
    Assistant,
}

/// <summary>Mensagem da conversa enviada ao modelo de linguagem.</summary>
public sealed record HelpChatMessage(HelpChatRole Role, string Content);

/// <summary>
/// Modelo de linguagem local do assistente virtual (etapa 3): escreve a resposta, palavra por palavra, a partir das
/// mensagens já montadas (instruções, trechos do manual e pergunta). Opcional: sem o modelo instalado, o assistente mostra
/// só as seções encontradas.
/// </summary>
public interface IHelpAnswerGenerator
{
    /// <summary>O modelo está instalado (pode ser usado).</summary>
    bool IsAvailable { get; }

    /// <summary>Gera a resposta em pedaços de texto. O modelo é carregado na primeira chamada.</summary>
    /// <param name="maxTokens">Limite de tokens da resposta.</param>
    IAsyncEnumerable<string> GenerateAsync(IReadOnlyList<HelpChatMessage> messages, int maxTokens = 512,
        CancellationToken cancellationToken = default);

    /// <summary>Libera o modelo da memória (é recarregado na próxima pergunta).</summary>
    void Unload();
}
