using Avalonia.Platform.Storage;

namespace MeuEditorDeVideo.Desktop.Services;

/// <summary>
/// Converte os filtros no formato dos ViewModels ("Descrição (*.med)|*.med|Todos (*.*)|*.*", herdado do WPF) para
/// os tipos de arquivo dos seletores do Avalonia.
/// </summary>
public static class FileFilters
{
    public static List<FilePickerFileType> Parse(string? filter)
    {
        var result = new List<FilePickerFileType>();
        if (string.IsNullOrWhiteSpace(filter)) return result;
        var parts = filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new FilePickerFileType(Translator.T(parts[i].Trim()))
            {
                Patterns = patterns,
                // macOS: o seletor nativo filtra por tipo; "public.item" quando o filtro aceita tudo.
                AppleUniformTypeIdentifiers = patterns.Contains("*.*") || patterns.Contains("*") ? ["public.item"] : null,
            });
        }
        return result;
    }

    /// <summary>Extensão padrão (".med") do primeiro padrão do filtro, para "Salvar como" acrescentar quando faltar.</summary>
    public static string? DefaultExtension(string? filter) =>
        Parse(filter).SelectMany(t => t.Patterns ?? []).FirstOrDefault(p => p.StartsWith("*.", StringComparison.Ordinal) && p != "*.*")?[2..];
}
