// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.AudioEngine;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.IntegrationTests;

public sealed class SdlAudioOutputTests
{
    [Fact]
    public async Task Playing_silence_advances_the_clock_and_freezes_on_stop()
    {
        SdlAudioOutput output;
        try
        {
            output = new SdlAudioOutput();
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException
                                   && Environment.GetEnvironmentVariable("MED_EXIGIR_AUDIO") != "1")
        {
            return; // sem dispositivo de áudio neste ambiente (MED_EXIGIR_AUDIO=1 transforma isso em falha)
        }

        using (output)
        {
            var ring = new AudioRingBuffer(AudioFormat.SampleRate * AudioFormat.Channels); // 1 s
            ring.Write(new float[AudioFormat.SampleRate * AudioFormat.Channels / 2]);       // 0,5 s de silêncio
            Assert.Equal(0, output.PlayedSeconds);

            output.Start(ring);
            await Task.Delay(700);
            var played = output.PlayedSeconds;
            Assert.InRange(played, 0.05, 0.9);

            output.Stop();
            var frozen = output.PlayedSeconds;
            await Task.Delay(100);
            Assert.Equal(frozen, output.PlayedSeconds);
        }
    }
}
