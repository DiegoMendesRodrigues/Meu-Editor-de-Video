using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Decodifica toda a faixa de áudio, mistura em mono e calcula picos mínimo/máximo por intervalo.</summary>
internal sealed unsafe class AudioPeakReader : StreamDecoder
{
    private SwrContext* _swr;

    public AudioPeakReader(MediaFile file, int streamIndex) : base(file, streamIndex, threads: 1)
    {
        var codec = Codec;
        AVChannelLayout mono;
        ffmpeg.av_channel_layout_default(&mono, 1);
        SwrContext* swr = null;
        Av.Check(ffmpeg.swr_alloc_set_opts2(&swr,
            &mono, AVSampleFormat.AV_SAMPLE_FMT_FLT, codec->sample_rate,
            &codec->ch_layout, codec->sample_fmt, codec->sample_rate, 0, null), "configurar conversão de áudio");
        _swr = swr;
        Av.Check(ffmpeg.swr_init(_swr), "iniciar conversão de áudio");
    }

    public WaveformData Read(int peaksPerSecond, CancellationToken ct)
    {
        var rate = Codec->sample_rate;
        if (rate <= 0) throw new InvalidOperationException("Taxa de amostragem desconhecida.");
        var bucket = Math.Max(1, rate / peaksPerSecond);

        var min = new List<float>(1024);
        var max = new List<float>(1024);
        float curMin = 0, curMax = 0;
        var inBucket = 0;
        long totalSamples = 0;

        var buffer = new float[8192];

        void Accumulate(float[] data, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var v = data[i];
                if (inBucket == 0) { curMin = v; curMax = v; }
                else { if (v < curMin) curMin = v; if (v > curMax) curMax = v; }
                if (++inBucket == bucket)
                {
                    min.Add(curMin); max.Add(curMax);
                    inBucket = 0;
                }
            }
            totalSamples += count;
        }

        while (ReadFrame())
        {
            ct.ThrowIfCancellationRequested();
            var frame = Frame;
            var needed = (int)ffmpeg.swr_get_out_samples(_swr, frame->nb_samples);
            if (needed > buffer.Length) buffer = new float[needed];
            int converted;
            fixed (float* outPtr = buffer)
            {
                var outs = (byte*)outPtr;
                converted = Av.Check(ffmpeg.swr_convert(_swr, &outs, buffer.Length, frame->extended_data, frame->nb_samples), "converter áudio");
            }
            Accumulate(buffer, converted);
        }

        // Amostras retidas no conversor.
        fixed (float* outPtr = buffer)
        {
            var outs = (byte*)outPtr;
            var rest = ffmpeg.swr_convert(_swr, &outs, buffer.Length, null, 0);
            if (rest > 0) Accumulate(buffer, rest);
        }
        if (inBucket > 0) { min.Add(curMin); max.Add(curMax); }

        var duration = Domain.Time.MediaTime.FromSamples(totalSamples, rate);
        return new WaveformData(peaksPerSecond, [.. min], [.. max], duration);
    }

    public override void Dispose()
    {
        if (_swr != null) { var s = _swr; ffmpeg.swr_free(&s); _swr = null; }
        base.Dispose();
    }
}
