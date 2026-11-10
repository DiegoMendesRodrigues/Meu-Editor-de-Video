using System.IO;
using MeuEditorDeVideo.AIEngine;
using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.AIEngine.Runtime;
using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;

namespace MeuEditorDeVideo.UnitTests.AI;

/// <summary>
/// Testes de IA §14.4 — "Utilização de CPU e GPU" (RF167, CA021): o mesmo modelo roda na CPU e na GPU (DirectML), com recuo
/// automático para a CPU quando não há GPU utilizável, e os dois caminhos produzem a mesma máscara.
/// </summary>
public sealed class AiDeviceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "med-ai-device-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private InstalledModel Modnet()
    {
        var registry = new ModelRegistry(Path.Combine(_dir, "modelos"), builtInDirectory: AiEngineServiceCollectionExtensions.BuiltInModelsDirectory);
        var model = registry.Find(ModelCapability.PersonSegmentation);
        Assert.NotNull(model);
        Assert.True(model.Manifest.Gpu, "o MODNet declara suporte a GPU no manifesto");
        return model;
    }

    [ModelFact("modnet.onnx")]
    public void CPU_escolhida_roda_na_CPU()
    {
        using var session = new OnnxSessionFactory(new Settings(AiDevice.Cpu)).Create(Modnet());
        Assert.Equal(AiDevice.Cpu, session.Device);
    }

    [ModelFact("modnet.onnx")]
    public void GPU_escolhida_usa_DirectML_ou_recua_para_a_CPU_e_a_mascara_e_a_mesma()
    {
        var model = Modnet();
        using var gpu = new OnnxSessionFactory(new Settings(AiDevice.Gpu)).Create(model);
        using var cpu = new OnnxSessionFactory(new Settings(AiDevice.Cpu)).Create(model);
        Assert.Equal(AiDevice.Cpu, cpu.Device);
        // Sem GPU compatível com DirectML (ex.: servidor de CI), a sessão cai para a CPU sem erro.
        Assert.True(gpu.Device is AiDevice.Gpu or AiDevice.Cpu);

        var frame = Person(160, 90);
        var a = new ModnetSegmenter(gpu.Session, 256).Segment(frame);
        var b = new ModnetSegmenter(cpu.Session, 256).Segment(frame);
        Assert.Equal((b.Width, b.Height), (a.Width, a.Height));

        double diff = 0;
        for (var i = 0; i < a.Alpha.Length; i++) diff += Math.Abs(a.Alpha[i] - b.Alpha[i]);
        Assert.True(diff / a.Alpha.Length < 6, $"máscaras da GPU e da CPU diferem demais (média {diff / a.Alpha.Length:0.0})");
    }

    [ModelFact("modnet.onnx")]
    public void Modelo_sem_suporte_a_GPU_roda_na_CPU_mesmo_com_GPU_escolhida()
    {
        var model = Modnet();
        var cpuOnly = model with { Manifest = model.Manifest with { Gpu = false } };
        using var session = new OnnxSessionFactory(new Settings(AiDevice.Gpu)).Create(cpuOnly);
        Assert.Equal(AiDevice.Cpu, session.Device);
    }

    /// <summary>Silhueta simples (cabeça e tronco) clara sobre fundo escuro.</summary>
    private static BgraImage Person(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var head = Math.Pow(x - w / 2.0, 2) + Math.Pow(y - h * 0.3, 2) < Math.Pow(h * 0.15, 2);
                var body = Math.Abs(x - w / 2.0) < w * 0.15 && y > h * 0.45;
                byte v = head || body ? (byte)210 : (byte)30;
                px[i] = (byte)(v * 0.8); px[i + 1] = (byte)(v * 0.9); px[i + 2] = v; px[i + 3] = 255;
            }
        return new BgraImage(w, h, px);
    }

    private sealed class Settings(AiDevice device) : ISettingsService
    {
        public AppSettings Current { get; } = new() { AiDevice = device };
        public event EventHandler<AppSettings>? Changed { add { } remove { } }
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
