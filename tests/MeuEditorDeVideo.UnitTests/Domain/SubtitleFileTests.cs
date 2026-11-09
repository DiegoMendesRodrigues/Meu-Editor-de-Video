using System.IO;
using System.Text;
using MeuEditorDeVideo.Core.Subtitles;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.UnitTests.Domain;

public class SubtitleFileTests
{
    private static MediaTime Ms(long ms) => new(ms * (MediaTime.TicksPerSecond / 1000));

    private static long ToMs(MediaTime t) => (long)Math.Round(t.Ticks / 705_600.0);

    private const string BasicSrt =
        "1\n00:00:01,000 --> 00:00:03,500\nPrimeira linha\nSegunda linha\n\n" +
        "2\n00:00:04,000 --> 00:01:02,250\nTerceira\n\n" +
        "3\n01:00:00,001 --> 01:00:05,999\nQuarta\n";

    [Fact]
    public void SRT_basico_le_tres_cues_com_tempos_exatos_em_ms()
    {
        var cues = SubtitleFile.Parse(BasicSrt, SubtitleFormat.Srt);

        Assert.Equal(3, cues.Count);
        Assert.Equal(Ms(1000), cues[0].Start);
        Assert.Equal(Ms(3500), cues[0].End);
        Assert.Equal("Primeira linha\nSegunda linha", cues[0].Text);
        Assert.Equal(4000, ToMs(cues[1].Start));
        Assert.Equal(62_250, ToMs(cues[1].End));
        Assert.Equal(3_600_001, ToMs(cues[2].Start));
        Assert.Equal(3_605_999, ToMs(cues[2].End));
    }

    [Fact]
    public void SRT_sem_indice_com_linhas_em_branco_extras_e_ponto_como_separador()
    {
        const string content = "\n\n00:00:01.000 --> 00:00:02.000\nA\n\n\n\n00:00:03,5 --> 00:00:04,25\nB\n\n\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal(1000, ToMs(cues[0].Start));
        Assert.Equal(3500, ToMs(cues[1].Start));
        Assert.Equal(4250, ToMs(cues[1].End));
        Assert.Equal("B", cues[1].Text);
    }

    [Fact]
    public void SRT_aceita_CRLF_e_BOM()
    {
        var content = "\uFEFF" + BasicSrt.Replace("\n", "\r\n");

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Equal(3, cues.Count);
        Assert.Equal("Primeira linha\nSegunda linha", cues[0].Text);
    }

    [Fact]
    public void SRT_aceita_posicao_apos_o_tempo_final()
    {
        const string content = "1\n00:00:01,000 --> 00:00:02,000 X1:10 X2:20 Y1:30\nTexto\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Single(cues);
        Assert.Equal(2000, ToMs(cues[0].End));
    }

    [Fact]
    public void Remove_tags_ass_e_decodifica_entidades()
    {
        const string content =
            "1\n00:00:01,000 --> 00:00:02,000\n<i>Olá</i> <b>mundo</b>  \n<font color=\"#ff0\">cor</font>\n\n" +
            "2\n00:00:02,000 --> 00:00:03,000\n{\\an8}Topo &amp; fundo &lt;3 &gt; &nbsp;x&lrm;\n\n" +
            "3\n00:00:03,000 --> 00:00:04,000\n<u></u>{\\an2}\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal("Olá mundo\ncor", cues[0].Text);
        Assert.Equal("Topo & fundo <3 >  x", cues[1].Text); // &gt; + &nbsp; geram dois espaços
    }

    [Fact]
    public void Remove_tags_de_vtt_e_timestamps_inline()
    {
        const string content = "WEBVTT\n\n00:00.000 --> 00:02.000\n<v Roger><c.yellow>Oi</c></v>\n<00:00:01.500>tudo bem\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Vtt);

        Assert.Single(cues);
        Assert.Equal("Oi\ntudo bem", cues[0].Text);
    }

    [Fact]
    public void Remove_lang_e_ignora_cue_vazio_apos_limpeza()
    {
        const string content = "1\n00:00:01,000 --> 00:00:02,000\n<lang en>Hi</lang>\n\n2\n00:00:02,000 --> 00:00:03,000\n<i></i>\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Single(cues);
        Assert.Equal("Hi", cues[0].Text);
    }

    [Fact]
    public void VTT_com_texto_no_cabecalho_ignora_NOTE_STYLE_e_REGION()
    {
        const string content =
            "WEBVTT - legendas de teste\nKind: captions\n\n" +
            "NOTE\nEste bloco é um comentário --> não é cue\n\n" +
            "STYLE\n::cue { color: yellow }\n\n" +
            "REGION\nid:fred\n\n" +
            "intro\n00:01.000 --> 00:02.000 align:start position:10%\nPrimeiro\n\n" +
            "00:00:03.000 --> 00:00:04.000\nSegundo\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Vtt);

        Assert.Equal(2, cues.Count);
        Assert.Equal(1000, ToMs(cues[0].Start));
        Assert.Equal(2000, ToMs(cues[0].End));
        Assert.Equal("Primeiro", cues[0].Text);
        Assert.Equal(3000, ToMs(cues[1].Start));
        Assert.Equal("Segundo", cues[1].Text);
    }

    [Fact]
    public void VTT_aceita_cabecalho_sem_texto_e_linha_com_tab()
    {
        var cues = SubtitleFile.Parse("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nA\n", SubtitleFormat.Vtt);
        var tab = SubtitleFile.Parse("WEBVTT\tcabeçalho\n\n00:00:01.000 --> 00:00:02.000\nA\n", SubtitleFormat.Vtt);

        Assert.Single(cues);
        Assert.Single(tab);
    }

    [Fact]
    public void VTT_com_identificadores_de_cue()
    {
        const string content = "WEBVTT\n\nintro-1\n00:00:01.000 --> 00:00:02.000\nA\n\n2\n00:00:02.000 --> 00:00:03.000\nB\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Vtt);

        Assert.Equal(["A", "B"], cues.Select(c => c.Text).ToArray());
    }

    [Fact]
    public void Cabecalho_VTT_invalido_lanca_excecao()
    {
        var ex = Assert.Throws<SubtitleParseException>(() =>
            SubtitleFile.Parse("00:00:01.000 --> 00:00:02.000\nA\n", SubtitleFormat.Vtt));

        Assert.Equal("Arquivo WebVTT inválido: falta o cabeçalho WEBVTT.", ex.Message);
    }

    [Fact]
    public void Cabecalho_VTT_colado_em_outra_palavra_e_invalido()
    {
        Assert.Throws<SubtitleParseException>(() =>
            SubtitleFile.Parse("WEBVTTX\n\n00:00:01.000 --> 00:00:02.000\nA\n", SubtitleFormat.Vtt));
    }

    [Fact]
    public void SRT_sem_nenhuma_legenda_valida_lanca_excecao()
    {
        var ex = Assert.Throws<SubtitleParseException>(() =>
            SubtitleFile.Parse("isto não é uma legenda\n\nnem isto\n", SubtitleFormat.Srt));

        Assert.Equal("O arquivo não contém legendas válidas.", ex.Message);
    }

    [Fact]
    public void SRT_vazio_retorna_lista_vazia()
    {
        Assert.Empty(SubtitleFile.Parse("  \n\n", SubtitleFormat.Srt));
    }

    [Fact]
    public void Cues_com_fim_nao_posterior_ao_inicio_sao_descartados()
    {
        const string content = "1\n00:00:02,000 --> 00:00:02,000\nZero\n\n2\n00:00:03,000 --> 00:00:01,000\nInvertido\n\n3\n00:00:04,000 --> 00:00:05,000\nOK\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Single(cues);
        Assert.Equal("OK", cues[0].Text);
    }

    [Fact]
    public void Cues_fora_de_ordem_e_sobrepostos_sao_ordenados_por_inicio()
    {
        const string content =
            "1\n00:00:05,000 --> 00:00:06,000\nC\n\n" +
            "2\n00:00:01,000 --> 00:00:04,000\nA\n\n" +
            "3\n00:00:02,000 --> 00:00:03,000\nB\n";

        var cues = SubtitleFile.Parse(content, SubtitleFormat.Srt);

        Assert.Equal(["A", "B", "C"], cues.Select(c => c.Text).ToArray());
        Assert.True(cues[0].Start < cues[1].Start && cues[1].Start < cues[2].Start);
        Assert.True(cues[0].End > cues[1].Start); // sobreposição preservada
    }

    [Theory]
    [InlineData(SubtitleFormat.Srt)]
    [InlineData(SubtitleFormat.Vtt)]
    public void Write_e_Parse_preservam_tempos_em_ms_e_texto(SubtitleFormat format)
    {
        var original = new List<SubtitleCue>
        {
            new(Ms(1), Ms(999), "Início\nsegunda linha"),
            new(Ms(10_000), Ms(10_001), "Um milissegundo"),
            new(Ms(3_600_123), Ms(3_601_456), "Depois de uma hora"),
        };

        var text = SubtitleFile.Write(original, format);
        var parsed = SubtitleFile.Parse(text, format);

        Assert.Equal(original.Count, parsed.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Start, parsed[i].Start);
            Assert.Equal(original[i].End, parsed[i].End);
            Assert.Equal(original[i].Text, parsed[i].Text);
        }
    }

    [Fact]
    public void Write_SRT_gera_indices_e_formato_esperado()
    {
        var text = SubtitleFile.Write(
            [new SubtitleCue(Ms(1500), Ms(2000), "Olá")], SubtitleFormat.Srt);

        Assert.Equal("1\n00:00:01,500 --> 00:00:02,000\nOlá\n\n", text);
    }

    [Fact]
    public void Write_VTT_gera_cabecalho_e_formato_esperado()
    {
        var text = SubtitleFile.Write(
            [new SubtitleCue(Ms(1500), Ms(2000), "Olá")], SubtitleFormat.Vtt);

        Assert.Equal("WEBVTT\n\n00:00:01.500 --> 00:00:02.000\nOlá\n\n", text);
    }

    [Fact]
    public void Write_descarta_linhas_vazias_e_clampa_tempos_negativos()
    {
        var text = SubtitleFile.Write(
            [new SubtitleCue(new MediaTime(-Ms(500).Ticks), Ms(1000), "A\n\n  \nB  ")], SubtitleFormat.Srt);

        Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\nA\nB\n\n", text);
    }

    [Fact]
    public void Decode_Windows1252_quando_nao_e_UTF8_valido()
    {
        byte[] bytes = [0x61, 0xE7, 0xE3, 0x6F];

        Assert.Equal("ação", SubtitleFile.Decode(bytes));
    }

    [Fact]
    public void Decode_honra_BOM_UTF8()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("ação")];

        Assert.Equal("ação", SubtitleFile.Decode(bytes));
    }

    [Fact]
    public void Decode_honra_BOM_UTF16LE()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("oi")];

        Assert.Equal("oi", SubtitleFile.Decode(bytes));
    }

    [Fact]
    public void Decode_UTF8_sem_BOM()
    {
        Assert.Equal("ação", SubtitleFile.Decode(Encoding.UTF8.GetBytes("ação")));
    }

    [Fact]
    public void FormatFromExtension_reconhece_srt_e_vtt_sem_distinguir_caixa()
    {
        Assert.Equal(SubtitleFormat.Srt, SubtitleFile.FormatFromExtension("legenda.SRT"));
        Assert.Equal(SubtitleFormat.Vtt, SubtitleFile.FormatFromExtension(@"C:\x\legenda.vtt"));
        Assert.Null(SubtitleFile.FormatFromExtension("arquivo.txt"));
    }

    [Fact]
    public void Save_e_Load_fazem_ida_e_volta_por_arquivo_temporario()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meu-editor-teste-{Guid.NewGuid():N}.srt");
        try
        {
            var original = new List<SubtitleCue>
            {
                new(Ms(1000), Ms(2500), "ação\nlinha"),
                new(Ms(4000), Ms(4001), "Fim"),
            };

            SubtitleFile.Save(path, original, SubtitleFormat.Srt);
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF); // sem BOM
            Assert.Contains((byte)'\r', bytes); // CRLF no SRT

            var loaded = SubtitleFile.Load(path);

            Assert.Equal(original, loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_detecta_VTT_pelo_conteudo_quando_extensao_e_desconhecida()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meu-editor-teste-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nOi\n", new UTF8Encoding(false));

            var cues = SubtitleFile.Load(path);

            Assert.Single(cues);
            Assert.Equal("Oi", cues[0].Text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
