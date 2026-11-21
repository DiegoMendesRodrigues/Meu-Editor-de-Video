using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.App.Views.Panels;

/// <summary>Painel do assistente virtual, à direita do editor (Ajuda › Assistente virtual).</summary>
public partial class AssistantPanel
{
    private AssistantViewModel? _vm;
    private bool _scrollQueued;

    public AssistantPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as AssistantViewModel);
    }

    /// <summary>O usuário pediu para fechar o painel.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Põe o cursor na caixa da pergunta.</summary>
    public void FocusQuestion() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Pergunta));

    private void Attach(AssistantViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Messages.CollectionChanged -= Messages_CollectionChanged;
            foreach (var m in _vm.Messages) m.PropertyChanged -= Message_PropertyChanged;
        }
        _vm = vm;
        if (vm is null) return;
        vm.Messages.CollectionChanged += Messages_CollectionChanged;
        foreach (var m in vm.Messages) m.PropertyChanged += Message_PropertyChanged;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (AssistantMessageViewModel m in e.NewItems) m.PropertyChanged += Message_PropertyChanged;
        if (e.OldItems is not null)
            foreach (AssistantMessageViewModel m in e.OldItems) m.PropertyChanged -= Message_PropertyChanged;
        ScrollToEnd();
    }

    private void Message_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ScrollToEnd();

    /// <summary>Acompanha a resposta enquanto ela é escrita (uma rolagem por quadro, no máximo).</summary>
    private void ScrollToEnd()
    {
        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollQueued = false;
            Rolagem.ScrollToEnd();
        });
    }

    /// <summary>Enter envia; Shift+Enter quebra a linha.</summary>
    private void Pergunta_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            var box = Pergunta;
            var caret = box.CaretIndex;
            box.Text = box.Text.Insert(caret, Environment.NewLine);
            box.CaretIndex = caret + Environment.NewLine.Length;
        }
        else if (_vm?.SendCommand.CanExecute(null) == true)
        {
            _vm.SendCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
