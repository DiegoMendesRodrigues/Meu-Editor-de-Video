using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeuEditorDeVideo.App.Services;
using MeuEditorDeVideo.App.ViewModels.Timeline;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.ImageEngine.Composition;
using MeuEditorDeVideo.TimelineEngine;
using Microsoft.Extensions.Logging;

namespace MeuEditorDeVideo.App.ViewModels.Inspector;

/// <summary>O que a camada de edição sobre o monitor está editando.</summary>
public enum OverlayKind
{
    None,
    MaskRectangle,
    MaskEllipse,
    MaskPath,
    TrackerRegion,
}

/// <summary>Item de lista (máscara ou rastreamento) com nome e resumo.</summary>
public sealed record CompositionListItem(Guid Id, string Name, string Summary)
{
    public override string ToString() => Name;
}

/// <summary>
/// Aba "Composição" do painel C (F9): elemento gráfico (forma, cores, tamanho), máscaras manuais e animadas com edição
/// no monitor (RF110, RF149), rastreamento de movimento por visão computacional (RF111, RF150), vínculo de elementos ao
/// movimento e estabilização (RF075). As análises rodam em segundo plano, com progresso e cancelamento.
/// </summary>
public sealed partial class CompositionInspectorViewModel : ObservableObject, IClipEditor, IDisposable
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private const int AnalysisWidth = 960;
    private readonly IProjectService _projects;
    private readonly IVideoFrameSourceFactory _frames;
    private readonly IDialogService _dialogs;
    private readonly ILogger<CompositionInspectorViewModel> _logger;
    private bool _updating;
    private CancellationTokenSource? _analysis;

    public CompositionInspectorViewModel(TimelineViewModel timeline, IProjectService projects, IVideoFrameSourceFactory frames,
        IDialogService dialogs, ILogger<CompositionInspectorViewModel> logger)
    {
        Timeline = timeline;
        _projects = projects;
        _frames = frames;
        _dialogs = dialogs;
        _logger = logger;

        ShapeValues =
        [
            new(this, "shape.w", "Largura", 4, 4000, " px", c => c.Shape?.Width ?? 0, (c, v) => WithShape(c, s => s with { Width = v }), 400),
            new(this, "shape.h", "Altura", 2, 4000, " px", c => c.Shape?.Height ?? 0, (c, v) => WithShape(c, s => s with { Height = v }), 240),
            new(this, "shape.stroke", "Contorno", 0, 60, " px", c => c.Shape?.StrokeWidth ?? 0, (c, v) => WithShape(c, s => s with { StrokeWidth = v })),
            new(this, "shape.radius", "Cantos", 0, 400, " px", c => c.Shape?.CornerRadius ?? 0, (c, v) => WithShape(c, s => s with { CornerRadius = v })),
            new(this, "shape.sides", "Pontas/lados", 3, 24, "", c => c.Shape?.Sides ?? 5, (c, v) => WithShape(c, s => s with { Sides = (int)Math.Round(v) }), 5),
        ];
        MaskValues =
        [
            new(this, "mask.feather", "Suavizar borda", 0, 25, "%", c => SelectedMaskOf(c)?.Feather * 100 ?? 0,
                (c, v) => WithMask(c, m => m with { Feather = v / 100 }), 1, 0.1),
            new(this, "mask.expand", "Contrair/expandir", -10, 10, "%", c => SelectedMaskOf(c)?.Expansion * 100 ?? 0,
                (c, v) => WithMask(c, m => m with { Expansion = v / 100 }), 0, 0.1),
            new(this, "mask.opacity", "Opacidade", 0, 100, "%", c => SelectedMaskOf(c)?.Opacity * 100 ?? 100,
                (c, v) => WithMask(c, m => m with { Opacity = v / 100 }), 100),
        ];
        StabilizationValues =
        [
            new(this, "stab.smooth", "Suavidade", 0.1, 5, " s", c => c.Stabilization?.Smoothness ?? 1,
                (c, v) => c.Stabilization is { } s ? c with { Stabilization = s with { Smoothness = v } } : c, 1, 0.1),
            new(this, "stab.zoom", "Ampliação (0 = automática)", 0, 50, "%", c => (c.Stabilization?.Zoom ?? 0) * 100,
                (c, v) => c.Stabilization is { } s ? c with { Stabilization = s with { Zoom = v / 100 } } : c),
        ];

        Timeline.Invalidated += OnTimelineInvalidated;
        Timeline.PropertyChanged += OnTimelinePropertyChanged;
        Refresh();
    }

    public TimelineViewModel Timeline { get; }

    /// <summary>A forma, os pontos ou a região mudaram (a camada sobre o monitor se redesenha).</summary>
    public event EventHandler? OverlayChanged;

    public IReadOnlyList<ClipValueViewModel> ShapeValues { get; }
    public IReadOnlyList<ClipValueViewModel> MaskValues { get; }
    public IReadOnlyList<ClipValueViewModel> StabilizationValues { get; }

    public IReadOnlyList<Option<ShapeKind>> ShapeKinds { get; } =
        ShapePresets.All.DistinctBy(p => p.Kind).Select(p => new Option<ShapeKind>(p.Kind, p.Name)).ToList();

    public IReadOnlyList<Option<MaskMode>> MaskModes { get; } =
        [new(MaskMode.Add, "Somar"), new(MaskMode.Subtract, "Subtrair"), new(MaskMode.Intersect, "Interseção")];

    public ObservableCollection<CompositionListItem> Masks { get; } = [];
    public ObservableCollection<CompositionListItem> Trackers { get; } = [];
    public ObservableCollection<Option<Guid?>> MaskTrackerOptions { get; } = [];
    public ObservableCollection<Option<(Guid ClipId, Guid TrackerId)?>> LinkOptions { get; } = [];

    [ObservableProperty] public partial bool HasClip { get; set; }
    [ObservableProperty] public partial string ClipTitle { get; set; } = "";
    [ObservableProperty] public partial bool IsShape { get; set; }

    /// <summary>Clipe com imagem de mídia (máscaras e edição no monitor).</summary>
    [ObservableProperty] public partial bool CanMask { get; set; }

    /// <summary>Clipe de vídeo (rastreamento e estabilização).</summary>
    [ObservableProperty] public partial bool IsVideo { get; set; }

    [ObservableProperty] public partial Option<ShapeKind>? SelectedShapeKind { get; set; }
    [ObservableProperty] public partial string FillColor { get; set; } = "";
    [ObservableProperty] public partial string StrokeColor { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMask))]
    public partial CompositionListItem? SelectedMask { get; set; }

    public bool HasSelectedMask => SelectedMask is not null;
    [ObservableProperty] public partial bool MaskInvert { get; set; }
    [ObservableProperty] public partial bool MaskSmooth { get; set; }
    [ObservableProperty] public partial bool MaskIsPath { get; set; }
    [ObservableProperty] public partial Option<MaskMode>? SelectedMaskMode { get; set; }
    [ObservableProperty] public partial bool MaskAnimated { get; set; }
    [ObservableProperty] public partial bool MaskKeyHere { get; set; }
    [ObservableProperty] public partial Option<Guid?>? SelectedMaskTracker { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTracker))]
    public partial CompositionListItem? SelectedTracker { get; set; }

    public bool HasSelectedTracker => SelectedTracker is not null;

    [ObservableProperty] public partial Option<(Guid ClipId, Guid TrackerId)?>? SelectedLink { get; set; }
    [ObservableProperty] public partial bool LinkFollowScale { get; set; }
    [ObservableProperty] public partial bool IsLinked { get; set; }
    [ObservableProperty] public partial bool HasStabilization { get; set; }
    [ObservableProperty] public partial bool StabilizationEnabled { get; set; }
    [ObservableProperty] public partial string StabilizationSummary { get; set; } = "";

    /// <summary>Mostra e edita a máscara/região selecionada sobre o monitor.</summary>
    [ObservableProperty] public partial bool EditOnMonitor { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TrackCommand), nameof(StabilizeCommand), nameof(CancelAnalysisCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string BusyText { get; set; } = "";

    /// <summary>Tipo de edição ativo sobre o monitor.</summary>
    public OverlayKind Overlay { get; private set; }

    /// <summary>Pontos editáveis em pixels do projeto (relativos ao centro): 2 cantos (retângulo/elipse/região) ou o contorno livre.</summary>
    public IReadOnlyList<Point> OverlayPoints { get; private set; } = [];

    /// <summary>Quatro cantos da caixa (retângulo, elipse, região) em pixels do projeto, na ordem do contorno.</summary>
    public IReadOnlyList<Point> OverlayCorners { get; private set; } = [];

    /// <summary>Contorno desenhado (retângulo/elipse calculados) em pixels do projeto.</summary>
    public IReadOnlyList<Point> OverlayOutline { get; private set; } = [];

    /// <summary>Posição rastreada no instante atual (caixa), em pixels do projeto; vazia se não há amostras.</summary>
    public IReadOnlyList<Point> OverlayTracked { get; private set; } = [];

    public int ProjectWidth => Timeline.Project?.Settings.Width ?? 1920;
    public int ProjectHeight => Timeline.Project?.Settings.Height ?? 1080;

    // ---------------------------------------------------------------- estado

    private void OnTimelineInvalidated(object? sender, EventArgs e) => Refresh();

    private void OnTimelinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.Playhead)) Refresh();
    }

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

    public MediaTime Offset(Clip c)
    {
        var offset = Timeline.FrameRate.SnapToFrame(Timeline.Playhead) - c.Start;
        return MediaTime.Max(MediaTime.Zero, MediaTime.Min(c.Duration, offset));
    }

    private MediaTime SourceTime(Clip c) => c.FreezeAt ?? TimelineEvaluator.SourceTimeAt(c, c.Start + Offset(c));

    private MediaAsset? AssetOf(Clip c) => c.MediaAssetId is { } id ? Timeline.Project?.FindMedia(id) : null;

    private Mask? SelectedMaskOf(Clip c) => SelectedMask is { } s ? c.Masks.Find(m => m.Id == s.Id) : null;

    private Clip WithMask(Clip c, Func<Mask, Mask> change) => SelectedMask is { } s ? CompositionOps.UpdateMask(c, s.Id, change) : c;

    private static Clip WithShape(Clip c, Func<ShapeStyle, ShapeStyle> change) => c.Shape is { } s ? c with { Shape = change(s) } : c;

    private void Refresh()
    {
        var current = Current();
        HasClip = current is not null;
        if (current is not { } cur)
        {
            Masks.Clear();
            Trackers.Clear();
            SetOverlay(OverlayKind.None, [], [], []);
            return;
        }

        var clip = cur.Clip;
        var asset = AssetOf(clip);
        _updating = true;
        try
        {
            ClipTitle = asset?.Name ?? (clip.IsText ? ClipInspectorViewModel.TextTitle(clip.Text!) : clip.Shape is { } sh ? "◆  " + ShapePresets.Name(sh.Kind) : "Clipe");
            IsShape = clip.IsShape;
            CanMask = asset is { HasVideo: true } || clip.IsShape;
            IsVideo = asset is { Kind: MediaKind.Video };
            if (clip.Shape is { } shape)
            {
                SelectedShapeKind = ShapeKinds.FirstOrDefault(k => k.Value == shape.Kind);
                FillColor = Rgb(shape.FillColor);
                StrokeColor = Rgb(shape.StrokeColor);
                foreach (var v in ShapeValues) v.Refresh(clip);
            }

            SyncList(Masks, clip.Masks.Select(m => new CompositionListItem(m.Id, m.Name, MaskSummary(m))).ToList());
            if (SelectedMask is null || !clip.Masks.Exists(m => m.Id == SelectedMask.Id)) SelectedMask = Masks.FirstOrDefault();
            else SelectedMask = Masks.First(m => m.Id == SelectedMask.Id);

            var trackerItems = clip.Trackers.Select(t => new CompositionListItem(t.Id, t.Name,
                t.Samples.Count == 0 ? "não rastreado" : $"{t.Samples.Count} quadros")).ToList();
            SyncList(Trackers, trackerItems);
            if (SelectedTracker is null || !clip.Trackers.Exists(t => t.Id == SelectedTracker.Id)) SelectedTracker = Trackers.FirstOrDefault();
            else SelectedTracker = Trackers.First(t => t.Id == SelectedTracker.Id);

            var trackerOptions = new List<Option<Guid?>> { new(null, "Nenhum (máscara parada)") };
            trackerOptions.AddRange(clip.Trackers.Where(t => t.Samples.Count > 0).Select(t => new Option<Guid?>(t.Id, t.Name)));
            SyncList(MaskTrackerOptions, trackerOptions, (a, b) => a.Value == b.Value && a.Label == b.Label);

            if (SelectedMaskOf(clip) is { } mask)
            {
                MaskInvert = mask.Invert;
                MaskSmooth = mask.Smooth;
                MaskIsPath = mask.Kind == MaskKind.Path;
                SelectedMaskMode = MaskModes.First(m => m.Value == mask.Mode);
                MaskAnimated = mask.Keys.Count > 0;
                MaskKeyHere = mask.Keys.Exists(k => k.Offset == Offset(clip));
                SelectedMaskTracker = MaskTrackerOptions.FirstOrDefault(o => o.Value == mask.TrackerId) ?? MaskTrackerOptions[0];
                foreach (var v in MaskValues) v.Refresh(clip);
            }

            var links = new List<Option<(Guid, Guid)?>> { new(null, "Nenhum") };
            if (Timeline.Sequence is { } seq)
                links.AddRange(CompositionOps.Trackers(seq).Where(x => x.Clip.Id != clip.Id)
                    .Select(x => new Option<(Guid, Guid)?>((x.Clip.Id, x.Tracker.Id), $"{ClipName(x.Clip)} · {x.Tracker.Name}")));
            SyncList(LinkOptions, links, (a, b) => a.Value == b.Value && a.Label == b.Label);
            IsLinked = clip.MotionLink is not null;
            SelectedLink = clip.MotionLink is { } link
                ? LinkOptions.FirstOrDefault(o => o.Value == (link.ClipId, link.TrackerId)) ?? LinkOptions[0]
                : SelectedLink is { Value: not null } keep && LinkOptions.Contains(keep) ? keep : LinkOptions[0];
            LinkFollowScale = clip.MotionLink?.FollowScale ?? LinkFollowScale;

            HasStabilization = clip.Stabilization is not null;
            StabilizationEnabled = clip.Stabilization?.Enabled ?? false;
            StabilizationSummary = clip.Stabilization is { } st
                ? $"Movimento medido em {st.Steps.Count} quadros. Ampliação usada: {CompositionMath.Correction(st, st.Steps[0].SourceTime, AspectOf(asset)).Zoom.ToString("0.00", Pt)}×."
                : "";
            foreach (var v in StabilizationValues) v.Refresh(clip);
        }
        finally
        {
            _updating = false;
        }
        UpdateOverlay(cur.Track, clip);
    }

    /// <summary>"#AARRGGBB" → "#RRGGBB" (o campo de cor edita só RGB).</summary>
    private static string Rgb(string color) => color.Length == 9 && color[0] == '#' ? "#" + color[3..] : color;

    private static double AspectOf(MediaAsset? a) => a is { Width: > 0, Height: > 0 } ? (double)a.Width / a.Height : 16.0 / 9;

    private string ClipName(Clip c) =>
        AssetOf(c)?.Name ?? (c.IsText ? ClipInspectorViewModel.TextTitle(c.Text!) : c.Shape is { } s ? ShapePresets.Name(s.Kind) : "Clipe");

    private static string MaskSummary(Mask m)
    {
        var kind = m.Kind switch { MaskKind.Rectangle => "Retângulo", MaskKind.Ellipse => "Elipse", _ => "Livre" };
        var parts = new List<string> { kind };
        if (m.Invert) parts.Add("invertida");
        if (m.Keys.Count > 0) parts.Add("animada");
        if (m.TrackerId is not null) parts.Add("rastreada");
        return string.Join(" · ", parts);
    }

    private static void SyncList<T>(ObservableCollection<T> target, IReadOnlyList<T> items, Func<T, T, bool>? same = null)
    {
        same ??= EqualityComparer<T>.Default.Equals;
        if (target.Count == items.Count && target.Zip(items).All(p => same(p.First, p.Second))) return;
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    // ---------------------------------------------------------------- camada sobre o monitor

    /// <summary>Geometria da imagem da camada no instante: transformação efetiva, tamanho da imagem e ajuste ao quadro.</summary>
    private (Transform Transform, double ImageWidth, double ImageHeight, double Fit)? Geometry(Track track, Clip clip)
    {
        if (Timeline.Project is not { } project || Timeline.Sequence is not { } seq) return null;
        var layer = TimelineEvaluator.Layer(project, track, clip, clip.Start + Offset(clip), seq);
        var transform = layer?.Transform ?? ClipAnimation.TransformAt(clip, Offset(clip));
        if (clip.Shape is { } shape) return (transform, shape.Width, shape.Height, 1);
        if (AssetOf(clip) is { Width: > 0, Height: > 0 } asset)
            return (transform, asset.Width, asset.Height, CompositionMath.FitScale(asset.Width, asset.Height, project.Settings.Width, project.Settings.Height));
        return null;
    }

    private void UpdateOverlay(Track track, Clip clip)
    {
        if (!EditOnMonitor || Geometry(track, clip) is not { } g)
        {
            SetOverlay(OverlayKind.None, [], [], []);
            return;
        }
        Point P(double u, double v)
        {
            var (x, y) = CompositionMath.ToProject(g.Transform, u, v, g.ImageWidth, g.ImageHeight, g.Fit);
            return new Point(x, y);
        }

        var offset = Offset(clip);
        var source = SourceTime(clip);
        if (_overlayTarget == OverlayTarget.Tracker && SelectedTracker is { } ts && clip.Trackers.Find(t => t.Id == ts.Id) is { } tracker)
        {
            var r = tracker.Region;
            var tracked = new List<Point>();
            if (CompositionMath.SampleAt(tracker, source) is { } s && tracker.Samples.Count > 0)
                tracked.AddRange([P(s.X - s.Width / 2, s.Y - s.Height / 2), P(s.X + s.Width / 2, s.Y - s.Height / 2),
                    P(s.X + s.Width / 2, s.Y + s.Height / 2), P(s.X - s.Width / 2, s.Y + s.Height / 2)]);
            var a = P(r.X - r.Width / 2, r.Y - r.Height / 2);
            var b = P(r.X + r.Width / 2, r.Y + r.Height / 2);
            var outline = new[] { a, P(r.X + r.Width / 2, r.Y - r.Height / 2), b, P(r.X - r.Width / 2, r.Y + r.Height / 2) };
            SetOverlay(OverlayKind.TrackerRegion, [a, b], outline, tracked, outline);
            return;
        }
        if (SelectedMaskOf(clip) is { } mask)
        {
            var resolved = CompositionMath.Resolve(mask, clip, offset, source);
            var kind = mask.Kind switch { MaskKind.Rectangle => OverlayKind.MaskRectangle, MaskKind.Ellipse => OverlayKind.MaskEllipse, _ => OverlayKind.MaskPath };
            var points = resolved.Points.Select(p => P(p.X, p.Y)).ToList();
            var outline = CompositionMath.Outline(resolved).Select(p => P(p.X, p.Y)).ToList();
            IReadOnlyList<Point> corners = [];
            if (kind != OverlayKind.MaskPath && resolved.Points.Count >= 2)
            {
                double l = Math.Min(resolved.Points[0].X, resolved.Points[1].X), r = Math.Max(resolved.Points[0].X, resolved.Points[1].X);
                double t = Math.Min(resolved.Points[0].Y, resolved.Points[1].Y), b = Math.Max(resolved.Points[0].Y, resolved.Points[1].Y);
                corners = [P(l, t), P(r, t), P(r, b), P(l, b)];
            }
            SetOverlay(kind, points, outline, [], corners);
            return;
        }
        SetOverlay(OverlayKind.None, [], [], []);
    }

    private enum OverlayTarget
    {
        Mask,
        Tracker,
    }

    private OverlayTarget _overlayTarget = OverlayTarget.Mask;

    private void SetOverlay(OverlayKind kind, IReadOnlyList<Point> points, IReadOnlyList<Point> outline, IReadOnlyList<Point> tracked,
        IReadOnlyList<Point>? corners = null)
    {
        Overlay = kind;
        OverlayCorners = corners ?? [];
        OverlayPoints = points;
        OverlayOutline = outline;
        OverlayTracked = tracked;
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Pontos editados no monitor (pixels do projeto). Durante o arraste os comandos se fundem num só passo do histórico.
    /// </summary>
    public void SetOverlayPoints(IReadOnlyList<Point> points)
    {
        if (Current() is not { } cur || Geometry(cur.Track, cur.Clip) is not { } g) return;
        var normalized = points.Select(p =>
        {
            var (u, v) = CompositionMath.FromProject(g.Transform, p.X, p.Y, g.ImageWidth, g.ImageHeight, g.Fit);
            return new MaskPoint(u, v);
        }).ToList();
        var clip = cur.Clip;
        var offset = Offset(clip);
        var source = SourceTime(clip);
        if (Overlay == OverlayKind.TrackerRegion && SelectedTracker is { } ts && normalized.Count >= 2)
        {
            double l = Math.Clamp(Math.Min(normalized[0].X, normalized[1].X), 0, 1), r = Math.Clamp(Math.Max(normalized[0].X, normalized[1].X), 0, 1);
            double t = Math.Clamp(Math.Min(normalized[0].Y, normalized[1].Y), 0, 1), b = Math.Clamp(Math.Max(normalized[0].Y, normalized[1].Y), 0, 1);
            var region = new TrackSample(source, (l + r) / 2, (t + b) / 2, Math.Max(0.01, r - l), Math.Max(0.01, b - t));
            UpdateClip(c => CompositionOps.UpdateTracker(c, ts.Id, k => k with { Region = region }), "Ajustar região do rastreamento", "tracker.region." + ts.Id);
            return;
        }
        if (SelectedMaskOf(clip) is not { } mask) return;
        // Com rastreamento, o arraste edita a forma na referência (desfaz o deslocamento do objeto).
        if (mask.TrackerId is { } tid && clip.Trackers.Find(k => k.Id == tid) is { } tracker
            && CompositionMath.SampleAt(tracker, source) is { } now && CompositionMath.SampleAt(tracker, mask.TrackerReference) is { } reference)
        {
            var sx = reference.Width > 1e-6 ? now.Width / reference.Width : 1;
            var sy = reference.Height > 1e-6 ? now.Height / reference.Height : 1;
            normalized = normalized.Select(p => new MaskPoint(reference.X + (p.X - now.X) / sx, reference.Y + (p.Y - now.Y) / sy)).ToList();
        }
        if (mask.Kind != MaskKind.Path && normalized.Count >= 2)
            normalized = [new MaskPoint(Math.Min(normalized[0].X, normalized[1].X), Math.Min(normalized[0].Y, normalized[1].Y)),
                new MaskPoint(Math.Max(normalized[0].X, normalized[1].X), Math.Max(normalized[0].Y, normalized[1].Y))];
        UpdateClip(c => CompositionOps.UpdateMask(c, mask.Id, m => CompositionMath.SetShape(m, offset, [.. normalized])), "Editar forma da máscara",
            "mask.shape." + mask.Id);
    }

    // ---------------------------------------------------------------- edição

    public void UpdateClip(Func<Clip, Clip> change, string description, string? mergeKey = null)
    {
        if (Current() is not { } cur) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, cur.Clip.Id, change, description,
            mergeKey is null ? null : $"{cur.Clip.Id}:{mergeKey}"));
    }

    void IClipEditor.UpdateEffect(Guid effectId, Func<EffectInstance, EffectInstance> change, string description, string? mergeKey) { }
    void IClipEditor.RemoveEffect(Guid effectId) { }
    void IClipEditor.MoveEffect(Guid effectId, int direction) { }
    void IClipEditor.ChooseLut(EffectItemViewModel item) { }

    partial void OnSelectedShapeKindChanged(Option<ShapeKind>? value)
    {
        if (!_updating && value is not null) UpdateClip(c => WithShape(c, s => s with { Kind = value.Value }), $"Forma: {value.Label}");
    }

    partial void OnFillColorChanged(string value) => CommitColor(value, fill: true);

    partial void OnStrokeColorChanged(string value) => CommitColor(value, fill: false);

    private void CommitColor(string value, bool fill)
    {
        if (_updating || !ColorParameterViewModel.TryParse(value, out var r, out var g, out var b)) return;
        var hex = $"#FF{r:X2}{g:X2}{b:X2}";
        UpdateClip(c => WithShape(c, s => fill ? s with { FillColor = hex } : s with { StrokeColor = hex }),
            fill ? "Cor de preenchimento" : "Cor do contorno", fill ? "shape.fill" : "shape.strokeColor");
    }

    [RelayCommand]
    private void AddMask(string kind)
    {
        if (!CanMask) return;
        var k = Enum.Parse<MaskKind>(kind);
        var id = Guid.NewGuid();
        var name = k switch { MaskKind.Rectangle => "retângulo", MaskKind.Ellipse => "elipse", _ => "livre" };
        UpdateClip(c => CompositionOps.AddMask(c, k, id), $"Adicionar máscara ({name})");
        SelectedMask = Masks.FirstOrDefault(m => m.Id == id);
        _overlayTarget = OverlayTarget.Mask;
        EditOnMonitor = true;
        Refresh();
    }

    [RelayCommand]
    private void RemoveMask()
    {
        if (SelectedMask is not { } m) return;
        UpdateClip(c => CompositionOps.RemoveMask(c, m.Id), $"Remover {m.Name.ToLowerInvariant()}");
    }

    [RelayCommand]
    private void MoveMaskUp() => MoveMask(-1);

    [RelayCommand]
    private void MoveMaskDown() => MoveMask(1);

    private void MoveMask(int delta)
    {
        if (SelectedMask is { } m) UpdateClip(c => CompositionOps.MoveMask(c, m.Id, delta), "Reordenar máscaras");
    }

    partial void OnSelectedMaskChanged(CompositionListItem? value)
    {
        if (_updating || value is null) return;
        _overlayTarget = OverlayTarget.Mask;
        Refresh();
    }

    partial void OnSelectedTrackerChanged(CompositionListItem? value)
    {
        if (_updating || value is null) return;
        _overlayTarget = OverlayTarget.Tracker;
        Refresh();
    }

    partial void OnEditOnMonitorChanged(bool value)
    {
        if (!_updating) Refresh();
    }

    /// <summary>Volta a camada do monitor para a máscara selecionada.</summary>
    [RelayCommand]
    private void EditMaskOnMonitor()
    {
        _overlayTarget = OverlayTarget.Mask;
        EditOnMonitor = true;
        Refresh();
    }

    /// <summary>Mostra a região do rastreamento selecionado no monitor.</summary>
    [RelayCommand]
    private void EditTrackerOnMonitor()
    {
        _overlayTarget = OverlayTarget.Tracker;
        EditOnMonitor = true;
        Refresh();
    }

    partial void OnMaskInvertChanged(bool value)
    {
        if (!_updating) UpdateClip(c => WithMask(c, m => m with { Invert = value }), value ? "Inverter máscara" : "Desinverter máscara");
    }

    partial void OnMaskSmoothChanged(bool value)
    {
        if (!_updating) UpdateClip(c => WithMask(c, m => m with { Smooth = value }), value ? "Contorno suave" : "Contorno reto");
    }

    partial void OnSelectedMaskModeChanged(Option<MaskMode>? value)
    {
        if (!_updating && value is not null) UpdateClip(c => WithMask(c, m => m with { Mode = value.Value }), $"Máscara: {value.Label}");
    }

    partial void OnSelectedMaskTrackerChanged(Option<Guid?>? value)
    {
        if (_updating || value is null || SelectedMask is not { } m || Current() is not { } cur) return;
        var source = SourceTime(cur.Clip);
        UpdateClip(c => CompositionOps.FollowTracker(c, m.Id, value.Value, source),
            value.Value is null ? "Máscara parada" : $"Máscara segue {value.Label}");
    }

    /// <summary>Liga/desliga a forma-chave da máscara no cursor (máscara animada, RF149).</summary>
    [RelayCommand]
    private void ToggleMaskKey()
    {
        if (SelectedMask is not { } m || Current() is not { } cur) return;
        var offset = Offset(cur.Clip);
        UpdateClip(c => CompositionOps.UpdateMask(c, m.Id, mask => CompositionMath.ToggleShapeKey(mask, offset)),
            MaskKeyHere ? "Remover forma-chave" : "Adicionar forma-chave");
    }

    // ---------------------------------------------------------------- rastreamento

    [RelayCommand]
    private void AddTracker()
    {
        if (!IsVideo || Current() is not { } cur) return;
        var id = Guid.NewGuid();
        var region = new TrackSample(SourceTime(cur.Clip), 0.5, 0.5, 0.16, 0.2);
        UpdateClip(c => CompositionOps.AddTracker(c, region, id), "Novo rastreamento");
        SelectedTracker = Trackers.FirstOrDefault(t => t.Id == id);
        _overlayTarget = OverlayTarget.Tracker;
        EditOnMonitor = true;
        Refresh();
    }

    [RelayCommand]
    private void RemoveTracker()
    {
        if (SelectedTracker is { } t) UpdateClip(c => CompositionOps.RemoveTracker(c, t.Id), $"Remover {t.Name.ToLowerInvariant()}");
    }

    private bool CanAnalyze() => !IsBusy;

    /// <summary>
    /// Rastreia a região do rastreamento selecionado em todo o clipe, a partir do quadro onde foi marcada (para a frente
    /// e para trás), por visão computacional (RF111, RF150).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task Track()
    {
        if (SelectedTracker is not { } item || Current() is not { } cur || cur.Clip.Trackers.Find(t => t.Id == item.Id) is not { } tracker) return;
        if (Prepare(cur.Clip) is not { } prep) return;
        var (asset, path, frames) = prep;
        var start = tracker.Region.SourceTime;
        var forward = frames.Where(t => t >= start).ToList();
        var backward = frames.Where(t => t <= start).Reverse().ToList();
        if (forward.Count == 0 || forward[0] != start) forward.Insert(0, start);
        if (backward.Count == 0 || backward[0] != start) backward.Insert(0, start);
        var total = forward.Count + backward.Count;
        var clipId = cur.Clip.Id;

        var samples = await RunAnalysis("Rastreando o movimento…", (progress, token) =>
        {
            using var source = _frames.Create(false);
            BgraImage? Frame(MediaTime t) => source.GetFrame(asset, path, t, AnalysisWidth, AnalysisWidth);
            var fw = MotionAnalyzer.Track(Frame, forward, tracker.Region, new Progress<double>(p => progress(p * forward.Count / total)), token);
            var bw = MotionAnalyzer.Track(Frame, backward, tracker.Region,
                new Progress<double>(p => progress((forward.Count + p * backward.Count) / total)), token);
            return fw.Concat(bw).ToList();
        });
        if (samples is null) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, clipId,
            c => CompositionOps.UpdateTracker(c, item.Id, k => k with { Samples = CompositionMath.MergeSamples([], samples) }),
            $"Rastrear movimento ({samples.Count} quadros)"));
    }

    // ---------------------------------------------------------------- vínculo

    [RelayCommand]
    private void ApplyLink()
    {
        if (SelectedLink?.Value is not { } target)
        {
            UpdateClip(CompositionOps.Unlink, "Soltar do rastreamento");
            return;
        }
        var reference = Timeline.FrameRate.SnapToFrame(Timeline.Playhead);
        var follow = LinkFollowScale;
        UpdateClip(c => CompositionOps.Link(c, target.ClipId, target.TrackerId, reference, follow), $"Seguir {SelectedLink.Label}");
    }

    [RelayCommand]
    private void Unlink() => UpdateClip(CompositionOps.Unlink, "Soltar do rastreamento");

    partial void OnLinkFollowScaleChanged(bool value)
    {
        if (!_updating && IsLinked)
            UpdateClip(c => c.MotionLink is { } l ? c with { MotionLink = l with { FollowScale = value } } : c,
                value ? "Acompanhar a escala do objeto" : "Não acompanhar a escala");
    }

    // ---------------------------------------------------------------- estabilização

    /// <summary>Mede o movimento da câmera em todo o clipe e liga a estabilização (RF075).</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task Stabilize()
    {
        if (Current() is not { } cur || Prepare(cur.Clip) is not { } prep) return;
        var (asset, path, frames) = prep;
        var clipId = cur.Clip.Id;
        var smoothness = cur.Clip.Stabilization?.Smoothness ?? 1;
        var steps = await RunAnalysis("Analisando o movimento da câmera…", (progress, token) =>
        {
            using var source = _frames.Create(false);
            return MotionAnalyzer.AnalyzeCameraMotion(t => source.GetFrame(asset, path, t, 640, 640), frames,
                new Progress<double>(progress), token).ToList();
        });
        if (steps is null) return;
        Timeline.Execute(new UpdateClipCommand(Timeline.SequenceId, clipId, c => CompositionOps.SetStabilization(c, steps, smoothness),
            "Estabilizar vídeo"));
    }

    partial void OnStabilizationEnabledChanged(bool value)
    {
        if (!_updating)
            UpdateClip(c => c.Stabilization is { } s ? c with { Stabilization = s with { Enabled = value } } : c,
                value ? "Ativar estabilização" : "Desativar estabilização");
    }

    [RelayCommand]
    private void RemoveStabilization() => UpdateClip(c => c with { Stabilization = null }, "Remover estabilização");

    // ---------------------------------------------------------------- análise em segundo plano

    private (MediaAsset Asset, string Path, IReadOnlyList<MediaTime> Frames)? Prepare(Clip clip)
    {
        if (AssetOf(clip) is not { Kind: MediaKind.Video } asset) return null;
        if (_projects.Current?.Media.AvailablePaths.TryGetValue(asset.Id, out var path) != true || path is null)
        {
            _dialogs.ShowInfo("Mídia indisponível", "O arquivo deste clipe não foi encontrado. Reconecte a mídia e tente de novo.");
            return null;
        }
        var rate = asset.FrameRate ?? Timeline.FrameRate;
        var frames = CompositionOps.SourceFrames(clip, rate, clip.SourceIn, clip.SourceOut);
        if (frames.Count < 2)
        {
            _dialogs.ShowInfo("Clipe curto demais", "O clipe precisa ter pelo menos dois quadros para a análise de movimento.");
            return null;
        }
        return (asset, path, frames);
    }

    private async Task<T?> RunAnalysis<T>(string text, Func<Action<double>, CancellationToken, T> work) where T : class
    {
        _analysis = new CancellationTokenSource();
        var token = _analysis.Token;
        IsBusy = true;
        BusyText = text;
        Progress = 0;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Report(double p) => dispatcher?.BeginInvoke(() => Progress = Math.Round(Math.Clamp(p, 0, 1) * 100));
        try
        {
            return await Task.Run(() => work(Report, token), token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is OpenCvSharp.OpenCVException or InvalidOperationException or System.IO.IOException or DllNotFoundException)
        {
            _logger.LogWarning(ex, "Falha na análise de movimento");
            _dialogs.ShowInfo("Análise de movimento", $"Não foi possível analisar o vídeo: {ex.Message}");
            return null;
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
            _analysis.Dispose();
            _analysis = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelAnalysis() => _analysis?.Cancel();

    public void Dispose()
    {
        _analysis?.Cancel();
        Timeline.Invalidated -= OnTimelineInvalidated;
        Timeline.PropertyChanged -= OnTimelinePropertyChanged;
    }
}
