using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public sealed class CropZoomTests
{
    private static (Project P, Clip Clip) Build(Transform t)
    {
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = MediaKind.Video, Name = "g.mp4", OriginalPath = @"C:\m\g.mp4",
            Duration = MediaTime.FromSeconds(10), Width = 1920, Height = 1080,
        };
        var clip = new Clip
        {
            Id = Guid.NewGuid(), MediaAssetId = media.Id, Start = MediaTime.Zero, Duration = MediaTime.FromSeconds(5), Transform = t,
            Keyframes = System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableList<Keyframe>>.Empty
                .Add(AnimatedProperty.Scale, [new Keyframe(MediaTime.Zero, 2)]),
        };
        var p = Project.CreateNew("P", new ProjectSettings { Width = 1920, Height = 1080 }, StorageMode.Linked, DateTimeOffset.Now) with { Media = [media] };
        var seq = p.MainSequence;
        var visual = seq.Tracks.First(tr => tr.IsVisual);
        return (p.WithSequence(seq with { Tracks = seq.Tracks.Replace(visual, visual with { Clips = [clip] }) }), clip);
    }

    private static Clip Find(Project p, Guid id) => p.MainSequence.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == id);

    [Fact]
    public void Cropped_area_is_zoomed_to_fill_the_frame_centered()
    {
        // Metade direita e de baixo (proporção 16:9): amplia 2×, e a área vai para o centro.
        var (p, clip) = Build(new Transform { RotationDegrees = 30 });
        p = new CropZoomCommand(p.MainSequence.Id, clip.Id, 0.5, 0.5, 0, 0).Apply(p);

        var c = Find(p, clip.Id);
        var t = c.Transform;
        Assert.Equal(2, t.ScaleX, 6);
        Assert.Equal(0, t.RotationDegrees);
        Assert.Equal(0.5, t.CropLeft);
        Assert.Equal(0.5, t.CropTop);
        var (cx, cy) = CompositionMath.ToProject(t, 0.75, 0.75, 1920, 1080, 1);
        Assert.Equal(0, cx, 3);
        Assert.Equal(0, cy, 3);
        var (lx, ty) = CompositionMath.ToProject(t, 0.5, 0.5, 1920, 1080, 1);
        Assert.Equal(-960, lx, 3);
        Assert.Equal(-540, ty, 3);
        Assert.False(c.Keyframes.ContainsKey(AnimatedProperty.Scale));
    }

    [Fact]
    public void Area_with_other_proportion_fits_inside_the_frame()
    {
        // Faixa vertical (um quarto da largura, altura toda): cabe pela altura, escala 1.
        var (p, clip) = Build(Transform.Identity);
        p = new CropZoomCommand(p.MainSequence.Id, clip.Id, 0.375, 0, 0.375, 0).Apply(p);
        var t = Find(p, clip.Id).Transform;
        Assert.Equal(1, t.ScaleX, 6);
        Assert.Equal(0, t.X, 3);
    }

    [Fact]
    public void Mirrored_clip_keeps_the_area_centered()
    {
        var (p, clip) = Build(new Transform { FlipHorizontal = true });
        p = new CropZoomCommand(p.MainSequence.Id, clip.Id, 0, 0, 0.5, 0.5).Apply(p);
        var t = Find(p, clip.Id).Transform;
        var (cx, cy) = CompositionMath.ToProject(t, 0.25, 0.25, 1920, 1080, 1);
        Assert.Equal(0, cx, 3);
        Assert.Equal(0, cy, 3);
    }

    [Fact]
    public void Without_crop_the_clip_returns_to_the_original_framing()
    {
        var (p, clip) = Build(new Transform { X = 100, ScaleX = 3, ScaleY = 3, CropLeft = 0.2 });
        p = new CropZoomCommand(p.MainSequence.Id, clip.Id, 0, 0, 0, 0).Apply(p);
        var t = Find(p, clip.Id).Transform;
        Assert.Equal(1, t.ScaleX, 6);
        Assert.Equal(0, t.X, 3);
        Assert.Equal(0, t.CropLeft);
    }

    [Fact]
    public void Tiny_area_is_rejected()
    {
        var (p, clip) = Build(Transform.Identity);
        Assert.Throws<EditRejectedException>(() => new CropZoomCommand(p.MainSequence.Id, clip.Id, 0.5, 0, 0.49, 0).Apply(p));
    }
}
