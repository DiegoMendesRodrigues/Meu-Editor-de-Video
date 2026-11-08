using System.IO;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.UnitTests.Domain;

public sealed class MediaOrganizationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-seq-" + Guid.NewGuid().ToString("N"));

    private static Project WithMedia(params MediaAsset[] assets) =>
        Project.CreateNew("P", new ProjectSettings(), StorageMode.Linked, DateTimeOffset.Now) with { Media = [.. assets] };

    private static MediaAsset Asset(string name) => new()
    {
        Id = Guid.NewGuid(), Kind = MediaKind.Video, Name = name, OriginalPath = @"C:\m\" + name,
    };

    [Fact]
    public void SetMediaCategory_sets_and_clears()
    {
        var a = Asset("a.mp4");
        var b = Asset("b.mp4");
        var p = new SetMediaCategoryCommand([a.Id, b.Id], "  Entrevistas ").Apply(WithMedia(a, b));
        Assert.All(p.Media, m => Assert.Equal("Entrevistas", m.Category));

        p = new SetMediaCategoryCommand([a.Id], " ").Apply(p);
        Assert.Null(p.FindMedia(a.Id)!.Category);
        Assert.Equal("Entrevistas", p.FindMedia(b.Id)!.Category);
    }

    [Fact]
    public void RenameMedia_trims_and_rejects_empty()
    {
        var a = Asset("a.mp4");
        var p = new RenameMediaCommand(a.Id, " Abertura ").Apply(WithMedia(a));
        Assert.Equal("Abertura", p.FindMedia(a.Id)!.Name);
        Assert.Throws<EditRejectedException>(() => new RenameMediaCommand(a.Id, "  ").Apply(p));
        Assert.Throws<EditRejectedException>(() => new RenameMediaCommand(Guid.NewGuid(), "x").Apply(p));
    }

    private void Touch(params string[] names)
    {
        Directory.CreateDirectory(_dir);
        foreach (var n in names) File.WriteAllBytes(Path.Combine(_dir, n), [1]);
    }

    [Fact]
    public void Detects_padded_contiguous_run_around_selected_frame()
    {
        Touch("cena_0008.png", "cena_0009.png", "cena_0010.png", "cena_0011.png", "cena_0020.png", "outra_0009.png", "cena_0009.jpg");
        var seq = ImageSequenceDetector.Detect(Path.Combine(_dir, "cena_0010.png"));
        Assert.NotNull(seq);
        Assert.Equal(("cena_", ".png", 4, 8, 4), (seq.Prefix, seq.Suffix, seq.Digits, seq.FirstNumber, seq.FrameCount));
        Assert.Equal(Path.Combine(_dir, "cena_0011.png"), seq.PathOf(11));
        Assert.EndsWith("cena_%04d.png", seq.FfmpegPattern);
    }

    [Fact]
    public void Detects_unpadded_numbers()
    {
        Touch("f9.png", "f10.png", "f11.png");
        var seq = ImageSequenceDetector.Detect(Path.Combine(_dir, "f9.png"));
        Assert.NotNull(seq);
        Assert.Equal(0, seq.Digits);
        Assert.Equal(3, seq.FrameCount);
        Assert.Equal("f10.png", seq.FileName(10));
    }

    [Fact]
    public void Isolated_or_unnumbered_images_are_not_sequences()
    {
        Touch("foto.png", "solo_0001.png", "solo_0003.png");
        Assert.Null(ImageSequenceDetector.Detect(Path.Combine(_dir, "foto.png")));
        Assert.Null(ImageSequenceDetector.Detect(Path.Combine(_dir, "solo_0001.png")));
        Assert.Null(ImageSequenceDetector.Detect(Path.Combine(_dir, "nao_existe_0001.mp4")));
    }

    [Fact]
    public void Media_formats_classify_extensions_case_insensitively()
    {
        Assert.True(MediaFormats.IsSupported("a.MP4"));
        Assert.True(MediaFormats.IsSupported("b.Flac"));
        Assert.True(MediaFormats.IsSupported("c.gif"));
        Assert.False(MediaFormats.IsSupported("d.docx"));
        Assert.StartsWith("Mídias suportadas|", MediaFormats.OpenFileFilter);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
