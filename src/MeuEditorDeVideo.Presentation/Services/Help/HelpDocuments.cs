using System.Text;

namespace MeuEditorDeVideo.Presentation.Services.Help;

/// <summary>Documentos em Markdown das janelas Ajuda › Atalhos de teclado e Ajuda › Informações do sistema.</summary>
public static class HelpDocuments
{
    /// <summary>Tabela de atalhos por categoria; uma ação sem atalho aparece como "—".</summary>
    public static string ShortcutsMarkdown(IEnumerable<ShortcutAction> actions, Func<string, IReadOnlyList<KeyChord>> chords)
    {
        var sb = new StringBuilder("# Atalhos de teclado\n\n> Para mudar um atalho, use **Ferramentas › Configurações… › Atalhos**.\n");
        foreach (var group in actions.GroupBy(a => a.Category))
        {
            sb.Append("\n## ").Append(group.Key).Append("\n\n| Ação | Atalho |\n|---|---|\n");
            foreach (var action in group)
            {
                var keys = chords(action.Id).Select(c => c.Display).ToList();
                sb.Append("| ").Append(action.Label).Append(" | ").Append(keys.Count > 0 ? string.Join(" ou ", keys) : "—").Append(" |\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>Converte o relatório ("Rótulo: valor" por linha) numa tabela; listas viram itens.</summary>
    public static string SystemInfoMarkdown(string report)
    {
        var sb = new StringBuilder("# Informações do sistema\n\n");
        var inTable = false;
        foreach (var raw in report.Replace("\r\n", "\n").Split('\n').Skip(1))
        {
            var line = raw.TrimEnd();
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (inTable) { sb.Append('\n'); inTable = false; }
                sb.Append("- ").Append(line[4..]).Append('\n');
            }
            else if (colon > 0)
            {
                if (!inTable) { sb.Append("\n| Item | Valor |\n|---|---|\n"); inTable = true; }
                sb.Append("| ").Append(line[..colon]).Append(" | ").Append(line[(colon + 2)..].Replace("|", "/")).Append(" |\n");
            }
            else if (line.Length > 0)
            {
                if (inTable) { sb.Append('\n'); inTable = false; }
                sb.Append("\n**").Append(line.TrimEnd(':')).Append("**\n\n");
            }
            else if (inTable)
            {
                sb.Append('\n');
                inTable = false;
            }
        }
        return sb.ToString();
    }
}
