using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MeuEditorDeVideo.Presentation.Localization;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>
/// Nenhum botão fica sem tradução. Um texto igual no outro idioma não tem linha no .tsv: entra nas exceções abaixo.
/// Lê os fontes e os .tsv, sem ligar o tradutor (que é estático e afetaria os outros testes).
/// </summary>
public partial class BotoesTraducaoTests
{
    private static readonly string[] Botoes =
        ["Button", "ToggleButton", "RepeatButton", "HyperlinkButton", "DropDownButton", "SplitButton", "ToggleSplitButton"];

    // Siglas e nomes iguais em todos os idiomas: mudo/solo do mixer, LinkedIn, OK e proxy.
    // N, I e S do painel de texto são trocados em código pela inicial traduzida (ClipTextPanel).
    private static readonly string[] IguaisEmTodos = ["M", "S", "N", "I", "in", "OK", "Proxy"];

    private static readonly Dictionary<string, string[]> Iguais = new()
    {
        ["en"] = [],
        ["fr"] = [],
        ["de"] = [],
        ["es"] =
        [
            "+ Elipse", "Abrir", "Aplicar", "Cancelar", "Continuar", "Copiar", "Duplicar", "Exportar",
            "Exportar SRT/VTT…", "Exportar…", "Girar 90° ↺", "Girar 90° ↻", "Importar SRT/VTT…", "Importar SVG…",
            "Instalar", "Instalar todos", "Localizar…", "Normalizar", "Rastrear", "Reinstalar", "Restaurar todos",
            "Seguir", "Sincronizar", "Soltar", "Textos", "Usar", "Vídeos",
        ],
        ["pt-PT"] =
        [
            "+ Adicionar no cursor", "+ Elipse", "+ Livre", "+ Retângulo", "◇ Forma-chave", "Abrir",
            "Abrir no manual", "Abrir pasta dos pacotes", "Adicionar no cursor", "Agora não",
            "Analisar e estabilizar", "Aplicar", "Aplicar ao clipe selecionado", "Aplicar o limite agora",
            "Aplicar recorte ao vídeo", "Assistente virtual", "Atenuar sob a fala", "Atualizar", "Áudios", "Beleza",
            "Cabeça", "Calcular máscaras em segundo plano", "Cancelar", "Combinar cor com o clipe anterior",
            "Continuar", "Copiar", "Cor", "Corpo", "Cortar vídeos nas batidas", "Criar projeto", "Desfocar rostos",
            "Duplicar", "Editar no monitor", "Editor de legendas…", "Efeitos", "Elementos Gráficos", "Escolher…",
            "Exportar", "Exportar SRT/VTT…", "Exportar…", "Fechar", "Gerar e inserir", "Gerar legendas", "Gravar",
            "Imagens", "Importar SRT/VTT…", "Importar SVG…", "Início no cursor", "Instalar", "Instalar pacote…",
            "Instalar todos", "Ir para", "Isolar voz (voz e ambiente separados)", "Limpar cache",
            "Limpar concluídas", "Localizar…", "Marcar batidas", "Melhorar imagem (ampliar 2×)", "Mostrar na pasta",
            "Mostrar região no monitor", "Não", "Normalizar", "Outra…", "Parar", "Procurar em pasta…", "Procurar…",
            "Reduzir ruído", "Reenquadrar", "Reinstalar", "Remover", "Remover atenuação", "Remover silêncios",
            "Remover trechos sem fala", "Repetir trecho", "Restaurar todos", "Retomar", "Seguir", "Sim",
            "Sincronizar", "Soltar", "Textos", "Transições", "Usar", "Verificar integridade", "Vídeos",
            "Virar cartão 3D",
        ],
    };

    public static TheoryData<string> Idiomas => ["en", "es", "fr", "de", "pt-PT"];

    [Theory]
    [MemberData(nameof(Idiomas))]
    public void Todo_botao_tem_traducao(string idioma)
    {
        var textos = TextosDosBotoes();
        Assert.True(textos.Count >= 110, $"só {textos.Count} textos de botão");
        var traduzidos = Traduzidos(idioma);
        var faltas = textos.Where(t => !traduzidos.Contains(Translator.IdOf(t)))
            .Except(IguaisEmTodos).Except(Iguais[idioma]).Order().ToList();
        Assert.True(faltas.Count == 0, $"Sem tradução em {idioma}:\n" + string.Join('\n', faltas));
    }

    /// <summary>Os botões do Histórico mostram as descrições dos comandos (montadas com valores).</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    public void Descricoes_dos_botoes_do_historico_tem_traducao(string idioma)
    {
        var traduzidos = Traduzidos(idioma);
        var origem = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(Raiz(), "resources", "i18n", "origem.json")))!;
        var modelos = origem.Where(t => traduzidos.Contains(Translator.IdOf(t))).Select(SemValores).ToHashSet();

        var descricoes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var projeto in new[] { "Domain", "Application", "Presentation" })
            foreach (var arquivo in Fontes(projeto, ".cs"))
                foreach (Match trecho in Descricao().Matches(File.ReadAllText(arquivo)))
                    foreach (Match texto in Literal().Matches(trecho.Value))
                    {
                        var t = texto.Groups[1].Value;
                        // Literais com expressões entre aspas (formatos) não dá para ler por expressão regular.
                        if (t.Count(c => c == '{') == t.Count(c => c == '}') && !t.Contains('\\')) descricoes.Add(t);
                    }

        descricoes.UnionWith(["elipse", "livre"]); // valores de "Adicionar máscara ({0})"
        Assert.True(descricoes.Count >= 50, $"só {descricoes.Count} descrições");
        var faltas = descricoes.Where(t => !modelos.Contains(SemValores(t))).ToList();
        Assert.True(faltas.Count == 0, $"Sem tradução em {idioma}:\n" + string.Join('\n', faltas));
    }

    private static HashSet<string> TextosDosBotoes()
    {
        var recursos = Fontes("Desktop", ".axaml").Where(f => f.Contains("Strings", StringComparison.Ordinal))
            .SelectMany(f => XDocument.Load(f).Root!.Elements())
            .ToDictionary(e => e.Attributes().First(a => a.Name.LocalName == "Key").Value, e => e.Value);

        var textos = new HashSet<string>(StringComparer.Ordinal);
        void Juntar(string? valor)
        {
            if (valor is null) return;
            if (Recurso().Match(valor) is { Success: true } r && recursos.TryGetValue(r.Groups[1].Value, out var texto)) valor = texto;
            else if (valor.StartsWith('{')) return; // ligação: textos do código, conferidos abaixo
            if (valor.Any(char.IsLetter)) textos.Add(valor.Trim());
        }

        void Percorrer(XElement e)
        {
            var nome = e.Name.LocalName;
            if (nome.EndsWith(".Flyout", StringComparison.Ordinal) || nome.EndsWith(".ContextMenu", StringComparison.Ordinal)
                || nome.StartsWith("ToolTip", StringComparison.Ordinal)) return;
            if (nome == "Setter")
            {
                if ((string?)e.Attribute("Property") is "Content" or "Text") Juntar((string?)e.Attribute("Value"));
                return;
            }
            Juntar((string?)e.Attribute("Content"));
            Juntar((string?)e.Attribute("Text"));
            if (nome is "TextBlock" or "Run" || Botoes.Contains(nome))
                foreach (var t in e.Nodes().OfType<XText>()) Juntar(t.Value);
            foreach (var filho in e.Elements()) Percorrer(filho);
        }

        foreach (var arquivo in Fontes("Desktop", ".axaml"))
            foreach (var botao in XDocument.Load(arquivo).Descendants().Where(e => Botoes.Contains(e.Name.LocalName)))
                Percorrer(botao);

        // Botões das janelas montadas em código e rótulos que chegam a eles por parâmetro ou ligação.
        foreach (var arquivo in Fontes("Desktop", ".cs").Concat(Fontes("Presentation", ".cs")))
        {
            var codigo = File.ReadAllText(arquivo);
            foreach (Match m in BotaoEmCodigo().Matches(codigo)) Juntar(m.Groups[1].Value);
            foreach (var trecho in RotulosPorParametro().Matches(codigo).Select(m => m.Groups[1].Value))
                foreach (Match m in Literal().Matches(trecho)) Juntar(m.Groups[1].Value);
        }
        return textos;
    }

    private static HashSet<string> Traduzidos(string idioma) =>
        File.ReadLines(Path.Combine(Raiz(), "resources", "i18n", idioma + ".tsv")).Select(l => l.Split('\t')[0]).ToHashSet();

    private static string SemValores(string texto) => Valor().Replace(texto, "{}");

    private static IEnumerable<string> Fontes(string projeto, string extensao) =>
        Directory.EnumerateFiles(Path.Combine(Raiz(), "src", "MeuEditorDeVideo." + projeto), "*" + extensao, SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"));

    private static string Raiz()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "MeuEditorDeVideo.sln"))) pasta = pasta.Parent;
        return pasta?.FullName ?? throw new DirectoryNotFoundException("MeuEditorDeVideo.sln não encontrado.");
    }

    [GeneratedRegex(@"^\{(?:Static|Dynamic)Resource ([^}]+)\}$")]
    private static partial Regex Recurso();

    [GeneratedRegex("""(?:new (?:Button|ToggleButton|HyperlinkButton)\s*\{\s*Content = |DialogParts\.Accent\(|\bButton\(|_record\.Content = |(?:closeText|saveText) = Translator\.T\(|\.Choose\([^;]*,\s*)"([^"\n]+)"(?=\s*[,);}])""")]
    private static partial Regex BotaoEmCodigo();

    [GeneratedRegex("""(?:(?<=MessageKind\.\w+,\s*)|(?<=string ActionText => State switch\s*))([\[{][^\]}]+[\]}])""")]
    private static partial Regex RotulosPorParametro();

    [GeneratedRegex("""\bstring Description\b[^;"\n]*=[^;]*;|\bDescription = \$?"[^\n]*|new CompositeCommand\([^;\n]*|\bUpdateClip\([^;\n]*""")]
    private static partial Regex Descricao();

    [GeneratedRegex("""(?<=^|[^\\])"([A-ZÀ-Ý][A-Za-zÀ-ÿ][^"\n]*)"(?!\w)""")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Valor();
}
