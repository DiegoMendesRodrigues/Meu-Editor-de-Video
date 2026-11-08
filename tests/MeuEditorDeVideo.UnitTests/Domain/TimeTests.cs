using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public class TimeTests
{
    public static TheoryData<int, int> StandardRates => new()
    {
        { 24000, 1001 }, { 24, 1 }, { 25, 1 }, { 30000, 1001 }, { 30, 1 }, { 50, 1 }, { 60000, 1001 }, { 60, 1 },
    };

    [Theory, MemberData(nameof(StandardRates))]
    public void Taxas_padrao_sao_exatas_em_flicks(int n, int d) => Assert.True(new FrameRate(n, d).IsExactInFlicks);

    [Theory, MemberData(nameof(StandardRates))]
    public void Ida_e_volta_quadro_tempo_nao_acumula_erro_em_24_horas(int n, int d)
    {
        var rate = new FrameRate(n, d);
        long framesIn24h = rate.TimeToFrame(MediaTime.FromSeconds(24 * 3600));
        foreach (var f in new[] { 0L, 1, 1000, 1001, 123_457, framesIn24h - 1, framesIn24h })
        {
            Assert.Equal(f, rate.TimeToFrame(rate.FrameToTime(f)));
            Assert.Equal(rate.FrameToTime(f).Ticks, f * rate.FrameDuration.Ticks); // soma de quadros = posição exata
        }
    }

    [Fact]
    public void Quadro_de_2997_tem_duracao_exata() =>
        Assert.Equal(23_543_520, FrameRate.Fps29_97.FrameDuration.Ticks);

    [Theory]
    [InlineData(44100, 16_000)]
    [InlineData(48000, 14_700)]
    [InlineData(96000, 7_350)]
    public void Amostras_de_audio_sao_exatas(int rate, long ticksPerSample)
    {
        Assert.Equal(ticksPerSample, MediaTime.FromSamples(1, rate).Ticks);
        Assert.Equal(rate * 3600L, MediaTime.FromSeconds(3600).ToSamples(rate));
    }

    [Fact]
    public void TimeToFrame_arredonda_para_baixo_inclusive_negativos()
    {
        var r = FrameRate.Fps25;
        Assert.Equal(0, r.TimeToFrame(new MediaTime(r.FrameDuration.Ticks - 1)));
        Assert.Equal(-1, r.TimeToFrame(new MediaTime(-1)));
    }

    [Fact]
    public void SnapToFrame_escolhe_quadro_mais_proximo()
    {
        var r = FrameRate.Fps25;
        var d = r.FrameDuration.Ticks;
        Assert.Equal(d, r.SnapToFrame(new MediaTime(d - 10)).Ticks);
        Assert.Equal(d, r.SnapToFrame(new MediaTime(d + 10)).Ticks);
    }

    [Fact]
    public void FrameRate_normaliza_fracao() => Assert.Equal(FrameRate.Fps30, new FrameRate(60, 2));

    [Fact]
    public void Timecode_formata_horas_minutos_segundos_quadros()
    {
        var r = FrameRate.Fps30;
        Assert.Equal("01:02:03:04", Timecode.Format(r.FrameToTime((3600 + 120 + 3) * 30 + 4), r));
        Assert.Equal("00:00:12", Timecode.FormatSeconds(MediaTime.FromSeconds(12)));
    }
}
