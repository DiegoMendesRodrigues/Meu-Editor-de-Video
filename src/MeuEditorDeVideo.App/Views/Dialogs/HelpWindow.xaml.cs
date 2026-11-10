using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MeuEditorDeVideo.App.Services.Help;

namespace MeuEditorDeVideo.App.Views.Dialogs;

/// <summary>Manual do usuário offline (Ajuda › Manual do usuário, F1). Janela não modal: dá para ler enquanto se edita.</summary>
public partial class HelpWindow
{
    /// <summary>Local do manual instalado junto com o aplicativo.</summary>
    public static string ManualPath => Path.Combine(AppContext.BaseDirectory, "ajuda", "MANUAL_DO_USUARIO.md");

    private static HelpWindow? _open;

    public sealed record TopicItem(HelpTopic Topic)
    {
        public string Title => Topic.Title;
        public Thickness Indent => new((Topic.Level - 1) * 12, 2, 0, 2);
        public FontWeight Weight => Topic.Level <= 2 ? FontWeights.SemiBold : FontWeights.Normal;

        /// <summary>Nome lido pelos leitores de tela (o item da lista usa ToString).</summary>
        public override string ToString() => Title;
    }

    public HelpWindow()
    {
        InitializeComponent();
        Load();
    }

    /// <summary>Abre o manual (ou traz a janela já aberta para a frente).</summary>
    public static void ShowFor(Window? owner)
    {
        if (_open is not null)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        _open = new HelpWindow { Owner = owner };
        if (owner is null) _open.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private void Load()
    {
        IReadOnlyList<HelpTopic> topics;
        FlowDocument document;
        if (File.Exists(ManualPath))
        {
            document = MarkdownFlowDocument.Build(MarkdownDocument.Parse(File.ReadAllText(ManualPath)), out topics);
        }
        else
        {
            document = MarkdownFlowDocument.Build(MarkdownDocument.Parse(
                $"# Manual do usuário\n\nO manual não foi encontrado em `{ManualPath}`. Reinstale o Meu Editor de Vídeo para restaurá-lo."), out topics);
        }
        Leitor.Document = document;
        Sumario.ItemsSource = topics.Where(t => t.Level is 2 or 3).Select(t => new TopicItem(t)).ToList();
    }

    private void Sumario_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Sumario.SelectedItem is TopicItem item) item.Topic.Target.BringIntoView();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
