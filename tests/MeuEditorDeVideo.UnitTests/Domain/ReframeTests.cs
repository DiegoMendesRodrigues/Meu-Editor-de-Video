// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public sealed class ReframeTests
{
    private static MediaTime S(double seconds) => MediaTime.FromSecondsApprox(seconds);

    private static Clip Video(double duration = 4) => new()
    {
        Id = Guid.NewGuid(),
        MediaAssetId = Guid.NewGuid(),
        Start = MediaTime.Zero,
        Duration = S(duration),
    };

    [Theory]
    [InlineData(9, 16, 1080, 1920)]
    [InlineData(1, 1, 1080, 1080)]
    [InlineData(4, 5, 1080, 1350)]
    [InlineData(16, 9, 1920, 1080)]
    public void Size_keeps_the_short_side(int aw, int ah, int w, int h)
    {
        Assert.Equal((w, h), Reframe.Size(new ProjectSettings(), aw, ah));
    }

    [Fact]
    public void Without_samples_the_clip_covers_the_frame_centered()
    {
        // 1920×1080 num quadro 1080×1920: contain = 0,5625; cover = 1,7778 → escala relativa 3,16.
        var clip = Reframe.Apply(Video(), 1920, 1080, 1080, 1920);
        Assert.Equal(1920.0 / 1080 / (1080.0 / 1920), clip.Transform.ScaleX, 6);
        Assert.Equal(clip.Transform.ScaleX, clip.Transform.ScaleY);
        Assert.Equal(0, clip.Transform.X);
        Assert.False(ClipAnimation.IsAnimated(clip, AnimatedProperty.X));
    }

    [Fact]
    public void Back_to_16x9_restores_the_original_framing()
    {
        // 9:16 e depois 16:9 (centro do quadro): o projeto volta a 1920×1080 e o vídeo 16:9 a escala 1, centralizado.
        var vertical = new ProjectSettings { Width = 1080, Height = 1920 };
        Assert.Equal((1920, 1080), Reframe.Size(vertical, 16, 9));
        var clip = Reframe.Apply(Reframe.Apply(Video(), 1920, 1080, 1080, 1920), 1920, 1080, 1920, 1080);
        Assert.Equal(1, clip.Transform.ScaleX, 6);
        Assert.Equal(0, clip.Transform.X);
        Assert.Equal(0, clip.Transform.Y);
        // Já em 16:9: aplicar de novo não muda nada (o diálogo avisa em vez de criar uma etapa vazia).
        Assert.Equal(clip.Transform, Reframe.Apply(clip, 1920, 1080, 1920, 1080).Transform);
    }

    [Fact]
    public void Follows_the_focus_and_never_shows_the_edges()
    {
        // Imagem exibida com 3413 px de largura num quadro de 1080: deslocamento máximo ±1166 px.
        var samples = new[]
        {
            new FocusSample(S(0), 0.5, 0.5),
            new FocusSample(S(1), 0.7, 0.5),
            new FocusSample(S(2), 1.0, 0.5),
            new FocusSample(S(3), 1.0, 0.5),
        };
        var clip = Reframe.Apply(Video(), 1920, 1080, 1080, 1920, samples);
        var shown = 1920 * (1920.0 / 1080);
        var max = (shown - 1080) / 2;

        Assert.True(ClipAnimation.IsAnimated(clip, AnimatedProperty.X));
        Assert.Equal(0, ClipAnimation.ValueAt(clip, AnimatedProperty.X, S(0)), 1);
        Assert.Equal(-0.2 * shown, ClipAnimation.ValueAt(clip, AnimatedProperty.X, S(1)), 0);
        Assert.Equal(-max, ClipAnimation.ValueAt(clip, AnimatedProperty.X, S(2)), 0);
        // Sem folga na vertical (a imagem cobre exatamente a altura): Y fica parado.
        Assert.False(ClipAnimation.IsAnimated(clip, AnimatedProperty.Y));
    }

    [Fact]
    public void Small_movements_do_not_create_keyframes()
    {
        var samples = Enumerable.Range(0, 17).Select(i => new FocusSample(S(i * 0.25), 0.5 + (i % 2) * 0.001, 0.5)).ToList();
        var clip = Reframe.Apply(Video(), 1920, 1080, 1080, 1920, samples);
        Assert.False(ClipAnimation.IsAnimated(clip, AnimatedProperty.X));
    }

    [Fact]
    public void Smooth_averages_neighbours()
    {
        var samples = new[] { 0.0, 0.0, 1.0, 0.0, 0.0 }.Select((x, i) => new FocusSample(S(i), x, 0.5)).ToList();
        var smooth = Reframe.Smooth(samples, 1);
        Assert.Equal(1 / 3.0, smooth[1].X, 6);
        Assert.Equal(1 / 3.0, smooth[2].X, 6);
        Assert.Equal(0, smooth[0].X, 6);
    }
}
