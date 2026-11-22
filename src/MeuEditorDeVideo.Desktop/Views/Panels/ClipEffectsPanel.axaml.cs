using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;

namespace MeuEditorDeVideo.Desktop.Views.Panels;

public enum EffectsPanelMode
{
    Effects,
    Color,
}

public partial class ClipEffectsPanel : UserControl
{
    public static readonly StyledProperty<EffectsPanelMode> ModeProperty =
        AvaloniaProperty.Register<ClipEffectsPanel, EffectsPanelMode>(nameof(Mode), EffectsPanelMode.Effects);

    static ClipEffectsPanel() => ModeProperty.Changed.AddClassHandler<ClipEffectsPanel>((panel, _) => panel.Apply());

    public ClipEffectsPanel()
    {
        InitializeComponent();
        // No túnel: o botão trata o clique (aplica a opção) antes de a borbulha chegar aqui.
        AddHandler(PointerPressedEvent, Cartao_PointerPressed, RoutingStrategies.Tunnel);
        Apply();
    }

    public EffectsPanelMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private INotifyCollectionChanged? _watched;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Apply();
    }

    private void Apply()
    {
        var color = Mode == EffectsPanelMode.Color;
        Predefinicoes.IsVisible = color;
        TituloAdicionar.Text = Translator.T(color ? "Ferramentas de cor" : "Adicionar efeito");
        TituloPilha.Text = Translator.T(color ? "Correções aplicadas (em ordem)" : "Efeitos aplicados (em ordem)");
        PilhaVazia.Text = Translator.T(color
            ? "Nenhuma correção de cor. Comece por \"Ajustes de cor\"; use \"Comparar antes/depois\" para avaliar."
            : "Nenhum efeito. Os efeitos se combinam na ordem da lista; use as setas para reordenar.");

        Catalogo.Bind(ItemsControl.ItemsSourceProperty, new Binding(color ? nameof(ClipInspectorViewModel.LibraryColor) : nameof(ClipInspectorViewModel.LibraryBasicEffects)));
        Pilha.Bind(ItemsControl.ItemsSourceProperty, new Binding(color ? nameof(ClipInspectorViewModel.ColorEffects) : nameof(ClipInspectorViewModel.OtherEffects)));

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
    private void Cartao_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || DataContext is not ClipInspectorViewModel vm) return;
        var card = source as Button ?? source.FindAncestorOfType<Button>();
        if (card is not { Tag: string key } || !card.Classes.Contains("ins-card")) return;
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

    private void OnStackChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

    private void UpdateEmpty() =>
        PilhaVazia.IsVisible = _watched is not System.Collections.ICollection { Count: > 0 };
}
