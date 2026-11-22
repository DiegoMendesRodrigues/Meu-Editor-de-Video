using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IProjectWorkflow? _workflow;
    private bool _closeConfirmed;

    /// <summary>Designer do XAML.</summary>
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    public MainWindow(ShellViewModel viewModel, IProjectWorkflow workflow) : this()
    {
        DataContext = viewModel;
        _workflow = workflow;
        CaberNaTela();
        // Windows e macOS: conteúdo sob a barra de título, botões nativos por cima (ver MainWindow.axaml).
        if (!OperatingSystem.IsLinux())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 40;
            Opened += (_, _) => HideDrawnTitle();
            // Os botões de legenda podem ser recriados ao maximizar/restaurar.
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty) Dispatcher.UIThread.Post(HideDrawnTitle, DispatcherPriority.Loaded);
            };
        }
    }

    /// <summary>O tamanho de restaurar (1440 × 900) não pode passar da área útil da tela.</summary>
    private void CaberNaTela()
    {
        if (Screens.Primary is not { Scaling: > 0 } tela) return;
        Width = Math.Max(MinWidth, Math.Min(Width, tela.WorkingArea.Width / tela.Scaling));
        Height = Math.Max(MinHeight, Math.Min(Height, tela.WorkingArea.Height / tela.Scaling));
    }

    /// <summary>Partes da barra de título desenhada pelo Avalonia que o editor esconde.</summary>
    public static IReadOnlyList<string> HiddenTitleBarParts { get; } = ["PART_TitleTextPanel", "PART_FullScreenButton"];

    /// <summary>
    /// A barra de título desenhada pelo Avalonia (camada de decorações, fora da árvore da janela, por isso os estilos
    /// do aplicativo não a alcançam) mostra o título à esquerda, por baixo dos menus, e um botão de tela cheia à
    /// esquerda do minimizar, por cima do botão Exportar. O editor já mostra o título no centro da faixa superior e
    /// tem tela cheia própria (F11); aqui os dois são escondidos (minimizar, maximizar, fechar e o arraste ficam).
    /// </summary>
    private void HideDrawnTitle()
    {
        Visual root = this;
        while (root.GetVisualParent() is { } parent) root = parent;
        foreach (var part in root.GetVisualDescendants().OfType<Control>().Where(c => c.Name is { } n && HiddenTitleBarParts.Contains(n)))
            part.IsVisible = false;
    }

    /// <summary>
    /// Largura reservada pelas páginas à direita da faixa superior para os botões de legenda do Windows (3 × 46 px).
    /// No macOS os botões ficam à esquerda (<see cref="CaptionButtonsLeftWidth"/>); no Linux, a barra é do sistema.
    /// </summary>
    public static GridLength CaptionButtonsWidth { get; } = new(OperatingSystem.IsWindows() ? 138 : 0);

    /// <summary>Espaço à esquerda para os botões "semáforo" do macOS.</summary>
    public static GridLength CaptionButtonsLeftWidth { get; } = new(OperatingSystem.IsMacOS() ? 78 : 0);

    /// <summary>Executado depois de confirmar o fechamento e antes de fechar de fato (projeto, autosave, host).</summary>
    public Func<Task>? BeforeClose { get; set; }

    /// <summary>O usuário cancelou o fechamento na pergunta sobre alterações não salvas.</summary>
    public event EventHandler? CloseCanceled;

    /// <summary>Ao fechar, pergunta sobre alterações não salvas (Salvar / Não salvar / Cancelar).</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel || _workflow is null) return;

        e.Cancel = true;
        if (!await _workflow.ConfirmDiscardOrSaveAsync())
        {
            CloseCanceled?.Invoke(this, EventArgs.Empty);
            return;
        }

        _closeConfirmed = true;
        if (BeforeClose is { } before) await before();
        Dispatcher.UIThread.Post(Close);
    }
}
