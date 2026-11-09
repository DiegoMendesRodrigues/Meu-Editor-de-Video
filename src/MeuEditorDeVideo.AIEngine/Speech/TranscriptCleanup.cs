using System.Text.RegularExpressions;

namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>Limpeza do texto transcrito: normaliza espaços e descarta marcadores não verbais do Whisper.</summary>
public static partial class TranscriptCleanup
{
    // Um marcador é um trecho inteiro entre colchetes, parênteses ou asteriscos: [Música], (risos), *music*.
    private const string Marker = @"(?:\[[^\[\]]*\]|\([^()]*\)|\*[^*]*\*)";

    [GeneratedRegex(@"^" + Marker + @"(?:\s*" + Marker + @")*$")]
    private static partial Regex MarkersOnly();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Texto limpo, ou "" quando o trecho contém só marcadores não verbais.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var collapsed = Spaces().Replace(text.Trim(), " ");
        return MarkersOnly().IsMatch(collapsed) ? "" : collapsed;
    }
}
