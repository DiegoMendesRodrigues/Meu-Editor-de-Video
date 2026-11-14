using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public sealed class ApplyCropTests
{
    private static MediaAsset Video(int w = 1920, int h = 1080) => new()
    {
        Id = Guid.NewGuid(), Kind = MediaKind.Video, Name = "g.mp4", OriginalPath = @"C:\m\g.mp4",
        Duration = MediaTime.FromSeconds(10), Width = w, Height = h,
    };

    private static (Project P, Clip Clip) Build(Transform t, Clip? other = null)
    {
        var media = Video();
        var clip = new Clip { Id = Guid.NewGuid(), MediaAssetId = media.Id, Start = MediaTime.Zero, Duration = MediaTime.FromSeconds(5), Transform = t };
        var p = Project.CreateNew("P", new ProjectSettings { Width = 1920, Height = 1080 }, StorageMode.Linked, DateTimeOffset.Now) with { Media = [media] };
        var seq = p.MainSequence;
        var visual = seq.Tracks.Where(tr => tr.IsVisual).ToList();
        var tracks = seq.Tracks.Replace(visual[0], visual[0] with { Clips = [clip] });
        if (other is not null) tracks = tracks.Replace(visual[1], visual[1] with { Clips = [other] });
        return (p.WithSequence(seq with { Tracks = tracks }), clip);
    }

    private static Clip Find(Project p, Guid id) => p.MainSequence.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == id);

    [Fact]
    public void Project_takes_the_cropped_size_and_the_area_fills_the_frame()
    {
        // 1920×1080 sem 25% da esquerda e 10% da base: 1440×972, área recortada no centro do quadro.
        var (p, clip) = Build(new Transform { CropLeft = 0.25, CropBottom = 0.10 });
        p = new ApplyCropCommand(p.MainSequence.Id, clip.Id).Apply(p);

        Assert.Equal(1440, p.Settings.Width);
        Assert.Equal(972, p.Settings.Height);
        var t = Find(p, clip.Id).Transform;
        // Ponto central da área recortada (u = 0,625, v = 0,45) cai no centro do novo quadro, e os cantos nas bordas.
        var fit = CompositionMath.FitScale(1920, 1080, 1440, 972);
        var (cx, cy) = CompositionMath.ToProject(t, 0.625, 0.45, 1920, 1080, fit);
        Assert.Equal(0, cx, 3);
        Assert.Equal(0, cy, 3);
        var (lx, ty) = CompositionMath.ToProject(t, 0.25, 0, 1920, 1080, fit);
        Assert.Equal(-720, lx, 3);
        Assert.Equal(-486, ty, 3);
    }

    [Fact]
    public void Other_clips_follow_the_same_reframing()
    {
        // Um texto/sobreposição no centro da área recortada continua no centro.
        var overlay = new Clip { Id = Guid.NewGuid(), Start = MediaTime.Zero, Duration = MediaTime.FromSeconds(5), Transform = new Transform { X = 240, Y = 0 } };
        var (p, clip) = Build(new Transform { CropLeft = 0.25 }, overlay);
        p = new ApplyCropCommand(p.MainSequence.Id, clip.Id).Apply(p);
        var t = Find(p, overlay.Id).Transform;
        Assert.Equal(0, t.X, 3);
        Assert.Equal(1, t.ScaleX, 6); // 1 px do quadro antigo = 1 px do novo (a área tem o tamanho da mídia)
    }

    [Fact]
    public void Rejects_clip_without_crop_or_rotated()
    {
        var (p, clip) = Build(new Transform());
        Assert.Throws<EditRejectedException>(() => new ApplyCropCommand(p.MainSequence.Id, clip.Id).Apply(p));
        (p, clip) = Build(new Transform { CropLeft = 0.1, RotationDegrees = 10 });
        Assert.Throws<EditRejectedException>(() => new ApplyCropCommand(p.MainSequence.Id, clip.Id).Apply(p));
    }
}
