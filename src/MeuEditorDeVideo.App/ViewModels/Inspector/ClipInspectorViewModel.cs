using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Preview;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.App.ViewModels.Inspector;

/// <summary>
/// Propriedades do clipe visual selecionado (painel C): transformação e keyframes, recorte, opacidade, mistura,
/// velocidade, congelamento, repetição, transição de entrada, efeitos e correção de cor (F6).
/// </summary>
public sealed partial class ClipInspectorViewModel : ObservableObject, IClipEditor, IDisposable
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private readonly IDialogService _dialogs;
    private Guid? _clipId;
    private bool _updating;

    public ClipInspectorViewModel(TimelineViewModel timeline, PreviewViewModel preview, IDialogService dialogs)
    {
        Timeline = timeline;
        Preview = preview;
        _dialogs = dialogs;

        Animated =
        [
            new(this, AnimatedProperty.X, "Posição X", -4000, 4000, " px", 1, 0),
            new(this, AnimatedProperty.Y, "Posição Y", -4000, 4000, " px", 1, 0),
            new(this, AnimatedProperty.Scale, "Escala", 1, 1000, "%", 100, 1),
            new(this, AnimatedProperty.Rotation, "Rotação", -360, 360, "°", 1, 0),
            new(this, AnimatedProperty.Opacity, "Opacidade", 0, 100, "%", 100, 1),
        ];
        Animated3D =
        [
            new(this, AnimatedProperty.RotationX, "Giro X", -180, 180, "°", 1, 0),
            new(this, AnimatedProperty.RotationY, "Giro Y", -180, 180, "°", 1, 0),
            new(this, AnimatedProperty.Depth, "Profundidade", -1500, 5000, " px", 1, 0),
        ];
        Crop =
        [
            new(this, "crop.l", "Esquerda", 0, 95, "%", c => c.Transform.CropLeft * 100, (c, v) => c with { Transform = c.Transform with { CropLeft = v / 100 } }),
            new(this, "crop.t", "Topo", 0, 95, "%", c => c.Transform.CropTop * 100, (c, v) => c with { Transform = c.Transform with { CropTop = v / 100 } }),
            new(this, "crop.r", "Direita", 0, 95, "%", c => c.Transform.CropRight * 100, (c, v) => c with { Transform = c.Transform with { CropRight = v / 100 } }),
            new(this, "crop.b", "Base", 0, 95, "%", c => c.Transform.CropBottom * 100, (c, v) => c with { Transform = c.Transform with { CropBottom = v / 100 } }),
        ];
        Fades =
        [
            new(this, "fade.in", "Entrada gradual", 0, 10, " s", c => c.FadeIn.TotalSeconds,
                (c, v) => c with { FadeIn = MediaTime.Min(c.Duration, MediaTime.FromSecondsApprox(v)) }, step: 0.1),
            new(this, "fade.out", "Saída gradual", 0, 10, " s", c => c.FadeOut.TotalSeconds,
                (c, v) => c with { FadeOut = MediaTime.Min(c.Duration, MediaTime.FromSecondsApprox(v)) }, step: 0.1),
        ];

        Timeline.Invalidated += OnTimelineInvalidated;
        Refresh();
    }

    public TimelineViewModel Timeline { get; }

    /// <summary>Monitor (comparação antes/depois na aba Cor).</summary>
    public PreviewViewModel Preview { get; }

    public IReadOnlyList<AnimatedValueViewModel> Animated { get; }

    /// <summary>Giros 3D e profundidade (composição 2,5D, RF154).</summary>
    public IReadOnlyList<AnimatedValueViewModel> Animated3D { get; }

    /// <summary>Animações 2D prontas (RF153).</summary>
    public IReadOnlyList<Option<AnimationPreset>> AnimationPresetOptions { get; } =
        AnimationPresets.All.Select(p => new Option<AnimationPreset>(p, p.Name)).ToList();

    [ObservableProperty]
    public partial Option<AnimationPreset>? SelectedAnimationPreset { get; set; }

    /// <summary>Aplica a animação pronta escolhida (substitui os keyframes das propriedades que ela usa).</summary>
    [RelayCommand]
    private void ApplyAnimationPreset()
    {
        if (SelectedAnimationPreset?.Value is not { } preset || Timeline.Project is not { } project) return;
        UpdateClip(c => AnimationPresets.Apply(c, preset.Id, project.Settings), $"Animação: {preset.Name}");
    }

    /// <summary>Efeito 3D pronto: o clipe vira como um cartão (giro Y de 90° a 0° na entrada), RF154.</summary>
    [RelayCommand]
    private void ApplyCardFlip()
    {
        if (Current() is not { } cur || Timeline.Project is not { } project) return;
        var fr = project.Settings.FrameRate;
        var d = cur.Clip.Duration;
        var t = fr.SnapToFrame(MediaTime.Min(MediaTime.FromSecondsApprox(0.8), new MediaTime(d.Ticks / 2)));
        if (t <= MediaTime.Zero) return;
        UpdateClip(c => c with
        {
            Keyframes = c.Keyframes.SetItem(AnimatedProperty.RotationY,
                [new Keyframe(MediaTime.Zero, 90, Easing.EaseOut), new Keyframe(t, c.Transform.RotationY, Easing.EaseInOut)]),
        }, "Animação: virar cartão 3D");
    }
    public IReadOnlyList<ClipValueViewModel> Crop { get; }
    public IReadOnlyList<ClipValueViewModel> Fades { get; }
    public ObservableCollection<EffectItemViewModel> ColorEffects { get; } = [];
    public ObservableCollection<EffectItemViewModel> OtherEffects { get; } = [];

    public IReadOnlyList<Option<BlendMode>> BlendModes { get; } =
    [
        new(BlendMode.Normal, "Normal"), new(BlendMode.Multiply, "Multiplicar"), new(BlendMode.Screen, "Tela"),
        new(BlendMode.Overlay, "Sobrepor"), new(BlendMode.SoftLight, "Luz suave"), new(BlendMode.Add, "Adicionar"),
        new(BlendMode.Darken, "Escurecer"), new(BlendMode.Lighten, "Clarear"), new(BlendMode.Difference, "Diferença"),
    ];

    public IReadOnlyList<Option<double>> SpeedPresets { get; } =
    [
        new(0.25, "0,25× (câmera lenta)"), new(0.5, "0,5× (câmera lenta)"), new(0.75, "0,75×"), new(1, "1× (normal)"),
        new(1.5, "1,5×"), new(2, "2× (acelerado)"), new(4, "4× (acelerado)"), new(8, "8× (acelerado)"),
    ];

    public IReadOnlyList<Option<TransitionKind?>> TransitionOptions => AllTransitions;

    private static readonly IReadOnlyList<Option<TransitionKind?>> AllTransitions =
    [
        new(null, "Corte seco (nenhuma)"),
        new(TransitionKind.Dissolve, "Dissolver"), new(TransitionKind.FadeBlack, "Escurecer e clarear (fade)"),
        new(TransitionKind.Zoom, "Zoom"),
        new(TransitionKind.SlideLeft, "Deslizar para a esquerda"), new(TransitionKind.SlideRight, "Deslizar para a direita"),
        new(TransitionKind.SlideUp, "Deslizar para cima"), new(TransitionKind.SlideDown, "Deslizar para baixo"),
        new(TransitionKind.PushLeft, "Empurrar para a esquerda"), new(TransitionKind.PushRight, "Empurrar para a direita"),
        new(TransitionKind.WipeLeft, "Cortina para a esquerda"), new(TransitionKind.WipeRight, "Cortina para a direita"),
        new(TransitionKind.WipeUp, "Cortina para cima"), new(TransitionKind.WipeDown, "Cortina para baixo"),
        new(TransitionKind.Circle, "Círculo"), new(TransitionKind.Diamond, "Losango"),
        new(TransitionKind.Clock, "Relógio"), new(TransitionKind.Blinds, "Persianas"),
    ];

    public IReadOnlyList<Option<Easing>> Easings { get; } =
    [
        new(Easing.EaseInOut, "Suave (entrada e saída)"), new(Easing.Linear, "Linear"),
        new(Easing.EaseIn, "Acelerando"), new(Easing.EaseOut, "Desacelerando"), new(Easing.Hold, "Manter (salto)"),
    ];

    /// <summary>Itens das seções Transições/Efeitos/Cor da biblioteca (painel A).</summary>
    public IReadOnlyList<LibraryEffectItem> LibraryTransitions { get; } =
        AllTransitions.Skip(1).Select(o => new LibraryEffectItem(o.Value!.Value.ToString(), o.Label, "Aplica ao corte antes do clipe selecionado.", "")).ToList();

    public IReadOnlyList<LibraryEffectItem> LibraryEffects { get; } =
        EffectCatalog.All.Where(d => d.Category is not (EffectCategory.Color or EffectCategory.Audio)).Select(d => new LibraryEffectItem(d.Type, d.Name, d.Description, "")).ToList();

    public IReadOnlyList<LibraryEffectItem> LibraryColor { get; } =
        EffectCatalog.All.Where(d => d.Category == EffectCategory.Color).Select(d => new LibraryEffectItem(d.Type, d.Name, d.Description, "")).ToList();

    [ObservableProperty]
    public partial bool HasClip { get; set; }

    [ObservableProperty]
    public partial string ClipTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool FlipHorizontal { get; set; }

    [ObservableProperty]
    public partial bool FlipVertical { get; set; }

    [ObservableProperty]
    public partial Option<BlendMode>? SelectedBlendMode { get; set; }

    [ObservableProperty]
    public partial Option<double>? SelectedSpeed { get; set; }

    [ObservableProperty]
    public partial string SpeedText { get; set; } = "1×";

    [ObservableProperty]
    public partial bool FrameBlending { get; set; }

    [ObservableProperty]
    public partial bool IsFrozen { get; set; }

    [ObservableProperty]
    public partial bool IsAnyAnimated { get; set; }

    [ObservableProperty]
    public partial Option<Easing>? SelectedEasing { get; set; }

    /// <summary>Existe um clipe encostado antes do selecionado (permite transição de entrada).</summary>
    [ObservableProperty]
    public partial bool HasTransitionSlot { get; set; }

    [ObservableProperty]
    public partial Option<TransitionKind?>? SelectedTransition { get; set; }

    [ObservableProperty]
    public partial double TransitionSeconds { get; set; } = 1;

    [ObservableProperty]
    public partial double TransitionMaxSeconds { get; set; } = 2;

    public string TransitionDisplay => TransitionSeconds.ToString("0.0#", Pt) + " s";

    // ---------------------------------------------------------------- estado

    private void OnTimelineInvalidated(object? sender, EventArgs e) => Refresh();

    /// <summary>Clipe visual (com mídia) selecionado — o primeiro, de cima para baixo.</summary>
    private (Track Track, Clip Clip)? Current()
    {
        if (Timeline.Sequence is not { } seq || Timeline.Selection.Count == 0) return null;
        foreach (var t in seq.Tracks)
        {
            if (!t.IsVisual) continue;
            foreach (var c in t.Clips)
                if (Timeline.Selection.Contains(c.Id) && (c.MediaAssetId is not null || c.IsText || c.IsShape)) return (t, c);
        }
        return null;
    }

    /// <summary>Primeira linha do texto, para títulos de clipes de texto.</summary>
    public static string TextTitle(string text)
    {
        var line = text.Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return "T  " + (line.Length > 40 ? line[..40] + "…" : line);
    }

    public MediaTime Offset(Clip c)
    {
        var fr = Timeline.FrameRate;
        var offset = fr.SnapToFrame(Timeline.Playhead) - c.Start;
        return MediaTime.Max(MediaTime.Zero, MediaTime.Min(c.Duration, offset));
    }

    private void Refresh()
    {
        var current = Current();
        HasClip = current is not null;
        if (current is not { } cur)
        {
            _clipId = null;
            ColorEffects.Clear();
            OtherEffects.Clear();
            return;
        }

        var (track, clip) = cur;
        var offset = Offset(clip);
        _updating = true;
        try
        {
            var asset = clip.MediaAssetId is { } id ? Timeline.Project?.FindMedia(id) : null;
            ClipTitle = asset?.Name ?? (clip.IsText ? TextTitle(clip.Text!) : clip.Shape is { } shape ? "◆  " + ShapePresets.Name(shape.Kind) : "Clipe");
            foreach (var a in Animated) a.Refresh(clip, offset);
            foreach (var a in Animated3D) a.Refresh(clip, offset);
            foreach (var c in Crop) c.Refresh(clip);
            foreach (var f in Fades) f.Refresh(clip);
            FlipHorizontal = clip.Transform.FlipHorizontal;
            FlipVertical = clip.Transform.FlipVertical;
            SelectedBlendMode = BlendModes.First(b => b.Value == clip.BlendMode);
            SelectedSpeed = SpeedPresets.FirstOrDefault(s => Math.Abs(s.Value - clip.Speed.Value) < 1e-6);
            SpeedText = clip.Speed.Value.ToString("0.##", Pt) + "×";
            FrameBlending = clip.FrameBlending;
            IsFrozen = clip.FreezeAt is not null;
            IsAnyAnimated = !clip.Keyframes.IsEmpty;
            SelectedEasing ??= Easings[0];

            var previous = track.Clips.Find(c => c.End == clip.Start && c.Id != clip.Id);
            HasTransitionSlot = previous is not null;
            var transition = TransitionOps.Incoming(track, clip.Id);
            SelectedTransition = TransitionOptions.First(o => o.Value == transition?.Kind);
            if (previous is not null)
                TransitionMaxSeconds = Math.Max(0.1, TransitionOps.MaxDuration(previous, clip, Timeline.FrameRate).TotalSeconds);
            TransitionSeconds = transition?.Duration.TotalSeconds ?? Math.Min(1, TransitionMaxSeconds);
        }
        finally
        {
            _updating = false;
        }

        SyncEffects(clip);
    }

    /// <summary>Recria a lista de efeitos só quando a estrutura muda (mantém o arraste dos controles fluido).</summary>
    private void SyncEffects(Clip clip)
    {
        var color = clip.Effects.Where(e => EffectCatalog.Find(e.Type)?.Category == EffectCategory.Color).ToList();
        var other = clip.Effects.Where(e => EffectCatalog.Find(e.Type) is { Category: not (EffectCategory.Color or EffectCategory.Audio) }).ToList();
        var sameClip = _clipId == clip.Id;
        _clipId = clip.Id;
        Sync(ColorEffects, color, sameClip);
        Sync(OtherEffects, other, sameClip);
    }

    private void Sync(ObservableCollection<EffectItemViewModel> target, List<EffectInstance> effects, bool sameClip)
    {
        if (sameClip && target.Select(t => t.Id).SequenceEqual(effects.Select(e => e.Id)))
        {
            for (var i = 0; i < effects.Count; i++)
                if (!ReferenceEquals(target[i].Effect, effects[i])) target[i].Load(effects[i]);
            return;
        }
        var expanded = target.Where(t => !t.IsExpanded).Select(t => t.Id).ToHashSet();
        target.Clear();
        foreach (var e in effects) target.Add(new EffectItemViewModel(this, e) { IsExpanded = !expanded.Contains(e.Id) });
    }

    // ---------------------------------------------------------------- edição

    public void UpdateClip(Func<Clip, Clip> change, string description, string? mergeKey = null)
    {
        if (Current() is not { } cur) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, change, description,
            mergeKey is null ? null : $"{cur.Clip.Id}:{mergeKey}"));
    }

    public void UpdateEffect(Guid effectId, Func<EffectInstance, EffectInstance> change, string description, string? mergeKey) =>
        UpdateClip(c =>
        {
            var index = c.Effects.FindIndex(e => e.Id == effectId);
            return index < 0 ? c : c with { Effects = c.Effects.SetItem(index, change(c.Effects[index])) };
        }, description, mergeKey);

    public void RemoveEffect(Guid effectId)
    {
        var name = AllEffects().FirstOrDefault(e => e.Id == effectId)?.Name ?? "efeito";
        UpdateClip(c => c with { Effects = c.Effects.RemoveAll(e => e.Id == effectId) }, $"Remover {name}");
    }

    public void MoveEffect(Guid effectId, int direction) =>
        UpdateClip(c =>
        {
            var index = c.Effects.FindIndex(e => e.Id == effectId);
            var target = index + direction;
            if (index < 0 || target < 0 || target >= c.Effects.Count) return c;
            var item = c.Effects[index];
            return c with { Effects = c.Effects.RemoveAt(index).Insert(target, item) };
        }, "Reordenar efeitos");

    private IEnumerable<EffectItemViewModel> AllEffects() => ColorEffects.Concat(OtherEffects);

    /// <summary>Adiciona um efeito do catálogo ao clipe selecionado (RF077, RF086).</summary>
    [RelayCommand]
    public void AddEffect(string type)
    {
        if (!EnsureClip()) return;
        var definition = EffectCatalog.Find(type);
        if (definition is null) return;
        string? resource = null;
        if (definition.UsesResource)
        {
            resource = _dialogs.PickFile("Escolher LUT", "LUT 3D (*.cube)|*.cube", null);
            if (resource is null || !ValidateLut(resource)) return;
        }
        var effect = EffectCatalog.Create(type, resource);
        UpdateClip(c => c with { Effects = c.Effects.Add(effect) }, $"Adicionar {definition.Name}");
    }

    public void ChooseLut(EffectItemViewModel item)
    {
        var path = _dialogs.PickFile("Escolher LUT", "LUT 3D (*.cube)|*.cube", null);
        if (path is null || !ValidateLut(path)) return;
        item.Update(e => e with { Resource = path }, "Trocar LUT", null);
    }

    private bool ValidateLut(string path)
    {
        try
        {
            CubeLut.Load(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _dialogs.ShowInfo("LUT inválida", $"Não foi possível usar \"{Path.GetFileName(path)}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>Aplica uma transição ao corte antes do clipe selecionado (painel A).</summary>
    [RelayCommand]
    private void ApplyLibraryTransition(string kind)
    {
        if (!EnsureClip()) return;
        if (!HasTransitionSlot)
        {
            _dialogs.ShowInfo("Transição", "Encoste outro clipe antes do selecionado, na mesma trilha, para aplicar uma transição.");
            return;
        }
        SelectedTransition = TransitionOptions.First(o => o.Value?.ToString() == kind);
    }

    private bool EnsureClip()
    {
        if (HasClip) return true;
        _dialogs.ShowInfo("Selecione um clipe", "Selecione um clipe de vídeo ou imagem na linha do tempo.");
        return false;
    }

    partial void OnFlipHorizontalChanged(bool value)
    {
        if (!_updating) UpdateClip(c => c with { Transform = c.Transform with { FlipHorizontal = value } }, "Espelhar horizontalmente");
    }

    partial void OnFlipVerticalChanged(bool value)
    {
        if (!_updating) UpdateClip(c => c with { Transform = c.Transform with { FlipVertical = value } }, "Espelhar verticalmente");
    }

    partial void OnSelectedBlendModeChanged(Option<BlendMode>? value)
    {
        if (!_updating && value is not null) UpdateClip(c => c with { BlendMode = value.Value }, $"Modo de mistura: {value.Label}");
    }

    partial void OnFrameBlendingChanged(bool value)
    {
        if (!_updating) UpdateClip(c => c with { FrameBlending = value }, value ? "Ativar câmera lenta suavizada" : "Desativar câmera lenta suavizada");
    }

    partial void OnSelectedSpeedChanged(Option<double>? value)
    {
        if (_updating || value is null || Current() is not { } cur) return;
        Timeline.Execute(new SetClipSpeedCommand(Timeline.SequenceId, cur.Clip.Id, new Rational((long)Math.Round(value.Value * 1000), 1000)));
    }

    partial void OnSelectedEasingChanged(Option<Easing>? value)
    {
        if (_updating || value is null || Current() is not { } cur) return;
        var offset = Offset(cur.Clip);
        UpdateClip(c => AnimatedProperty.All.Aggregate(c, (acc, p) => ClipAnimation.SetEasing(acc, p, offset, value.Value)), $"Interpolação: {value.Label}");
    }

    partial void OnSelectedTransitionChanged(Option<TransitionKind?>? value)
    {
        if (!_updating && value is not null) ApplyTransition(value.Value, TransitionSeconds);
    }

    partial void OnTransitionSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(TransitionDisplay));
        if (!_updating && SelectedTransition?.Value is { } kind) ApplyTransition(kind, value);
    }

    private void ApplyTransition(TransitionKind? kind, double seconds)
    {
        if (Current() is not { } cur) return;
        var previous = cur.Track.Clips.Find(c => c.End == cur.Clip.Start && c.Id != cur.Clip.Id);
        if (previous is null) return;
        Timeline.Execute(new SetTransitionCommand(Timeline.SequenceId, previous.Id, cur.Clip.Id, kind, MediaTime.FromSecondsApprox(seconds)));
    }

    [RelayCommand]
    private void ResetTransform() =>
        UpdateClip(c => c with { Transform = Transform.Identity, Opacity = 1, Keyframes = c.Keyframes.Clear() }, "Redefinir transformação");

    [RelayCommand]
    private void RotateRight() => Rotate(90);

    [RelayCommand]
    private void RotateLeft() => Rotate(-90);

    private void Rotate(double degrees) =>
        UpdateClip(c => ClipAnimation.SetValue(c, AnimatedProperty.Rotation, Offset(c),
            ClipAnimation.ValueAt(c, AnimatedProperty.Rotation, Offset(c)) + degrees), "Girar");

    /// <summary>Ajusta a escala para preencher o quadro (sem barras), útil para vídeos verticais (RF068).</summary>
    [RelayCommand]
    private void FillFrame()
    {
        if (Current() is not { } cur || Timeline.Project is not { } project || cur.Clip.MediaAssetId is not { } assetId || project.FindMedia(assetId) is not { } asset) return;
        if (asset.Width <= 0 || asset.Height <= 0) return;
        double pw = project.Settings.Width, ph = project.Settings.Height;
        var contain = Math.Min(pw / asset.Width, ph / asset.Height);
        var cover = Math.Max(pw / asset.Width, ph / asset.Height);
        var scale = cover / contain;
        UpdateClip(c => ClipAnimation.SetValue(c, AnimatedProperty.Scale, Offset(c), scale), "Preencher o quadro");
    }

    [RelayCommand]
    private void PreviousKeyframe() => JumpKeyframe(forward: false);

    [RelayCommand]
    private void NextKeyframe() => JumpKeyframe(forward: true);

    private void JumpKeyframe(bool forward)
    {
        if (Current() is not { } cur) return;
        if (ClipAnimation.Adjacent(cur.Clip, Offset(cur.Clip), forward) is { } offset)
            Timeline.Playhead = cur.Clip.Start + offset;
    }

    /// <summary>Congela o quadro sob o cursor por alguns segundos (RF073).</summary>
    [RelayCommand]
    private void FreezeFrame()
    {
        if (!EnsureClip() || Current() is not { } cur) return;
        var input = _dialogs.Prompt("Congelar quadro", "Duração do quadro congelado (segundos)", "2");
        if (input is null) return;
        if (!double.TryParse(input, NumberStyles.Float, Pt, out var seconds) || seconds <= 0 || seconds > 600)
        {
            _dialogs.ShowInfo("Duração inválida", "Informe um número de segundos entre 0,1 e 600, por exemplo 2 ou 1,5.");
            return;
        }
        var command = new FreezeFrameCommand(Timeline.SequenceId, cur.Clip.Id, Timeline.Playhead, MediaTime.FromSecondsApprox(seconds));
        if (Timeline.Execute(command)) Timeline.Select(command.FreezeClipId, additive: false);
    }

    /// <summary>Repete o clipe selecionado logo após ele (RF074).</summary>
    [RelayCommand]
    private void RepeatClip()
    {
        if (!EnsureClip() || Current() is not { } cur) return;
        var input = _dialogs.Prompt("Repetir trecho", "Quantas repetições depois do clipe?", "1");
        if (input is null) return;
        if (!int.TryParse(input, NumberStyles.Integer, Pt, out var count) || count is < 1 or > 100)
        {
            _dialogs.ShowInfo("Número inválido", "Informe um número inteiro entre 1 e 100.");
            return;
        }
        Timeline.Execute(new RepeatClipCommand(Timeline.SequenceId, cur.Clip.Id, count));
    }

    [RelayCommand]
    private void CustomSpeed() => Timeline.SetSpeedCommand.Execute(null);

    public void Dispose() => Timeline.Invalidated -= OnTimelineInvalidated;
}
