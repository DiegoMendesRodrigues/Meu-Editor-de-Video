// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Export;

namespace MeuEditorDeVideo.Presentation.Services;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Gravação concluída na janela Gravar tela.</summary>
public sealed record ScreenRecordingResult(string Path, bool InsertOnTimeline);

/// <summary>Escolhas da janela Reenquadrar automaticamente.</summary>
/// <param name="FollowHead">Acompanhar a cabeça (true), o corpo (false) ou só centralizar (null).</param>
public sealed record ReframeChoice(int AspectWidth, int AspectHeight, bool? FollowHead);

/// <summary>Pedido da janela Texto para fala.</summary>
public sealed record TextToSpeechRequest(string Text, string? Voice, int Rate);

/// <summary>
/// Janelas e diálogos do sistema pedidos pelos ViewModels. A implementação fica no aplicativo (toolkit de interface);
/// os testes usam versões falsas.
/// </summary>
public interface IDialogService
{
    void ShowError(MedError error);

    void ShowInfo(string title, string message);

    bool Confirm(string title, string message);

    /// <summary>"Deseja salvar as alterações?" — Sim / Não / Cancelar.</summary>
    UnsavedChoice AskSaveChanges(string projectName);

    string? PickOpenProject(string? initialDirectory);

    string? PickSaveProject(string suggestedName, string? initialDirectory);

    string? PickFile(string title, string filter, string? initialDirectory);

    /// <summary>Escolhe onde salvar um arquivo genérico (ex.: captura de quadro). Null se cancelado.</summary>
    string? PickSaveFile(string title, string filter, string suggestedName, string? initialDirectory);

    string? PickFolder(string title, string? initialDirectory);

    /// <summary>Seleção de vários arquivos (RF026). Lista vazia se cancelado.</summary>
    IReadOnlyList<string> PickFiles(string title, string filter, string? initialDirectory);

    /// <summary>Pede um texto ao usuário. Retorna null se cancelado.</summary>
    string? Prompt(string title, string label, string initialValue);

    /// <summary>Escolha de uma opção numa lista. Índice escolhido, ou null se cancelado.</summary>
    int? Choose(string title, string label, IReadOnlyList<string> options, int selected = 0, string okText = "OK");

    /// <summary>Abre a janela de configurações. Retorna true se o usuário salvou.</summary>
    bool ShowSettings();

    /// <summary>Novo projeto. True se o usuário confirmou (resultado em <see cref="NewProjectViewModel.Result"/>).</summary>
    bool ShowNewProject(NewProjectViewModel viewModel);

    void ShowMissingMedia(MissingMediaViewModel viewModel);

    /// <summary>Ferramentas › Pacotes de conteúdo. True se algum pacote foi instalado ou removido.</summary>
    bool ManageContentPacks(IContentPackStore store);

    /// <summary>Ferramentas › Gravar tela. Null se nada foi gravado.</summary>
    ScreenRecordingResult? RecordScreen(IScreenRecorder recorder, string outputPath);

    /// <summary>Tela de exportação. True se um trabalho foi criado (<see cref="ExportViewModel.CreatedJob"/>).</summary>
    bool ShowExport(ExportViewModel viewModel);

    /// <summary>
    /// Fila aberta (modal) até o trabalho <see cref="RenderQueueViewModel.FollowJob"/> terminar. Retorna o trabalho
    /// terminado, ou null se o usuário fechou a janela antes.
    /// </summary>
    RenderJob? ShowExportProgress(RenderQueueViewModel queue);

    /// <summary>Fila de renderização em segundo plano (não modal, uma só janela).</summary>
    void ShowRenderQueue(Func<RenderQueueViewModel> create);

    ReframeChoice? AskReframe(string currentSize, bool trackingAvailable);

    TextToSpeechRequest? AskTextToSpeech(IReadOnlyList<string> voices);

    /// <summary>Resultado de Verificar atualizações: versão nova disponível ou programa atualizado.</summary>
    void ShowUpdateResult(UpdateInfo info, bool isNewer);
}
