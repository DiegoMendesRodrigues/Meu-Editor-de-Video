namespace MeuEditorDeVideo.Core.Errors;

/// <summary>Catálogo de erros com mensagens em Português do Brasil (§7.4).</summary>
public static class MedErrors
{
    public static MedError ProjectFileNotFound(string path) =>
        new("MED-1001", $"O arquivo de projeto não foi encontrado:\n{path}");

    public static MedError NotAProject(string path, Exception? cause = null) =>
        new("MED-1002", $"O arquivo não é um projeto válido do Meu Editor de Vídeo ou está danificado:\n{path}", cause);

    public static MedError NewerFormat(string version, string supported) =>
        new("MED-1003", $"Este projeto foi criado com uma versão mais nova do aplicativo (formato {version}). " +
                        $"Esta versão abre projetos até o formato {supported}. Atualize o Meu Editor de Vídeo.");

    public static MedError IntegrityFailed(string entry) =>
        new("MED-1004", $"A verificação de integridade do projeto falhou (arquivo interno \"{entry}\" diferente do esperado). " +
                        "O arquivo pode estar danificado. Tente abrir uma cópia de segurança.");

    public static MedError DiskFull(Exception cause) =>
        new("MED-1005", "Não há espaço suficiente em disco para salvar o projeto. Libere espaço e tente novamente. " +
                        "O arquivo anterior foi preservado.", cause);

    public static MedError AccessDenied(string path, Exception cause) =>
        new("MED-1006", $"Sem permissão para acessar:\n{path}", cause);

    public static MedError InvalidProject(IEnumerable<string> problems) =>
        new("MED-1007", "O projeto contém dados inconsistentes:\n• " + string.Join("\n• ", problems));

    public static MedError SaveFailed(string path, Exception cause) =>
        new("MED-1008", $"Não foi possível salvar o projeto em:\n{path}\nO arquivo anterior foi preservado.", cause);

    public static MedError MediaUnavailableForPackaging(string name) =>
        new("MED-1009", $"Não é possível empacotar o projeto: a mídia \"{name}\" não foi encontrada. Localize-a antes de salvar.");

    public static MedError FileInUse(string path, Exception cause) =>
        new("MED-1010", $"O arquivo está em uso por outro programa:\n{path}", cause);

    public static MedError SequencePackagingUnsupported(string name) =>
        new("MED-1011", $"A sequência de imagens \"{name}\" ainda não pode ser empacotada no projeto. Mantenha as mídias vinculadas.");

    // 2xxx — mídia
    public static MedError MediaEngineUnavailable(string detail) =>
        new("MED-2001", $"O mecanismo de mídia (FFmpeg) não pôde ser carregado. Reinstale o aplicativo.\n\nDetalhe: {detail}");
}
