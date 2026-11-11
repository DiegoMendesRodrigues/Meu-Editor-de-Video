using System.ComponentModel;
using System.Windows;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels;

namespace MeuEditorDeVideo.App.Views;

public partial class MainWindow : Window
{
    private readonly IProjectWorkflow _workflow;
    private bool _closeConfirmed;

    public MainWindow(ShellViewModel viewModel, IProjectWorkflow workflow)
    {
        InitializeComponent();
        DataContext = viewModel;
        _workflow = workflow;
        StateChanged += (_, _) => AtualizarEstado();
        AtualizarEstado();
    }

    /// <summary>Largura reservada pelas páginas para os botões de legenda (3 × 46 px).</summary>
    public static readonly GridLength CaptionButtonsWidth = new(138);

    /// <summary>
    /// Maximizada com WindowChrome, a janela ultrapassa a tela pela borda de redimensionamento;
    /// compensa com margem e troca o ícone maximizar/restaurar.
    /// </summary>
    private void AtualizarEstado()
    {
        var maximizada = WindowState == WindowState.Maximized;
        var borda = SystemParameters.WindowResizeBorderThickness;
        Raiz.Margin = maximizada
            ? new Thickness(borda.Left + 1, borda.Top + 1, borda.Right + 1, borda.Bottom + 1)
            : new Thickness(0);
        BotaoMaximizar.Content = maximizada ? "" : "";
        var rotulo = maximizada ? "Restaurar" : "Maximizar";
        BotaoMaximizar.ToolTip = rotulo;
        System.Windows.Automation.AutomationProperties.SetName(BotaoMaximizar, rotulo);
    }

    private void Minimizar_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximizar_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /// <summary>Ao fechar, pergunta sobre alterações não salvas (Sim / Não / Cancelar).</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel) return;

        e.Cancel = true;
        if (!await _workflow.ConfirmDiscardOrSaveAsync()) return;

        _closeConfirmed = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
