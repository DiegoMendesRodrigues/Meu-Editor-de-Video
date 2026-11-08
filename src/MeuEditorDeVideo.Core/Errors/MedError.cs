namespace MeuEditorDeVideo.Core.Errors;

/// <summary>
/// Erro de domínio/aplicação com código estável e mensagem em Português do Brasil (§7.4, RNF010).
/// Mensagens nunca devem conter conteúdo audiovisual do usuário (§13).
/// </summary>
public sealed record MedError(string Code, string Message, Exception? Cause = null)
{
    public static MedError Unexpected(Exception ex) =>
        new("MED-0000", "Ocorreu um erro inesperado. Os detalhes técnicos foram registrados no log.", ex);

    public override string ToString() => $"[{Code}] {Message}";
}

/// <summary>Exceção que transporta um <see cref="MedError"/> através de camadas assíncronas.</summary>
public sealed class MedException(MedError error) : Exception(error.Message, error.Cause)
{
    public MedError Error { get; } = error;
}
