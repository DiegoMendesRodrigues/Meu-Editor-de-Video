namespace MeuEditorDeVideo.AIEngine.Runtime;

/// <summary>Grava WAV PCM 16 bits (arquivos gerados pela IA, importados como mídia comum).</summary>
public static class WavFile
{
    public static void Write(string path, ReadOnlySpan<float> interleaved, int sampleRate, int channels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs))
        {
            var dataBytes = interleaved.Length * 2;
            w.Write("RIFF"u8);
            w.Write(36 + dataBytes);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(dataBytes);
            var buffer = new byte[Math.Max(2, Math.Min(dataBytes, 1 << 16))];
            var pos = 0;
            foreach (var v in interleaved)
            {
                var s = (short)Math.Clamp((int)MathF.Round(v * 32767f), short.MinValue, short.MaxValue);
                buffer[pos++] = (byte)s;
                buffer[pos++] = (byte)(s >> 8);
                if (pos == buffer.Length)
                {
                    w.Write(buffer, 0, pos);
                    pos = 0;
                }
            }
            if (pos > 0) w.Write(buffer, 0, pos);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
