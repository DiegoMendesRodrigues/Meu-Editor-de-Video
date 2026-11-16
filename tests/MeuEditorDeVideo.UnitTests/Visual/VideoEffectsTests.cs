using MeuEditorDeVideo.App.ViewModels.Inspector;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.UnitTests.Visual;

/// <summary>Efeitos de vídeo animados (vfx.*) e a biblioteca de efeitos prontos por categoria.</summary>
public sealed class VideoEffectsTests
{
    private static BgraImage Gradient(int width = 96, int height = 54)
    {
        var px = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                px[i] = (byte)(x * 255 / width);
                px[i + 1] = (byte)(y * 255 / height);
                px[i + 2] = 128;
                px[i + 3] = 255;
            }
        }
        return new BgraImage(width, height, px);
    }

    private static EffectInstance Fx(string type, int style, params (string Key, double Value)[] values) =>
        values.Aggregate(EffectCatalog.WithValue(EffectCatalog.Create(type), "style", style), (e, v) => EffectCatalog.WithValue(e, v.Key, v.Value));

    private static double MeanLuma(BgraImage img)
    {
        double sum = 0;
        for (var i = 0; i < img.Pixels.Length; i += 4) sum += 0.0722 * img.Pixels[i] + 0.7152 * img.Pixels[i + 1] + 0.2126 * img.Pixels[i + 2];
        return sum / (img.Pixels.Length / 4);
    }

    [Fact]
    public void Every_style_of_every_video_effect_renders_without_changing_the_size()
    {
        var source = Gradient();
        foreach (var definition in VideoEffects.Definitions)
        {
            var style = definition.Parameters.Single(p => p.Key == "style");
            for (var s = 0; s <= (int)style.Max; s++)
            {
                foreach (var t in new[] { 0.0, 0.7, 2.5, 4.9 })
                {
                    var (img, _) = EffectProcessor.Apply(source, [Fx(definition.Type, s)], new EffectContext(0.1, (long)(t * 30), t, 5));
                    Assert.Equal(source.Width, img.Width);
                    Assert.Equal(source.Height, img.Height);
                    Assert.Equal(source.Pixels.Length, img.Pixels.Length);
                }
            }
        }
    }

    [Fact]
    public void Video_effects_are_stylize_effects_of_the_catalog_with_a_vfx_type()
    {
        foreach (var d in VideoEffects.Definitions)
        {
            Assert.Same(d, EffectCatalog.Find(d.Type));
            Assert.Equal(EffectCategory.Stylize, d.Category);
            Assert.StartsWith("vfx.", d.Type, StringComparison.Ordinal);
            var style = d.Parameters.Single(p => p.Key == "style");
            Assert.Equal(style.Options!.Count - 1, (int)style.Max);
        }
    }

    [Fact]
    public void Opener_starts_hidden_and_leaves_the_image_untouched_after_its_duration()
    {
        var source = Gradient();
        var opener = Fx(VideoEffects.Opener, 0, ("duration", 1));
        var start = EffectProcessor.Apply(source, [opener], new EffectContext(1, 0, 0, 5)).Image;
        var after = EffectProcessor.Apply(source, [opener], new EffectContext(1, 60, 2, 5)).Image;
        Assert.True(MeanLuma(start) < 1);
        Assert.Equal(source.Pixels, after.Pixels);
    }

    [Fact]
    public void Ending_hides_the_image_at_the_end_of_the_clip_only()
    {
        var source = Gradient();
        var ending = Fx(VideoEffects.Ending, 0, ("duration", 1));
        var middle = EffectProcessor.Apply(source, [ending], new EffectContext(1, 30, 1, 5)).Image;
        var end = EffectProcessor.Apply(source, [ending], new EffectContext(1, 150, 5, 5)).Image;
        Assert.Equal(source.Pixels, middle.Pixels);
        Assert.True(MeanLuma(end) < 1);
    }

    [Fact]
    public void Shake_moves_the_image_over_time_and_is_reproducible()
    {
        var source = Gradient();
        var shake = Fx(VideoEffects.Shake, 1, ("amount", 100));
        var a = EffectProcessor.Apply(source, [shake], new EffectContext(1, 10, 0.33, 5)).Image;
        var b = EffectProcessor.Apply(source, [shake], new EffectContext(1, 20, 0.66, 5)).Image;
        var again = EffectProcessor.Apply(source, [shake], new EffectContext(1, 10, 0.33, 5)).Image;
        Assert.NotEqual(a.Pixels, b.Pixels);
        Assert.Equal(a.Pixels, again.Pixels);
    }

    [Fact]
    public void Effects_with_zero_intensity_are_neutral()
    {
        Assert.True(EffectCatalog.IsNeutral(Fx(VideoEffects.Glitch, 0, ("amount", 0))));
        Assert.False(EffectCatalog.IsNeutral(Fx(VideoEffects.Glitch, 0)));
        // Bordas, aberturas e temporizadores não têm intensidade: nunca são pulados.
        Assert.False(EffectCatalog.IsNeutral(Fx(VideoEffects.Border, 0)));
    }

    [Fact]
    public void Split_screen_repeats_the_frame_in_each_cell()
    {
        var source = Gradient(100, 50);
        var (img, _) = EffectProcessor.Apply(source, [Fx(VideoEffects.Split, 0, ("gap", 0))], new EffectContext(1, 0));
        // 2 colunas: o pixel no mesmo ponto relativo de cada metade é igual.
        int Index(int x, int y) => (y * img.Width + x) * 4;
        Assert.Equal(img.Pixels[Index(10, 25)], img.Pixels[Index(60, 25)]);
        Assert.Equal(img.Pixels[Index(10, 25) + 1], img.Pixels[Index(60, 25) + 1]);
    }

    [Fact]
    public void Library_presets_have_unique_keys_known_categories_and_valid_effects()
    {
        var presets = VideoEffectPresets.All;
        Assert.Equal(presets.Count, presets.Select(p => p.Key).Distinct().Count());
        foreach (var p in presets)
        {
            Assert.Contains(p.Category, VideoEffectPresets.Categories);
            Assert.NotEmpty(p.Build());
            Assert.All(p.Build(), e => Assert.NotNull(EffectCatalog.Find(e.Type)));
            Assert.Same(p, VideoEffectPresets.Find(p.Key));
        }
        // Toda categoria, menos Utilitários (efeitos básicos do catálogo), tem efeitos prontos.
        Assert.All(VideoEffectPresets.Categories.Where(c => c != VideoEffectPresets.Utilities), c => Assert.Contains(presets, p => p.Category == c));
    }

    [Fact]
    public void Filters_have_unique_keys_known_categories_and_color_effects()
    {
        var filters = FilterPresets.All;
        Assert.Equal(filters.Count, filters.Select(f => f.Key).Distinct().Count());
        foreach (var f in filters)
        {
            Assert.StartsWith(FilterPreset.Prefix, f.Key, StringComparison.Ordinal);
            Assert.Contains(f.Category, FilterPresets.Categories);
            Assert.NotEmpty(f.Build());
            Assert.All(f.Build(), e => Assert.NotNull(EffectCatalog.Find(e.Type)));
            Assert.Same(f, FilterPresets.Find(f.Key));
        }
        Assert.All(FilterPresets.Categories, c => Assert.Contains(filters, f => f.Category == c));
    }

    [Fact]
    public void Every_filter_changes_the_image()
    {
        var source = Gradient();
        foreach (var f in FilterPresets.All)
        {
            var (img, _) = EffectProcessor.Apply(source, f.Build(), new EffectContext(1, 0));
            Assert.NotEqual(source.Pixels, img.Pixels);
        }
    }
}
