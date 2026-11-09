namespace MeuEditorDeVideo.AIEngine.Speech;

/// <summary>
/// Converte probabilidades de fala por janela em intervalos de fala (amostras), com histerese como o
/// <c>get_speech_timestamps</c> do Silero, preenchimento nas bordas e fusão de sobreposições.
/// </summary>
public static class SpeechSegments
{
    /// <param name="probs">Probabilidade de fala de cada janela de <paramref name="windowSamples"/> amostras.</param>
    /// <param name="totalSamples">Duração total do áudio em amostras (limita os intervalos).</param>
    /// <param name="threshold">Limiar para iniciar a fala; o fim exige probabilidade abaixo de <c>threshold − 0,15</c>.</param>
    public static IReadOnlyList<(long Start, long End)> FromProbabilities(IReadOnlyList<float> probs, int windowSamples, long totalSamples,
        float threshold = 0.5f, int sampleRate = 16000, int minSpeechMs = 250, int minSilenceMs = 300, int padMs = 60)
    {
        var result = new List<(long Start, long End)>();
        if (probs.Count == 0 || windowSamples <= 0 || totalSamples <= 0) return result;

        var minSpeech = (long)sampleRate * minSpeechMs / 1000;
        var minSilence = (long)sampleRate * minSilenceMs / 1000;
        var pad = (long)sampleRate * padMs / 1000;
        var negative = Math.Max(threshold - 0.15f, 0.01f);

        long? start = null;
        long silenceStart = -1;
        var raw = new List<(long Start, long End)>();

        for (var i = 0; i < probs.Count; i++)
        {
            var p = probs[i];
            var pos = (long)i * windowSamples;
            if (p >= threshold)
            {
                silenceStart = -1;
                start ??= pos;
            }
            else if (p < negative && start is { } s)
            {
                if (silenceStart < 0) silenceStart = pos;
                if (pos - silenceStart >= minSilence)
                {
                    AddIfLongEnough(raw, s, silenceStart, minSpeech, totalSamples);
                    start = null;
                    silenceStart = -1;
                }
            }
        }
        if (start is { } last) AddIfLongEnough(raw, last, totalSamples, minSpeech, totalSamples);

        // Preenchimento e fusão dos intervalos sobrepostos (ordenados por construção).
        foreach (var (rs, re) in raw)
        {
            var s = Math.Max(0, rs - pad);
            var e = Math.Min(totalSamples, re + pad);
            if (e <= s) continue;
            if (result.Count > 0 && s <= result[^1].End)
                result[^1] = (result[^1].Start, Math.Max(result[^1].End, e));
            else
                result.Add((s, e));
        }
        return result;
    }

    private static void AddIfLongEnough(List<(long Start, long End)> list, long start, long end, long minSpeech, long totalSamples)
    {
        end = Math.Min(end, totalSamples);
        if (end - start >= minSpeech && end > start) list.Add((start, end));
    }
}
