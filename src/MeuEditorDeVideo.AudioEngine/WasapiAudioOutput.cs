#if MED_WASAPI
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MeuEditorDeVideo.Core.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeuEditorDeVideo.AudioEngine;

/// <summary>Saída WASAPI compartilhada (latência ~60 ms) via NAudio.</summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioOutput : IAudioOutput
{
    private readonly WasapiOut _device;
    private readonly RingProvider _provider = new();

    public WasapiAudioOutput()
    {
        _device = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        _device.Init(_provider);
    }

    public bool HasDevice => true;

    public float Volume
    {
        get => _provider.Volume;
        set => _provider.Volume = Math.Clamp(value, 0f, 1f);
    }

    public double PlayedSeconds
    {
        get
        {
            var format = _device.OutputWaveFormat;
            return format.AverageBytesPerSecond > 0 ? (double)_device.GetPosition() / format.AverageBytesPerSecond : 0;
        }
    }

    public void Start(AudioRingBuffer buffer)
    {
        _provider.Buffer = buffer;
        _device.Play();
    }

    public void Stop()
    {
        _device.Stop();
        _provider.Buffer = null;
    }

    public void Dispose() => _device.Dispose();

    private sealed class RingProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);

        public volatile AudioRingBuffer? Buffer;

        public float Volume { get; set; } = 1;

        public int Read(byte[] buffer, int offset, int count)
        {
            var floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count - count % 4));
            var read = Buffer?.Read(floats) ?? 0;
            var volume = Volume;
            if (volume < 0.999f)
                for (var i = 0; i < read; i++) floats[i] *= volume;
            floats[read..].Clear(); // falta de dados: silêncio, sem interromper o relógio
            return count;
        }
    }
}
#endif
