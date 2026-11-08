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
    }

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
