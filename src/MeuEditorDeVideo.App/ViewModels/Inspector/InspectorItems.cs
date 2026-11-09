using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.App.ViewModels.Inspector;

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Editor do clipe dono dos itens do inspetor (vídeo ou áudio).</summary>
public interface IClipEditor
{
    void UpdateClip(Func<Clip, Clip> change, string description, string? mergeKey = null);
    MediaTime Offset(Clip clip);
    void UpdateEffect(Guid effectId, Func<EffectInstance, EffectInstance> change, string description, string? mergeKey);
    void RemoveEffect(Guid effectId);
    void MoveEffect(Guid effectId, int direction);
    void ChooseLut(EffectItemViewModel item);
}

/// <summary>Valor numérico exibido num controle deslizante; evita reenvio ao atualizar a partir do modelo.</summary>
public abstract partial class ValueItemViewModel : ObservableObject
{
    private bool _updating;

    protected ValueItemViewModel(string label, double min, double max, string unit, double step)
    {
        Label = label;
        Min = min;
        Max = max;
        Unit = unit;
        Step = step;
    }

    public string Label { get; }
    public double Min { get; }
    public double Max { get; }
    public string Unit { get; }
    public double Step { get; }

    [ObservableProperty]
    public partial double Value { get; set; }

    public string Display => Value.ToString(Step < 1 ? "0.##" : "0", CultureInfo.GetCultureInfo("pt-BR")) + Unit;

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Display));
        if (!_updating) Commit(Math.Clamp(value, Min, Max));
    }

    protected abstract void Commit(double value);

    /// <summary>Atualiza a partir do modelo sem gerar comando.</summary>
    public void Load(double value)
    {
        _updating = true;
        try
        {
            if (Math.Abs(Value - value) > 1e-9) Value = value;
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Volta ao padrão (duplo clique no controle).</summary>
    [RelayCommand]
    protected virtual void Reset() { }
}

/// <summary>Propriedade animável por keyframes (posição, escala, rotação, opacidade — RF144–RF148).</summary>
public sealed partial class AnimatedValueViewModel(IClipEditor owner, string property, string label, double min, double max,
    string unit, double factor, double defaultValue, double step = 1)
    : ValueItemViewModel(label, min, max, unit, step)
{
    public string Property { get; } = property;

    /// <summary>A propriedade tem keyframes.</summary>
    [ObservableProperty]
    public partial bool IsAnimated { get; set; }

    /// <summary>Há keyframe no instante do cursor.</summary>
    [ObservableProperty]
    public partial bool HasKeyHere { get; set; }

    public string KeyToolTip => HasKeyHere ? $"Remover keyframe de {Label.ToLowerInvariant()} neste instante" : $"Adicionar keyframe de {Label.ToLowerInvariant()} neste instante";

    partial void OnHasKeyHereChanged(bool value) => OnPropertyChanged(nameof(KeyToolTip));

    protected override void Commit(double value) =>
        owner.UpdateClip(c => ClipAnimation.SetValue(c, Property, owner.Offset(c), value / factor), $"Ajustar {Label.ToLowerInvariant()}", Property);

    protected override void Reset() => Value = defaultValue * factor;

    [RelayCommand]
    private void ToggleKey() =>
        owner.UpdateClip(c => ClipAnimation.ToggleKeyframe(c, Property, owner.Offset(c)), HasKeyHere ? "Remover keyframe" : "Adicionar keyframe");

    [RelayCommand]
    private void ClearKeys() =>
        owner.UpdateClip(c => ClipAnimation.ClearKeyframes(c, Property, owner.Offset(c)), $"Remover animação de {Label.ToLowerInvariant()}");

    public void Refresh(Clip clip, MediaTime offset)
    {
        Load(ClipAnimation.ValueAt(clip, Property, offset) * factor);
        IsAnimated = ClipAnimation.IsAnimated(clip, Property);
        HasKeyHere = ClipAnimation.HasKeyframeAt(clip, Property, offset);
    }
}

/// <summary>Propriedade simples do clipe (recorte, entrada/saída gradual).</summary>
public sealed partial class ClipValueViewModel(IClipEditor owner, string key, string label, double min, double max, string unit,
    Func<Clip, double> get, Func<Clip, double, Clip> set, double defaultValue = 0, double step = 1)
    : ValueItemViewModel(label, min, max, unit, step)
{
    protected override void Commit(double value) => owner.UpdateClip(c => set(c, value), $"Ajustar {Label.ToLowerInvariant()}", key);

    protected override void Reset() => Value = defaultValue;

    public void Refresh(Clip clip) => Load(get(clip));
}

/// <summary>Efeito na pilha do clipe (RF086–RF089).</summary>
public sealed partial class EffectItemViewModel : ObservableObject
{
    private readonly IClipEditor _owner;
    private bool _updating;

    public EffectItemViewModel(IClipEditor owner, EffectInstance effect)
    {
        _owner = owner;
        Effect = effect;
        Definition = EffectCatalog.Find(effect.Type)!;
        foreach (var p in Definition.Parameters)
        {
            EffectParameterViewModel vm = p.Kind switch
            {
                ParameterKind.Slider => new SliderParameterViewModel(this, p),
                ParameterKind.Choice => new ChoiceParameterViewModel(this, p),
                ParameterKind.Color => new ColorParameterViewModel(this, p),
                ParameterKind.Curve => new CurveParameterViewModel(this, p),
                _ => new WheelParameterViewModel(this, p),
            };
            Parameters.Add(vm);
        }
        Load(effect);
    }

    public EffectInstance Effect { get; private set; }
    public EffectDefinition Definition { get; }
    public Guid Id => Effect.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public bool UsesResource => Definition.UsesResource;
    public string ResourceName => string.IsNullOrEmpty(Effect.Resource) ? "Nenhum arquivo escolhido" : Path.GetFileName(Effect.Resource);
    public ObservableCollection<EffectParameterViewModel> Parameters { get; } = [];

    /// <summary>Curvas: um único editor com seletor de canal.</summary>
    public bool IsCurves => Effect.Type == EffectCatalog.Curves;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    partial void OnEnabledChanged(bool value)
    {
        if (!_updating) Update(e => e with { Enabled = value }, value ? $"Ativar {Name}" : $"Desativar {Name}", null);
    }

    public void Load(EffectInstance effect)
    {
        Effect = effect;
        _updating = true;
        try
        {
            Enabled = effect.Enabled;
            foreach (var p in Parameters) p.Load(effect);
        }
        finally
        {
            _updating = false;
        }
        OnPropertyChanged(nameof(ResourceName));
    }

    public void Update(Func<EffectInstance, EffectInstance> change, string description, string? mergeKey) =>
        _owner.UpdateEffect(Id, change, description, mergeKey is null ? null : $"{Id}:{mergeKey}");

    [RelayCommand] private void Remove() => _owner.RemoveEffect(Id);
    [RelayCommand] private void MoveUp() => _owner.MoveEffect(Id, -1);
    [RelayCommand] private void MoveDown() => _owner.MoveEffect(Id, +1);
    [RelayCommand] private void Reset() => Update(e => e with { Parameters = e.Parameters.Clear() }, $"Redefinir {Name}", null);
    [RelayCommand] private void ChooseResource() => _owner.ChooseLut(this);
}

public abstract partial class EffectParameterViewModel(EffectItemViewModel owner, EffectParameter parameter) : ObservableObject
{
    protected bool Updating;

    public EffectItemViewModel Owner { get; } = owner;
    public EffectParameter Parameter { get; } = parameter;
    public string Label => Parameter.Label;

    public void Load(EffectInstance effect)
    {
        Updating = true;
        try
        {
            OnLoad(effect);
        }
        finally
        {
            Updating = false;
        }
    }

    protected abstract void OnLoad(EffectInstance effect);
}

public sealed partial class SliderParameterViewModel(EffectItemViewModel owner, EffectParameter parameter) : EffectParameterViewModel(owner, parameter)
{
    public double Min => Parameter.Min;
    public double Max => Parameter.Max;
    public double Step => Parameter.Max - Parameter.Min <= 10 ? 0.05 : 1;

    [ObservableProperty]
    public partial double Value { get; set; }

    public string Display => Value.ToString(Step < 1 ? "0.##" : "0", CultureInfo.GetCultureInfo("pt-BR")) + Parameter.Unit;

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Display));
        if (!Updating) Owner.Update(e => EffectCatalog.WithValue(e, Parameter.Key, value), $"Ajustar {Owner.Name}: {Label.ToLowerInvariant()}", Parameter.Key);
    }

    [RelayCommand]
    private void Reset() => Value = Parameter.Default;

    protected override void OnLoad(EffectInstance effect) => Value = EffectCatalog.Value(effect, Parameter.Key);
}

public sealed partial class ChoiceParameterViewModel(EffectItemViewModel owner, EffectParameter parameter) : EffectParameterViewModel(owner, parameter)
{
    public IReadOnlyList<string> Options => Parameter.Options ?? [];

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    partial void OnSelectedIndexChanged(int value)
    {
        if (!Updating && value >= 0) Owner.Update(e => EffectCatalog.WithValue(e, Parameter.Key, value), $"{Owner.Name}: {Label.ToLowerInvariant()}", null);
    }

    protected override void OnLoad(EffectInstance effect) => SelectedIndex = (int)Math.Round(EffectCatalog.Value(effect, Parameter.Key));
}

/// <summary>Cor em hexadecimal (#RRGGBB) com amostra.</summary>
public sealed partial class ColorParameterViewModel(EffectItemViewModel owner, EffectParameter parameter) : EffectParameterViewModel(owner, parameter)
{
    [ObservableProperty]
    public partial string Hex { get; set; } = "#000000";

    [ObservableProperty]
    public partial Brush Swatch { get; set; } = Brushes.Black;

    partial void OnHexChanged(string value)
    {
        if (!TryParse(value, out var r, out var g, out var b)) return;
        Swatch = Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));
        if (!Updating) Owner.Update(e => EffectCatalog.WithColor(e, Parameter.Key, r / 255.0, g / 255.0, b / 255.0), $"{Owner.Name}: {Label.ToLowerInvariant()}", Parameter.Key);
    }

    /// <summary>Cores rápidas (verde e azul de estúdio, branco, cinza neutro).</summary>
    public IReadOnlyList<string> Presets { get; } = ["#00B140", "#0047BB", "#FFFFFF", "#808080", "#000000"];

    [RelayCommand]
    private void UsePreset(string hex) => Hex = hex;

    protected override void OnLoad(EffectInstance effect)
    {
        var (r, g, b) = EffectCatalog.ColorValue(effect, Parameter.Key);
        Hex = $"#{(byte)Math.Round(r * 255):X2}{(byte)Math.Round(g * 255):X2}{(byte)Math.Round(b * 255):X2}";
    }

    public static bool TryParse(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var t = (text ?? "").Trim().TrimStart('#');
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        r = (byte)(v >> 16);
        g = (byte)(v >> 8);
        b = (byte)v;
        return true;
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>Curva de tons de um canal; o editor gráfico lê e grava os pontos.</summary>
public sealed partial class CurveParameterViewModel(EffectItemViewModel owner, EffectParameter parameter) : EffectParameterViewModel(owner, parameter)
{
    [ObservableProperty]
    public partial IReadOnlyList<(double X, double Y)> Points { get; set; } = [(0, 0), (1, 1)];

    public Color Stroke => Parameter.Key switch
    {
        "r" => Color.FromRgb(0xF0, 0x52, 0x5F),
        "g" => Color.FromRgb(0x2E, 0xC2, 0x6B),
        "b" => Color.FromRgb(0x3D, 0x8E, 0xFF),
        _ => Color.FromRgb(0xE8, 0xED, 0xF7),
    };

    /// <summary>Chamado pelo editor de curva durante o arraste.</summary>
    public void SetPoints(IReadOnlyList<(double X, double Y)> points)
    {
        Points = points;
        Owner.Update(e => EffectCatalog.WithCurve(e, Parameter.Key, points), $"Curvas: {Label.ToLowerInvariant()}", "curve." + Parameter.Key);
    }

    [RelayCommand]
    private void Reset() => SetPoints([(0, 0), (1, 1)]);

    protected override void OnLoad(EffectInstance effect) => Points = EffectCatalog.CurvePoints(effect, Parameter.Key);
}

/// <summary>Roda de cor (sombras, tons médios, realces) com luminância.</summary>
public sealed partial class WheelParameterViewModel(EffectItemViewModel owner, EffectParameter parameter) : EffectParameterViewModel(owner, parameter)
{
    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    public partial double Luminance { get; set; }

    partial void OnXChanged(double value) => Push();
    partial void OnYChanged(double value) => Push();
    partial void OnLuminanceChanged(double value) => Push();

    private void Push()
    {
        if (Updating) return;
        double x = X, y = Y, l = Luminance;
        Owner.Update(e => EffectCatalog.WithWheel(e, Parameter.Key, x, y, l), $"Rodas de cor: {Label.ToLowerInvariant()}", "wheel." + Parameter.Key);
    }

    [RelayCommand]
    private void Reset()
    {
        Updating = true;
        X = 0;
        Y = 0;
        Updating = false;
        Luminance = 0;
        Push();
    }

    protected override void OnLoad(EffectInstance effect)
    {
        var (x, y, l) = EffectCatalog.WheelValue(effect, Parameter.Key);
        X = x;
        Y = y;
        Luminance = l;
    }
}

/// <summary>Item da biblioteca de efeitos/cor/transições (painel A).</summary>
public sealed record LibraryEffectItem(string Key, string Name, string Description, string Glyph);
