// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Application.Help;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Help;
using L = MeuEditorDeVideo.Presentation.Localization.Translator;

namespace MeuEditorDeVideo.Presentation.ViewModels;

/// <summary>Seção do manual citada numa resposta (botão "Abrir no manual").</summary>
public sealed class AssistantSourceViewModel(HelpSection section)
{
    public HelpSection Section { get; } = section;
    public string Title => Section.Title;
    public string Breadcrumb => Section.Breadcrumb;

    /// <summary>Começo do texto da seção, sem marcação, mostrado quando não há modelo de linguagem.</summary>
    public string Excerpt { get; } = Plain(section.Text, 320);

    private static string Plain(string markdown, int max)
    {
        var lines = markdown.Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '>', '|', ' ').Replace("**", "").Replace("`", ""))
            .Where(l => l.Length > 0 && !l.All(c => c is '-' or '|' or ':' or ' '));
        var text = string.Join(" ", lines);
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }
}

/// <summary>Mensagem da conversa com o assistente virtual.</summary>
public sealed partial class AssistantMessageViewModel(bool fromUser) : ObservableObject
{
    public bool FromUser { get; } = fromUser;
    public bool FromAssistant => !FromUser;

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>A resposta ainda está sendo escrita.</summary>
    [ObservableProperty]
    public partial bool IsWriting { get; set; }

    /// <summary>Seções do manual usadas na resposta.</summary>
    public ObservableCollection<AssistantSourceViewModel> Sources { get; } = [];

    /// <summary>Sem modelo de linguagem: as seções aparecem com um trecho do texto.</summary>
    [ObservableProperty]
    public partial bool ShowExcerpts { get; set; }

    public bool HasSources => Sources.Count > 0;

    public void SetSources(IEnumerable<HelpSection> sections)
    {
        Sources.Clear();
        foreach (var s in sections) Sources.Add(new AssistantSourceViewModel(s));
        OnPropertyChanged(nameof(HasSources));
    }
}

/// <summary>
/// Painel Ajuda › Assistente virtual: perguntas sobre o programa e a edição de vídeo respondidas a partir do manual,
/// no idioma do aplicativo. Com o modelo de linguagem (Phi-4-mini) instalado, a resposta é escrita por ele e cita a seção do
/// manual; sem ele, o painel mostra as seções encontradas e oferece a instalação. Tudo roda no computador.
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject, IDisposable
{
    /// <summary>Id do modelo de linguagem sugerido no catálogo.</summary>
    public static string SuggestedModelId { get; } = OperatingSystem.IsWindows() ? "phi4-mini" : "phi4-mini-cpu";

    private readonly HelpAssistantService _assistant;
    private readonly IModelRegistry _registry;
    private readonly IHelpAnswerGenerator _generator;
    private readonly List<HelpExchange> _history = [];
    private CancellationTokenSource? _cts;

    public AssistantViewModel(HelpAssistantService assistant, IModelRegistry registry, IHelpAnswerGenerator generator,
        ModelCatalogViewModel catalog)
    {
        _assistant = assistant;
        _registry = registry;
        _generator = generator;
        Catalog = catalog;
        SuggestedModel = catalog.Items.FirstOrDefault(i => i.Id == SuggestedModelId);
        _registry.Changed += OnRegistryChanged;
        HasLanguageModel = _assistant.HasLanguageModel;
        Suggestions =
        [
            L.T("Como divido um clipe em duas partes?"),
            L.T("Como gero legendas automáticas?"),
            L.T("Como removo o fundo de um vídeo?"),
            L.T("Qual qualidade usar para exportar?"),
        ];
    }

    /// <summary>Idioma das respostas e do manual (o do aplicativo).</summary>
    public static string Language => L.Language;

    /// <summary>Catálogo de modelos (instalação do modelo de linguagem pelo próprio painel).</summary>
    public ModelCatalogViewModel Catalog { get; }

    public CatalogItemViewModel? SuggestedModel { get; }

    public ObservableCollection<AssistantMessageViewModel> Messages { get; } = [];

    public IReadOnlyList<string> Suggestions { get; }

    public bool IsEmpty => Messages.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Question { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand), nameof(ClearCommand), nameof(AskCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>O modelo de linguagem está instalado (respostas em linguagem natural).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallCard))]
    public partial bool HasLanguageModel { get; set; }

    /// <summary>O usuário fechou o aviso de instalação nesta sessão.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallCard))]
    public partial bool InstallCardDismissed { get; set; }

    public bool ShowInstallCard => !HasLanguageModel && !InstallCardDismissed && SuggestedModel is not null;

    public string InstallText => SuggestedModel is null ? "" :
        L.F("Para respostas em linguagem natural, instale o modelo {0} ({1}). Ele roda neste computador; sem ele, o assistente mostra as seções do manual.",
            "Phi-4-mini", SuggestedModel.Download);

    /// <summary>Pede ao editor para abrir o manual numa seção.</summary>
    public event EventHandler<HelpSection>? OpenSectionRequested;

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync()
    {
        var question = Question.Trim();
        Question = "";
        return AskCoreAsync(question);
    }

    private bool CanAsk(string? question) => !IsBusy && !string.IsNullOrWhiteSpace(question);

    /// <summary>Pergunta sugerida (botões do painel vazio).</summary>
    [RelayCommand(CanExecute = nameof(CanAsk))]
    private Task AskAsync(string? question) => AskCoreAsync(question!.Trim());

    private async Task AskCoreAsync(string question)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Messages.Add(new AssistantMessageViewModel(fromUser: true) { Text = question });
        var answer = new AssistantMessageViewModel(fromUser: false) { IsWriting = true };
        Messages.Add(answer);
        OnPropertyChanged(nameof(IsEmpty));
        try
        {
            var language = Language;
            var history = _history.ToList();
            var hits = await Task.Run(() => _assistant.Find(question, language, history));
            if (hits.Count == 0)
            {
                answer.Text = L.T("Não encontrei esse assunto no manual. Tente perguntar com outras palavras, por exemplo com o nome do menu ou do recurso.");
                return;
            }

            HasLanguageModel = _assistant.HasLanguageModel;
            if (!HasLanguageModel)
            {
                answer.Text = L.T("Estas seções do manual falam sobre isso:");
                answer.ShowExcerpts = true;
                answer.SetSources(hits.Take(3).Select(h => h.Section));
                return;
            }

            answer.Text = L.T("Pensando…");
            var started = false;
            var raw = "";
            await foreach (var piece in _assistant.AnswerAsync(question, language, hits, history, _cts.Token))
            {
                started = true;
                raw += piece;
                answer.Text = HelpAssistantService.PlainText(raw);
            }
            answer.Text = started ? HelpAssistantService.PlainText(raw).Trim() : "";
            if (_cts.IsCancellationRequested) answer.Text += (answer.Text.Length > 0 ? " " : "") + L.T("(interrompido)");
            answer.SetSources(HelpAssistantService.Sources(hits));
            _history.Add(new HelpExchange(question, answer.Text));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException)
        {
            answer.Text = ex is OperationCanceledException
                ? L.T("(interrompido)")
                : L.F("Não foi possível gerar a resposta: {0}", ex.Message);
        }
        finally
        {
            answer.IsWriting = false;
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private bool CanStop() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    private bool CanClear() => !IsBusy;

    /// <summary>Nova conversa.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Messages.Clear();
        _history.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void OpenSection(AssistantSourceViewModel? source)
    {
        if (source is not null) OpenSectionRequested?.Invoke(this, source.Section);
    }

    [RelayCommand]
    private Task InstallModelAsync() => Catalog.InstallAsync([SuggestedModelId]);

    [RelayCommand]
    private void DismissInstallCard() => InstallCardDismissed = true;

    /// <summary>Painel fechado: interrompe a resposta e libera o modelo da memória (é recarregado na próxima pergunta).</summary>
    public void Suspend()
    {
        _cts?.Cancel();
        if (!IsBusy) Task.Run(_generator.Unload);
    }

    private void OnRegistryChanged(object? sender, EventArgs e) =>
        UiThread.Dispatcher.Post(() => HasLanguageModel = _assistant.HasLanguageModel);

    public void Dispose()
    {
        _registry.Changed -= OnRegistryChanged;
        _cts?.Cancel();
        Catalog.Dispose();
    }
}
