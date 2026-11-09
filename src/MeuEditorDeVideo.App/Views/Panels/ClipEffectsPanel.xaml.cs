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
        Comparar.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
        TituloAdicionar.Text = color ? "Adicionar correção de cor" : "Adicionar efeito";
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

    private static readonly DependencyProperty ItemsControlItemsSource = System.Windows.Controls.ItemsControl.ItemsSourceProperty;

    private void OnStackChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

    private void UpdateEmpty() =>
        PilhaVazia.Visibility = _watched is System.Collections.ICollection { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
}
