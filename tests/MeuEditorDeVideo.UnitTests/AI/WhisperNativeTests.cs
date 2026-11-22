using Whisper.net;

namespace MeuEditorDeVideo.UnitTests.AI;

public sealed class WhisperNativeTests
{
    // Sem modelo: prova que o carregador do Whisper.net resolve a libwhisper e as libggml do sistema atual.
    [Fact]
    public void Biblioteca_nativa_do_Whisper_carrega() => Assert.False(string.IsNullOrWhiteSpace(WhisperFactory.GetRuntimeInfo()));
}
