using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Desktop.Services.Help;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Item do sumário do manual (seção de nível 2 ou 3).</summary>
public sealed record HelpTopicItem(HelpTopic Topic)
{
    public string Title => Topic.Title;
    public Thickness Indent => new((Topic.Level - 1) * 12, 2, 0, 2);
    public FontWeight Weight => Topic.Level <= 2 ? FontWeight.SemiBold : FontWeight.Normal;

    /// <summary>Nome lido pelos leitores de tela (o item da lista usa ToString).</summary>
    public override string ToString() => Title;
}

/// <summary>Manual do usuário offline (Ajuda › Manual do usuário, F1). Janela não modal: dá para ler enquanto se edita.</summary>
public partial class HelpWindow : Window
{
    /// <summary>Manual no idioma escolhido (MANUAL_DO_USUARIO.{idioma}.md); o português do Brasil é o original.</summary>
    public static string ManualPath => ManualPathFor(Translator.Language);

    public static string ManualPathFor(string? language)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ajuda");
        var localized = Path.Combine(folder, $"MANUAL_DO_USUARIO.{language}.md");
        return !string.IsNullOrEmpty(language) && File.Exists(localized) ? localized : Path.Combine(folder, "MANUAL_DO_USUARIO.md");
    }

    private static HelpWindow? _open;

    private MarkdownContent _content = null!;
    private (int Block, int Start)? _lastMatch;

    public HelpWindow() : this(ManualPath) { }

    /// <summary>Abre o manual de um arquivo específico (testes e idiomas fora do atual).</summary>
    public HelpWindow(string manualPath)
    {
        InitializeComponent();
        Load(manualPath);
        CampoBusca.KeyDown += CampoBusca_KeyDown;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)) AbrirBusca();
        };
    }

    /// <summary>Abre o manual (ou traz a janela já aberta para a frente), opcionalmente na seção com o título dado.</summary>
    public static void ShowFor(Window? owner, string? topicTitle = null)
    {
        if (_open is not null)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
        }
        else
        {
            _open = new HelpWindow();
            if (owner is null) _open.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _open.Closed += (_, _) => _open = null;
            if (owner is not null) _open.Show(owner);
            else _open.Show();
        }
        if (!string.IsNullOrEmpty(topicTitle)) _open.GoTo(topicTitle);
    }

    /// <summary>Seções do sumário (nível 2 e 3).</summary>
    public IReadOnlyList<HelpTopicItem> Topics => (IReadOnlyList<HelpTopicItem>)Sumario.ItemsSource!;

    /// <summary>Seleciona a seção no sumário e rola o texto até ela (usado pelo assistente virtual: "Abrir no manual").</summary>
    public void GoTo(string topicTitle)
    {
        var item = Topics.FirstOrDefault(i => i.Title == topicTitle);
        if (item is null) return;
        Sumario.SelectedItem = null; // reseleciona mesmo se já estiver selecionada
        Sumario.SelectedItem = item;
        Sumario.ScrollIntoView(item);
    }

    private void Load(string manualPath)
    {
        var blocks = File.Exists(manualPath)
            ? MarkdownDocument.Parse(File.ReadAllText(manualPath))
            : MarkdownDocument.Parse(
                $"# Manual do usuário\n\nO manual não foi encontrado em `{manualPath}`. Reinstale o Meu Editor de Vídeo para restaurá-lo.");
        _content = MarkdownControls.Build(blocks, File.Exists(manualPath) ? Path.GetDirectoryName(manualPath) : null);
        Leitor.Content = _content.Root;
        Sumario.ItemsSource = _content.Topics.Where(t => t.Level is 2 or 3).Select(t => new HelpTopicItem(t)).ToList();
    }

    private void Sumario_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Sumario.SelectedItem is HelpTopicItem item)
            // Depois do layout: o controle ainda pode não ter posição quando a janela acabou de abrir.
            Dispatcher.UIThread.Post(() => item.Topic.Target.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();

    // ---- Busca (Ctrl+F) ----

    private void AbrirBusca()
    {
        BarraBusca.IsVisible = true;
        CampoBusca.Focus();
        CampoBusca.SelectAll();
    }

    private void CampoBusca_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Procurar(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            BarraBusca.IsVisible = false;
        }
    }

    private void Proxima_Click(object? sender, RoutedEventArgs e) => Procurar(forward: true);

    private void Anterior_Click(object? sender, RoutedEventArgs e) => Procurar(forward: false);

    private static string PlainText(SelectableTextBlock block) =>
        block.Inlines is { } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : block.Text ?? "";

    /// <summary>Seleciona a próxima (ou anterior) ocorrência do texto digitado e rola até ela, voltando ao começo ao chegar no fim.</summary>
    public bool Procurar(bool forward)
    {
        var term = CampoBusca.Text?.Trim();
        ResultadoBusca.Text = "";
        if (string.IsNullOrEmpty(term)) return false;

        // Ocorrências na ordem do documento (uma por posição de texto).
        var hits = new List<(int Block, int Start)>();
        for (var b = 0; b < _content.Texts.Count; b++)
        {
            var text = PlainText(_content.Texts[b]);
            for (var at = text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase); at >= 0;
                 at = text.IndexOf(term, at + term.Length, StringComparison.CurrentCultureIgnoreCase))
                hits.Add((b, at));
        }
        if (hits.Count == 0)
        {
            ResultadoBusca.Text = Translator.T("Não encontrado");
            return false;
        }

        int index;
        if (_lastMatch is not { } last || !hits.Contains(last))
            index = forward ? 0 : hits.Count - 1;
        else
        {
            var current = hits.IndexOf(last);
            index = forward ? (current + 1) % hits.Count : (current - 1 + hits.Count) % hits.Count;
        }

        var (block, start) = hits[index];
        _lastMatch = hits[index];
        foreach (var other in _content.Texts) other.ClearSelection();
        var target = _content.Texts[block];
        target.SelectionStart = start;
        target.SelectionEnd = start + term.Length;
        target.BringIntoView();
        ResultadoBusca.Text = $"{index + 1}/{hits.Count}";
        return true;
    }
}
