using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using MeuEditorDeVideo.Desktop.Services;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.Desktop.Views.Dialogs;

/// <summary>Ferramenta "Baixar modelos de IA" (catálogo oficial → pacotes .medmodel → instalação).</summary>
public partial class ModelCatalogWindow : Window
{
    public ModelCatalogWindow() { InitializeComponent(); }

    public ModelCatalogWindow(ModelCatalogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // O botão Instalar do modelo escolhido fica dentro do DataContext do item; ele só vale com a instalação parada.
        BotaoInstalar.Bind(IsEnabledProperty, new Binding(nameof(ModelCatalogViewModel.IsIdle)) { Source = viewModel });
        Closing += (_, e) =>
        {
            if (!viewModel.IsBusy) return;
            // Confirmação síncrona (laço modal): o fechamento só continua depois da resposta.
            var answer = DialogService.Message(Translator.T("Baixar modelos de IA"), Translator.T("A instalação está em andamento. Cancelar e fechar?"),
                Views.Dialogs.MessageKind.Question, ["Sim", "Não"], defaultIndex: 1, cancelIndex: 1);
            if (answer == 0) viewModel.CancelCommand.Execute(null);
            else e.Cancel = true;
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();
}
