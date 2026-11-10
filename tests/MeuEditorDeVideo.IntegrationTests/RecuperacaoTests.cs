using System.IO;
using System.Text.Json.Nodes;
using MeuEditorDeVideo.Application.Media;
using MeuEditorDeVideo.AIEngine.Vision;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Errors;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Editing;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.IntegrationTests.Support;
using MeuEditorDeVideo.RenderingEngine;
using OpenCvSharp;

namespace MeuEditorDeVideo.IntegrationTests;

/// <summary>
/// §14.5: cenários de recuperação ponta a ponta com o DI real. Reinício é simulado criando um novo container sobre a
/// mesma pasta temporária. Cenários já cobertos em detalhe pelos testes unitários estão indicados nos comentários.
/// </summary>
[Collection(IntegrationMediaCollection.Name)]
public sealed class RecuperacaoTests : IDisposable
{
    /// <summary>PID que não corresponde a nenhum processo (a sessão aparece como encerrada).</summary>
    private const int ProcessoEncerrado = int.MaxValue;

    private readonly IntegrationMedia _media;
    private readonly string _root = TempFiles.NewRoot("recuperacao");
    private readonly List<AppContainer> _containers = [];

    public RecuperacaoTests(IntegrationMedia media) => _media = media;

    private string ProjetosDir => Path.Combine(_root, "projetos");

    private AppContainer Iniciar()
    {
        var app = new AppContainer(_root, apagarRaiz: false);
        _containers.Add(app);
        return app;
    }

    private async Task<AppContainer> ProjetoComVideoAsync(AppContainer app, string nome)
    {
        Directory.CreateDirectory(ProjetosDir);
        await app.Get<IProjectService>().CreateAsync(new NewProjectRequest(nome, ProjetosDir,
            new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }, StorageMode.Linked));
        return app;
    }

    /// <summary>Edita o session.json da sessão (como o MedProjectStore grava) para parecer um processo que já encerrou.</summary>
    private void MarcarSessaoComoEncerrada(Guid projectId)
    {
        var path = Path.Combine(_root, "local", "autosave", projectId.ToString("N"), "session.json");
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        node["processId"] = ProcessoEncerrado;
        File.WriteAllText(path, node.ToJsonString());
    }

    // ------------------------------------------------------------------ 1. desligamento inesperado

    [Fact]
    public async Task Desligamento_inesperado_deixa_salvamento_automatico_oferecido_e_restauravel()
    {
        var app = await ProjetoComVideoAsync(Iniciar(), "Obra");
        var projects = app.Get<IProjectService>();
        projects.Execute(new RenameProjectCommand("Obra editada"));
        var projectId = projects.Current!.Project.Id;
        Assert.True(await app.Get<AutosaveScheduler>().SaveNowAsync());

        // Queda: o processo some sem chamar CloseAsync (nada de EndSession). Só o autosave sobra no disco.
        app.Dispose();
        MarcarSessaoComoEncerrada(projectId);

        var nova = Iniciar();
        var restaurador = nova.Get<IProjectService>();
        var recuperavel = Assert.Single(restaurador.FindRecoverable());
        Assert.Equal("Obra editada", recuperavel.Name);

        await restaurador.OpenRecoveredAsync(recuperavel);

        Assert.True(restaurador.Current!.IsRecovered);
        Assert.True(restaurador.Current.IsDirty);
        Assert.Equal("Obra editada", restaurador.Current.Project.Name);

        restaurador.DiscardRecoverable(recuperavel);
        Assert.Empty(restaurador.FindRecoverable());
    }

    // ------------------------------------------------------------------ 2. falha durante o salvamento

    [Fact]
    public async Task Falha_durante_o_salvamento_mantem_o_med_original_intacto()
    {
        // Atomicidade e pedido sem mídia (MED-1009) já cobertos por MedProjectStoreTests.Falha_ao_salvar_preserva_o_arquivo_anterior_byte_a_byte.
        var app = await ProjetoComVideoAsync(Iniciar(), "Atomico");
        var projects = app.Get<IProjectService>();
        var path = projects.Current!.FilePath!;
        var antes = await File.ReadAllBytesAsync(path);
        projects.Execute(new RenameProjectCommand("Alterado"));

        // Destino somente-leitura: a substituição atômica do arquivo é recusada pelo sistema.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var ex = await Assert.ThrowsAsync<MedException>(() => projects.SaveAsync());
            Assert.Contains(ex.Error.Code, new[] { "MED-1006", "MED-1008", "MED-1010" });
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Equal(antes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(ProjetosDir, "*.tmp"));
        Assert.True(projects.Current!.IsDirty); // nada foi marcado como salvo

        await projects.SaveAsync(); // depois que o problema sumiu, salvar funciona
        Assert.False(projects.Current.IsDirty);
    }

    // ------------------------------------------------------------------ 3. falta de espaço em disco

    [Fact]
    public async Task Espaco_insuficiente_em_disco_interrompe_a_exportacao_com_erro_em_portugues()
    {
        // O RenderEngine estima o espaço necessário (trechos + arquivo final) e compara com o espaço livre do disco
        // (CheckDiskSpace). Para exceder o espaço livre sem gravar gigabytes, usa-se FFV1 em 8192×8192 a 240 FPS,
        // cuja estimativa passa de 48 GB por segundo de vídeo.
        var app = Iniciar();
        var projects = app.Get<IProjectService>();
        Directory.CreateDirectory(ProjetosDir);
        await projects.CreateAsync(new NewProjectRequest("Disco", ProjetosDir,
            new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }, StorageMode.Linked));
        var asset = Assert.Single((await app.Get<IMediaImportService>().ImportAsync([_media.Video])).Imported);

        var livre = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_root))!).AvailableFreeSpace;
        var segundos = Math.Ceiling(livre / 4.0e10) + 1;
        var clipes = (int)Math.Ceiling(segundos / 3);
        for (var i = 0; i < clipes; i++)
        {
            var seq = projects.Current!.Project.MainSequence;
            Guid Trilha(TrackKind k) => seq.Tracks.First(t => t.Kind == k).Id;
            projects.Execute(new AddMediaToTimelineCommand(seq.Id, asset.Id, Trilha(TrackKind.Video), Trilha(TrackKind.Audio),
                MediaTime.FromSecondsApprox(3 * i), MediaTime.Zero));
        }

        var projeto = projects.Current!.Project;
        var saida = Path.Combine(_root, "saida", "estouro.mkv");
        var pedido = new RenderRequest
        {
            Project = projeto,
            SequenceId = projeto.MainSequence.Id,
            MediaPaths = projects.Current.Media.AvailablePaths,
            Settings = new ExportSettings
            {
                Container = ExportContainer.Mkv,
                VideoCodec = VideoCodec.Ffv1,
                AudioCodec = AudioCodec.None,
                Encoder = EncoderPreference.Software,
                Width = 8192,
                Height = 8192,
                FrameRate = new FrameRate(240, 1),
            },
            OutputPath = saida,
            WorkDirectory = Path.Combine(_root, "trabalho", "disco"),
            HardwareDecoding = false,
        };

        var ex = await Assert.ThrowsAsync<MedException>(() => app.Get<IRenderEngine>().RenderAsync(pedido));

        Assert.Equal("MED-3004", ex.Error.Code);
        Assert.Contains("Não há espaço suficiente em disco", ex.Error.Message);
        Assert.False(File.Exists(saida));
    }

    // ------------------------------------------------------------------ 4. mídia ausente

    [Fact]
    public async Task Midia_apagada_nao_impede_abrir_o_projeto_e_a_sinaliza_como_ausente()
    {
        var app = Iniciar();
        var projects = app.Get<IProjectService>();
        await ProjetoComVideoAsync(app, "Sumida");
        var copia = Path.Combine(_root, "midias", "sumida.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
        File.Copy(_media.Video, copia);
        var asset = Assert.Single((await app.Get<IMediaImportService>().ImportAsync([copia])).Imported);
        await projects.SaveAsync();
        var path = projects.Current!.FilePath!;
        await projects.CloseAsync();
        app.Dispose(); // encerra o log antes de "reiniciar" (o Serilog global aceita um logger por vez)

        File.Delete(copia);

        var reaberto = Iniciar().Get<IProjectService>();
        await reaberto.OpenAsync(path);

        Assert.NotNull(reaberto.Current);
        Assert.True(reaberto.Current.HasMissingMedia);
        Assert.Equal(asset.Id, Assert.Single(reaberto.Current.Media.Missing).AssetId);
        Assert.Equal("Sumida", reaberto.Current.Project.Name);
        Assert.Single(reaberto.Current.Project.Media);

        reaberto.Execute(new RenameProjectCommand("Sumida editada")); // o projeto continua editável
        Assert.True(reaberto.Current.IsDirty);
    }

    // ------------------------------------------------------------------ 5. falha durante a renderização

    // Regressão (F12): a falha de decodificação era engolida e a exportação gravava quadros pretos sem aviso. Agora
    // IVideoFrameSource.Failure é verificado pelo RenderEngine, que lança MED-3003 (MedErrors.RenderDecodeFailed).
    [Fact]
    public async Task Midia_corrompida_na_exportacao_gera_erro_claro_e_nao_deixa_saida_parcial()
    {
        var app = Iniciar();
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(), Kind = MediaKind.Video, Name = "cortado.mp4", OriginalPath = _media.Truncated,
            FileSize = new FileInfo(_media.Truncated).Length, LastWriteUtc = File.GetLastWriteTimeUtc(_media.Truncated),
            Duration = MediaTime.FromSeconds(3), Width = 640, Height = 360, FrameRate = FrameRate.Fps25,
        };
        var projeto = Project.CreateNew("Corrompido", new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 },
            StorageMode.Linked, DateTimeOffset.UtcNow);
        projeto = new AddMediaAssetsCommand([asset]).Apply(projeto);
        var seq = projeto.MainSequence;
        var trilhaDeVideo = seq.Tracks.First(t => t.Kind == TrackKind.Video).Id;
        projeto = new AddMediaToTimelineCommand(seq.Id, asset.Id, trilhaDeVideo, null, MediaTime.Zero, MediaTime.Zero).Apply(projeto);

        var saida = Path.Combine(_root, "saida", "corrompido.mp4");
        var ex = await Assert.ThrowsAsync<MedException>(() => app.Get<IRenderEngine>().RenderAsync(new RenderRequest
        {
            Project = projeto,
            SequenceId = seq.Id,
            MediaPaths = new Dictionary<Guid, string> { [asset.Id] = _media.Truncated },
            Settings = new ExportSettings { Encoder = EncoderPreference.Software, Width = 320, Height = 180, VideoBitrateKbps = 1000 },
            OutputPath = saida,
            WorkDirectory = Path.Combine(_root, "trabalho", "corrompido"),
            HardwareDecoding = false,
        }));

        Assert.StartsWith("MED-3", ex.Error.Code, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.False(File.Exists(saida));
        Assert.False(File.Exists(saida + ".parcial"));
    }

    [Fact]
    public async Task Cancelar_a_exportacao_no_meio_nao_deixa_arquivo_de_saida()
    {
        var app = Iniciar();
        var projects = app.Get<IProjectService>();
        await ProjetoComVideoAsync(app, "Cancelada");
        var asset = Assert.Single((await app.Get<IMediaImportService>().ImportAsync([_media.Video])).Imported);
        var seq = projects.Current!.Project.MainSequence;
        Guid Trilha(TrackKind k) => seq.Tracks.First(t => t.Kind == k).Id;
        projects.Execute(new AddMediaToTimelineCommand(seq.Id, asset.Id, Trilha(TrackKind.Video), Trilha(TrackKind.Audio), MediaTime.Zero, MediaTime.Zero));
        var projeto = projects.Current.Project;
        var saida = Path.Combine(_root, "saida", "cancelada.mp4");

        using var cts = new CancellationTokenSource();
        // Cancela já no primeiro relatório (fase de preparação): o trecho de 3 s termina em menos de 100 ms, a janela
        // em que o RenderEngine limita os relatórios de codificação, então não há outro ponto de disparo confiável.
        var progresso = new CancelarNoProgresso(cts);
        var pedido = new RenderRequest
        {
            Project = projeto,
            SequenceId = seq.Id,
            MediaPaths = projects.Current.Media.AvailablePaths,
            Settings = new ExportSettings { Encoder = EncoderPreference.Software, Width = 320, Height = 180, VideoBitrateKbps = 1000 },
            OutputPath = saida,
            WorkDirectory = Path.Combine(_root, "trabalho", "cancelada"),
            HardwareDecoding = false,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Get<IRenderEngine>().RenderAsync(pedido, progresso, cts.Token));

        Assert.True(progresso.Disparou);
        Assert.False(File.Exists(saida));
        Assert.False(File.Exists(saida + ".parcial"));
    }

    private sealed class CancelarNoProgresso(CancellationTokenSource cts) : IProgress<RenderProgress>
    {
        public bool Disparou { get; private set; }

        public void Report(RenderProgress value)
        {
            if (Disparou) return;
            Disparou = true;
            cts.Cancel();
        }
    }

    // ------------------------------------------------------------------ 6. interrupção do processamento de IA

    [ModeloFact("modnet.onnx")]
    public async Task Cancelar_o_processamento_de_IA_no_meio_nao_deixa_mascaras_incompletas_e_permite_retomar()
    {
        // Os testes unitários de segmentação cobrem o cache e o cálculo; aqui vale o efeito em disco de um cancelamento.
        var app = Iniciar();
        var projects = app.Get<IProjectService>();
        await ProjetoComVideoAsync(app, "IA");
        var asset = Assert.Single((await app.Get<IMediaImportService>().ImportAsync([_media.Video])).Imported);
        var seq = projects.Current!.Project.MainSequence;
        Guid Trilha(TrackKind k) => seq.Tracks.First(t => t.Kind == k).Id;
        projects.Execute(new AddMediaToTimelineCommand(seq.Id, asset.Id, Trilha(TrackKind.Video), Trilha(TrackKind.Audio), MediaTime.Zero, MediaTime.Zero));
        var projeto = projects.Current.Project;
        var clip = projects.Current.Project.MainSequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips[0];
        var caminhos = projects.Current.Media.AvailablePaths;
        var segmentacao = app.Get<ISegmentationService>();
        var cache = app.Get<MatteCache>();
        var pastaDoAtivo = Path.Combine(cache.Directory!, asset.Id.ToString("N"));

        using var cts = new CancellationTokenSource();
        var progresso = new CancelarNaContagem(cts, reportesAntesDeCancelar: 3);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => segmentacao.PrecomputeAsync(projeto, clip, caminhos, progresso, cts.Token));

        // Só há máscaras completas (PNG válido), nenhum arquivo temporário.
        var prontas = Directory.Exists(pastaDoAtivo) ? Directory.GetFiles(pastaDoAtivo) : [];
        Assert.NotEmpty(prontas);
        Assert.True(prontas.Length < 75);
        Assert.All(prontas, f => Assert.EndsWith(".png", f, StringComparison.Ordinal));
        foreach (var f in prontas)
        {
            using var mascara = Cv2.ImDecode(File.ReadAllBytes(f), ImreadModes.Grayscale);
            Assert.False(mascara.Empty());
        }
        Assert.Empty(Directory.GetFiles(cache.Directory!, "*.tmp", SearchOption.AllDirectories));

        // Retomar: a nova passada calcula o que falta, sem erro.
        await segmentacao.PrecomputeAsync(projeto, clip, caminhos, null, CancellationToken.None);
        for (long k = 0; k < 75; k++) Assert.True(cache.Contains(asset.Id, k), $"Quadro {k} sem máscara após retomar.");
    }

    private sealed class CancelarNaContagem(CancellationTokenSource cts, int reportesAntesDeCancelar) : IProgress<double>
    {
        private int _reportes;

        public void Report(double value)
        {
            if (++_reportes == reportesAntesDeCancelar) cts.Cancel();
        }
    }

    public void Dispose()
    {
        foreach (var app in _containers) app.Dispose();
        TempFiles.Delete(_root);
    }
}
