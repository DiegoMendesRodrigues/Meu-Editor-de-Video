using System.Text.Json;
using Xunit;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Opções de execução do aplicativo que, se mudarem, quebram recursos só em uso real (não nos testes headless).</summary>
public class RuntimeConfigTests
{
    [Fact]
    public void Interop_COM_embutido_fica_ligado_para_o_audio_WASAPI_e_a_fala_SAPI()
    {
        // O NAudio (MMDeviceEnumerator) e a SAPI usam COM; desligado, "Reproduzir" lança NotSupportedException no Windows.
        var path = Path.Combine(AppContext.BaseDirectory, "MeuEditorDeVideo.runtimeconfig.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var options = doc.RootElement.GetProperty("runtimeOptions");
        if (options.TryGetProperty("configProperties", out var props)
            && props.TryGetProperty("System.Runtime.InteropServices.BuiltInComInterop.IsSupported", out var value))
            Assert.True(value.GetBoolean());
    }
}
