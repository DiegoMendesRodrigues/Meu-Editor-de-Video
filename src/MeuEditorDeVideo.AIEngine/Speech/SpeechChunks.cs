namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// Preparação do áudio para o Whisper: localiza os trechos com fala e os agrupa em blocos. Transcrever só os trechos com
/// fala evita que o modelo "invente" frases no silêncio ou no ruído (alucinação) e mantém o tempo das legendas preso ao
/// início real da fala.
/// </summary>
public static class SpeechChunks
{
    /// <summary>
    /// Detector conservador por energia, usado quando o Silero VAD não está instalado: descarta só o que é praticamente
    /// silêncio (abaixo de <paramref name="gateDb"/> dBFS), para nunca perder fala com ruído de fundo. Devolve 0/1 por janela.
    /// </summary>
    public static float[] EnergyProbabilities(ReadOnlySpan<float> mono, int windowSamples, double gateDb = -50)
    {
        var count = mono.Length / windowSamples + (mono.Length % windowSamples > 0 ? 1 : 0);
        var db = new double[count];
        for (var w = 0; w < count; w++)
        {
            var start = w * windowSamples;
            var end = Math.Min(mono.Length, start + windowSamples);
            double sum = 0;
            for (var i = start; i < end; i++) sum += mono[i] * (double)mono[i];
            db[w] = 10 * Math.Log10(Math.Max(sum / Math.Max(1, end - start), 1e-12));
        }
        return db.Select(d => d > gateDb ? 1f : 0f).ToArray();
    }

    /// <summary>
    /// Agrupa trechos de fala próximos (pausa menor que <paramref name="maxGapSeconds"/>) em blocos de até
    /// <paramref name="maxChunkSeconds"/>; o Whisper processa janelas de 30 s, então menos blocos é mais rápido.
    /// Trechos mais longos que o limite ficam inteiros (o Whisper avança pela janela sozinho).
    /// </summary>
    public static IReadOnlyList<(long Start, long End)> Group(IReadOnlyList<(long Start, long End)> regions, int sampleRate,
        double maxGapSeconds = 3, double maxChunkSeconds = 28)
    {
        var maxGap = (long)(maxGapSeconds * sampleRate);
        var maxChunk = (long)(maxChunkSeconds * sampleRate);
        var result = new List<(long Start, long End)>();
        foreach (var (s, e) in regions.Where(r => r.End > r.Start).OrderBy(r => r.Start))
        {
            if (result.Count > 0 && s - result[^1].End <= maxGap && e - result[^1].Start <= maxChunk)
                result[^1] = (result[^1].Start, Math.Max(result[^1].End, e));
            else
                result.Add((s, e));
        }
        return result;
    }
}
