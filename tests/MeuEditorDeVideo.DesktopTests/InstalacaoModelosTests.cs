// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Security.Cryptography;
using Avalonia.Threading;
using MeuEditorDeVideo.AIEngine.Models;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.ViewModels;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Depois de instalar um modelo, a tela não volta para "Baixando…".</summary>
public class InstalacaoModelosTests
{
    [Fact]
    public void Aviso_de_download_nao_substitui_o_fim_da_instalacao()
    {
        var dir = Directory.CreateTempSubdirectory("med-instalacao-").FullName;
        try
        {
            byte[] conteudo = [1, 2, 3, 4];
            var sha = Convert.ToHexStringLower(SHA256.HashData(conteudo));
            var entrada = new ModelCatalogEntry
            {
                Id = "modelo", Name = "Modelo", Version = "1.0", Source = "teste", Capability = ModelCapability.SpeechRecognition, // oferecida em todos os sistemas
                Purpose = "teste", Features = ["Recurso de teste"], File = "modelo.onnx", License = "MIT", LicenseFile = "lic.txt",
                MemoryMb = 1, Url = new Uri("https://huggingface.co/x/modelo/resolve/abc/modelo.onnx"), Sha256 = sha, SizeBytes = conteudo.Length,
                ExtraFiles =
                [
                    new() { File = "extra.json", Url = new Uri("https://huggingface.co/x/modelo/resolve/abc/extra.json"), Sha256 = sha, SizeBytes = conteudo.Length },
                ],
            };
            File.WriteAllText(Path.Combine(dir, "lic.txt"), "MIT License");

            // O progresso do segundo arquivo é criado com este contexto: o que for postado nele só chega depois do fim.
            var atrasado = new ContextoAtrasado();
            var arquivos = 0;
            var downloader = Stub.Of<IModelDownloader>(s => s.On(nameof(IModelDownloader.DownloadAsync), a =>
            {
                File.WriteAllBytes((string)a[1]!, conteudo);
                SynchronizationContext.SetSynchronizationContext(++arquivos == 1 ? atrasado : null);
                ((IProgress<long>)a[4]!).Report((long)a[2]!);
                return Task.CompletedTask;
            }));
            var registro = Stub.Of<IModelRegistry>();
            var installer = new CatalogModelInstaller(new ModelCatalog([entrada], dir), registro, downloader, dir);

            Ui.Run(() =>
            {
                var servicos = Stub.Of<IServiceProvider>(s => s.On(nameof(IServiceProvider.GetService), _ => installer));
                var dialogos = Stub.Of<IDialogService>(s => s.On(nameof(IDialogService.Confirm), _ => true));
                using var vm = new ModelCatalogViewModel(servicos, registro, dialogos);

                var instalacao = vm.InstallAsync([entrada.Id]);
                var limite = Environment.TickCount64 + 30_000;
                while (!instalacao.IsCompleted && Environment.TickCount64 < limite)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(5);
                }
                Assert.True(instalacao.IsCompletedSuccessfully, "a instalação não terminou");
                atrasado.Entregar();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Instalado", vm.Items[0].Progress);
                Assert.StartsWith("1 modelo(s) instalado(s)", vm.Status);
                Assert.Equal(100, vm.Percent);
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class ContextoAtrasado : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Aviso, object? Estado)> _retidos = [];

        public override void Post(SendOrPostCallback d, object? state) => _retidos.Add((d, state));

        public void Entregar() => _retidos.ForEach(r => r.Aviso(r.Estado));
    }
}
