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

    // 3xxx — renderização e exportação (§7.4)
    public static MedError RenderMissingMedia(IEnumerable<string> names) =>
        new("MED-3001", "Não é possível exportar: as mídias abaixo não foram encontradas. Localize-as e tente novamente.\n• " +
                        string.Join("\n• ", names));

    public static MedError EncoderUnavailable(string codec) =>
        new("MED-3002", $"Nenhum codificador disponível para {codec} neste computador. Escolha outro codec ou formato.");

    public static MedError RenderDecodeFailed(string media, Exception cause) =>
        new("MED-3003", $"Falha ao decodificar a mídia \"{media}\" durante a exportação. O arquivo pode estar danificado.", cause);

    public static MedError RenderDiskFull(string folder, Exception? cause = null) =>
        new("MED-3004", $"Não há espaço suficiente em disco para concluir a exportação em:\n{folder}\n" +
                        "Libere espaço e retome a exportação; os trechos já prontos serão aproveitados.", cause);

    public static MedError RenderOutOfMemory(Exception cause) =>
        new("MED-3005", "Memória insuficiente para continuar a exportação. Feche outros programas ou reduza a resolução e tente novamente.", cause);

    public static MedError EncodeFailed(string encoder, Exception cause) =>
        new("MED-3006", $"O codificador \"{encoder}\" falhou durante a exportação.\n\nDetalhe: {cause.Message}", cause);

    public static MedError GpuEncoderFailed(string encoder, Exception cause) =>
        new("MED-3007", $"O codificador de vídeo da placa de vídeo (\"{encoder}\") falhou. " +
                        "Tente novamente com o codificador por software (opção \"Codificador: software\").", cause);

    public static MedError InvalidExportSettings(IEnumerable<string> problems) =>
        new("MED-3008", "As configurações de exportação são inválidas:\n• " + string.Join("\n• ", problems));

    public static MedError OutputValidationFailed(string path, string detail) =>
        new("MED-3009", $"O arquivo exportado não passou na verificação final:\n{path}\n\nDetalhe: {detail}");

    public static MedError OutputNotWritable(string path, Exception cause) =>
        new("MED-3010", $"Não foi possível gravar o arquivo de saída (sem permissão ou em uso por outro programa):\n{path}", cause);

    public static MedError NothingToRender() =>
        new("MED-3011", "Não há nada para exportar: a sequência (ou o intervalo escolhido) está vazia.");
}
