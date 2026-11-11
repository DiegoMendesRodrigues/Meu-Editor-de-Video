using System.Collections.Specialized;
using System.Windows;
using MeuEditorDeVideo.App.ViewModels.Inspector;

namespace MeuEditorDeVideo.App.Views.Panels;

public enum EffectsPanelMode
{
    Effects,
    Color,
}

public partial class ClipEffectsPanel
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(EffectsPanelMode), typeof(ClipEffectsPanel), new PropertyMetadata(EffectsPanelMode.Effects, (d, _) => ((ClipEffectsPanel)d).Apply()));

    public ClipEffectsPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Apply();
        Apply();
    }

    public EffectsPanelMode Mode
    {
        get => (EffectsPanelMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private INotifyCollectionChanged? _watched;

    private void Apply()
    {
        var color = Mode == EffectsPanelMode.Color;
        Predefinicoes.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
        TituloAdicionar.Text = color ? "Ferramentas de cor" : "Adicionar efeito";
        TituloPilha.Text = color ? "Correções aplicadas (em ordem)" : "Efeitos aplicados (em ordem)";
        PilhaVazia.Text = color
            ? "Nenhuma correção de cor. Comece por \"Ajustes de cor\"; use \"Comparar antes/depois\" para avaliar."
            : "Nenhum efeito. Os efeitos se combinam na ordem da lista; use as setas para reordenar.";

        Catalogo.SetBinding(ItemsControlItemsSource, color ? nameof(ClipInspectorViewModel.LibraryColor) : nameof(ClipInspectorViewModel.LibraryEffects));
        Pilha.SetBinding(ItemsControlItemsSource, color ? nameof(ClipInspectorViewModel.ColorEffects) : nameof(ClipInspectorViewModel.OtherEffects));

        if (_watched is not null) _watched.CollectionChanged -= OnStackChanged;
        _watched = DataContext is ClipInspectorViewModel vm ? (color ? vm.ColorEffects : vm.OtherEffects) : null;
        if (_watched is not null) _watched.CollectionChanged += OnStackChanged;
        UpdateEmpty();
    }

    private bool _cartaoJaAplicado;

    /// <summary>
    /// Duplo clique num cartão que já estava aplicado desaplica a opção. O primeiro clique ainda aplica (como sempre);
    /// o estado é lido antes dele, para saber se a opção já estava no clipe.
    /// </summary>
    private void Cartao_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key } || DataContext is not ClipInspectorViewModel vm) return;
        if (e.ClickCount == 1)
        {
            _cartaoJaAplicado = vm.AppliedKeys.Contains(key);
            return;
        }
        if (e.ClickCount == 2 && _cartaoJaAplicado)
        {
            vm.RemoveApplied(key);
            _cartaoJaAplicado = false;
        }
        e.Handled = true;
    }

    private static readonly DependencyProperty ItemsControlItemsSource = System.Windows.Controls.ItemsControl.ItemsSourceProperty;

    private void OnStackChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

    private void UpdateEmpty() =>
        PilhaVazia.Visibility = _watched is System.Collections.ICollection { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
}
