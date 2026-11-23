using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Application.Export;
using MeuEditorDeVideo.Application.Projects;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Core.Projects;
using MeuEditorDeVideo.Desktop.Services.Help;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Presentation.Services;
using MeuEditorDeVideo.Presentation.Services.Help;
using MeuEditorDeVideo.Presentation.ViewModels;
using MeuEditorDeVideo.Presentation.ViewModels.Export;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Diálogos convertidos do WPF: cada janela abre sem tela, com ViewModel real e serviços falsos, e gera uma captura.</summary>
public partial class DialogsTests
{
    // ---------------------------------------------------------------- utilitários

    /// <summary>Raiz do repositório (onde fica a pasta documentation).</summary>
    private static string RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "documentation")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
        }
    }

    private static string Manual(string? language = null) =>
        Path.Combine(RepoRoot, "documentation", language is null ? "MANUAL_DO_USUARIO.md" : $"MANUAL_DO_USUARIO.{language}.md");

    private static T Find<T>(Visual root, Func<T, bool>? filter = null) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First(c => filter?.Invoke(c) ?? true);

    private static IEnumerable<T> All<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    private static Button ButtonNamed(Visual root, string content) => Find<Button>(root, b => b.Content as string == content);

    /// <summary>Mostra o diálogo como modal sobre uma janela hospedeira; o resultado fica na tarefa devolvida.</summary>
    private static (Task<bool?> Result, Window Host) ShowModal(Window dialog)
    {
        var host = new Window { Width = 1000, Height = 800 };
        host.Show();
        var task = dialog.ShowDialog<bool?>(host);
        Dispatcher.UIThread.RunJobs();
        return (task, host);
    }

    private static void Click(Button button)
    {
        // O Avalonia só executa o Command dentro do clique real do botão; aqui o clique é simulado, então os dois passos são feitos à mão.
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (button.Command is { } command && command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertPng(string path) => Assert.True(new FileInfo(path).Length > 2000, path);

    private static void Layout(Window window)
    {
        if (!window.IsVisible) window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    // ---------------------------------------------------------------- Sobre

    [Fact]
    public void Sobre_lista_componentes_e_fecha()
    {
        var path = Ui.Run(() =>
        {
            var window = new AboutWindow(new AboutViewModel());
            var (result, host) = ShowModal(window);
            Layout(window);
            // 10 componentes de terceiros; o ícone do LinkedIn e os links também entram na árvore.
            Assert.True(All<TextBlock>(window).Count(t => t.Text == "GPL-3.0-or-later") >= 1);
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Componentes de terceiros");
            Assert.Contains(All<TextBlock>(window), t => t.Text?.StartsWith("https://meueditordevideos") == true);
            var file = Ui.Capture(window, "dialogo-sobre");
            Click(ButtonNamed(window, "Fechar"));
            Assert.True(result.IsCompleted);
            host.Close();
            return file;
        });
        AssertPng(path);
    }

    // ---------------------------------------------------------------- Manual do usuário

    public static TheoryData<string?> Idiomas => new() { null, "en", "es", "fr", "de", "pt-PT" };

    [Theory]
    [MemberData(nameof(Idiomas))]
    public void Manual_renderiza_nos_seis_idiomas_sem_excecao(string? language)
    {
        var file = Manual(language);
        Assert.True(File.Exists(file), file);
        var content = Ui.Run(() =>
        {
            var blocks = MarkdownDocument.Parse(File.ReadAllText(file));
            var built = MarkdownControls.Build(blocks, Path.GetDirectoryName(file));
            var host = Ui.Host(new ScrollViewer { Content = built.Root }, 900, 700);
            Layout(host);
            host.Close();
            return built;
        });
        Assert.True(content.Topics.Count > 10, $"{language}: {content.Topics.Count} títulos");
        Assert.True(content.Texts.Count > 50);
    }

    [Fact]
    public void Manual_mostra_sumario_vai_ate_a_secao_e_procura_texto()
    {
        var path = Ui.Run(() =>
        {
            var window = new HelpWindow(Manual());
            Layout(window);
            var summary = Find<ListBox>(window, l => l.Name == "Sumario");
            Assert.True(window.Topics.Count > 10);
            var topic = window.Topics.First(t => t.Topic.Level == 2);
            window.GoTo(topic.Title);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(topic, summary.SelectedItem);

            // Busca: acha, move para a ocorrência seguinte e volta ao começo.
            Find<TextBox>(window, t => t.Name == "CampoBusca").Text = "projeto";
            Assert.True(window.Procurar(forward: true));
            var first = Find<TextBlock>(window, t => t.Name == "ResultadoBusca").Text;
            Assert.StartsWith("1/", first);
            window.Procurar(forward: true);
            Assert.StartsWith("2/", Find<TextBlock>(window, t => t.Name == "ResultadoBusca").Text);
            Find<TextBox>(window, t => t.Name == "CampoBusca").Text = "zzzxxyyqq";
            Assert.False(window.Procurar(forward: true));

            Find<TextBox>(window, t => t.Name == "CampoBusca").Text = "projeto";
            window.Procurar(forward: true);
            Find<Border>(window, b => b.Name == "BarraBusca").IsVisible = true;
            Layout(window);
            var file = Ui.Capture(window, "dialogo-manual");
            window.Close();
            return file;
        });
        AssertPng(path);
    }

    [Fact]
    public void Busca_do_manual_abre_com_o_modificador_principal() => Ui.Run(() =>
    {
        var window = new HelpWindow(Manual());
        window.Show();
        Layout(window);
        var barra = Find<Border>(window, b => b.Name == "BarraBusca");
        Assert.False(barra.IsVisible);
        window.KeyPress(Key.F, Ui.Principal, PhysicalKey.F, null);
        Assert.True(barra.IsVisible);
        window.Close();
    });

    [Fact]
    public void Manual_aparece_tambem_no_tema_claro()
    {
        var path = Ui.Run(() =>
        {
            var app = Avalonia.Application.Current!;
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            try
            {
                var window = new HelpWindow(Manual("en"));
                Layout(window);
                window.GoTo(window.Topics.First(t => t.Topic.Level == 3).Title);
                var file = Ui.Capture(window, "dialogo-manual-claro");
                window.Close();
                return file;
            }
            finally
            {
                app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            }
        });
        AssertPng(path);
    }

    [Fact]
    public void Manual_sem_arquivo_mostra_aviso()
    {
        Ui.Run(() =>
        {
            var window = new HelpWindow(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".md"));
            Layout(window);
            Assert.Contains(All<TextBlock>(window), t => t.Text?.StartsWith("O manual não foi encontrado") == true
                || (t is SelectableTextBlock s && s.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Any(r => r.Text?.StartsWith("O manual não foi encontrado") == true) == true));
            window.Close();
        });
    }

    // ---------------------------------------------------------------- Atalhos e informações do sistema

    [Fact]
    public void Documento_de_informacoes_mostra_texto_e_copia()
    {
        var path = Ui.Run(() =>
        {
            const string markdown = "# Informações do sistema\n\n## Programa\n\n- **Versão:** 1.3.0\n- Processador: `CPU de teste`\n\n| Item | Valor |\n|---|---|\n| Memória | 32 GB |\n| Vídeo | GPU de teste |\n\n> Nada é enviado.";
            var window = new InfoDocumentWindow("Informações do sistema", markdown, "texto para copiar");
            var (result, host) = ShowModal(window);
            Layout(window);
            Assert.True(ButtonNamed(window, "Copiar").IsVisible);
            Assert.Contains(All<SelectableTextBlock>(window), t => t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Any(r => r.Text == "CPU de teste") == true);
            var file = Ui.Capture(window, "dialogo-informacoes");
            Click(ButtonNamed(window, "Fechar"));
            Assert.True(result.IsCompleted);
            host.Close();
            return file;
        });
        AssertPng(path);
    }

    [Fact]
    public void Documento_sem_texto_para_copiar_esconde_o_botao()
    {
        Ui.Run(() =>
        {
            var window = new InfoDocumentWindow("Atalhos de teclado", "# Atalhos\n\n- Ctrl+S: Salvar");
            Layout(window);
            Assert.False(ButtonNamed(window, "Copiar").IsVisible);
            window.Close();
        });
    }

    // ---------------------------------------------------------------- Gerenciador e catálogo de modelos de IA

    private static ModelManifest Manifest(string id, string name, ModelCapability capability, bool gpu = true, int memory = 400) => new()
    {
        Id = id,
        Name = name,
        Version = "1.0",
        Source = "Projeto de exemplo",
        Capability = capability,
        Purpose = "Modelo de teste para a janela de gerenciamento.",
        Format = "ONNX",
        File = "modelo.onnx",
        SizeBytes = 26_000_000,
        MemoryMb = memory,
        Gpu = gpu,
        License = "Apache-2.0",
        Sha256 = new string('a', 64),
    };

    private static IModelRegistry Registry()
    {
        var models = new List<InstalledModel>
        {
            new(Manifest("modnet", "MODNet (segmentação de pessoas)", ModelCapability.PersonSegmentation), "C:\\Modelos\\modnet", BuiltIn: true),
            new(Manifest("whisper-small", "Whisper small (fala)", ModelCapability.SpeechRecognition, memory: 900), "C:\\Modelos\\whisper"),
            new(Manifest("silero", "Silero VAD", ModelCapability.VoiceActivity, gpu: false, memory: 50), "C:\\Modelos\\silero"),
        };
        return Stub.Of<IModelRegistry>(s => s
            .Returns(nameof(IModelRegistry.Installed), models)
            .On(nameof(IModelRegistry.Find), a => models.FirstOrDefault(m => m.Manifest.Capability == (ModelCapability)a[0]!))
            .On(nameof(IModelRegistry.VerifyAsync), _ => Task.FromResult(true)));
    }

    [Fact]
    public void Gerenciador_de_modelos_lista_instalados_recursos_e_dispositivo()
    {
        var path = Ui.Run(() =>
        {
            var settings = new DialogsFakes.MemorySettings();
            var vm = new ModelManagerViewModel(Registry(), settings, Stub.Of<IDialogService>());
            var window = new ModelManagerWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);

            Assert.Equal(3, vm.Models.Count);
            var list = Find<ListBox>(window, l => l.ItemsSource == vm.Models);
            Assert.Equal(3, list.ItemCount);
            Assert.Equal(Enum.GetValues<ModelCapability>().Length, vm.Capabilities.Count);
            // Sem ONNX Runtime (macOS Intel), o MODNet instalado aparece como indisponível; o Whisper, não.
            Assert.Contains(All<TextBlock>(window), t => t.Text?.StartsWith(AiPlatform.OnnxRuntimeAvailable ? "Disponível · MODNet" : "Indisponível em Macs com Intel") == true);
            Assert.Contains(All<TextBlock>(window), t => t.Text?.StartsWith("Disponível · Whisper") == true);

            // O nome da GPU acompanha o sistema (DirectML no Windows, Core ML no macOS, só CPU no Linux).
            var gpu = Find<RadioButton>(window, r => r.Name == "OpcaoGpu");
            Assert.Equal(AiPlatform.GpuLabel(), gpu.Content);
            Assert.Equal(AiPlatform.GpuAvailable, gpu.IsEnabled);

            // Os dados do manifesto do modelo escolhido aparecem nas caixas de leitura.
            Assert.Contains(All<TextBox>(window), t => t.Text == "modnet");
            Assert.Contains(All<TextBox>(window), t => t.Text == "Apache-2.0");
            var file = Ui.Capture(window, "dialogo-modelos-ia");

            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("whisper-small", vm.Selected!.Id);
            Assert.Contains(All<TextBox>(window), t => t.Text == "whisper-small");

            Click(ButtonNamed(window, "Fechar"));
            Assert.True(result.IsCompleted);
            host.Close();
            return file;
        });
        AssertPng(path);
    }

    [Fact]
    public void Gerenciador_de_modelos_abre_o_catalogo_de_downloads()
    {
        Ui.Run(() =>
        {
            var vm = new ModelManagerViewModel(Registry(), new DialogsFakes.MemorySettings(), Stub.Of<IDialogService>());
            var window = new ModelManagerWindow(vm);
            Layout(window);
            var opened = 0;
            Window? catalog = null;
            window.CatalogFactory = () =>
            {
                opened++;
                // Dentro do laço modal: confere que o catálogo é uma janela filha e fecha-o para o laço terminar.
                Dispatcher.UIThread.Post(() =>
                {
                    catalog = window.OwnedWindows.FirstOrDefault();
                    catalog?.Close();
                });
                return CatalogViewModel();
            };
            Click(ButtonNamed(window, "Baixar modelos…"));
            Assert.Equal(1, opened);
            Assert.IsType<ModelCatalogWindow>(catalog);
            window.Close();
        });
    }

    private static ModelCatalogViewModel CatalogViewModel()
    {
        var catalog = MeuEditorDeVideo.AIEngine.Models.ModelCatalog.Load(Path.Combine(RepoRoot, "resources", "ai-models"));
        var registry = Registry();
        var installer = new MeuEditorDeVideo.AIEngine.Models.CatalogModelInstaller(catalog, registry,
            Stub.Of<IModelDownloader>(), Path.Combine(Path.GetTempPath(), "med-teste-catalogo"));
        var services = Stub.Of<IServiceProvider>(s => s.On(nameof(IServiceProvider.GetService), a =>
            (Type)a[0]! == typeof(MeuEditorDeVideo.AIEngine.Models.CatalogModelInstaller) ? installer : null));
        return new ModelCatalogViewModel(services, registry, Stub.Of<IDialogService>());
    }

    [Fact]
    public void Catalogo_de_modelos_mostra_os_modelos_da_plataforma_e_o_progresso()
    {
        var path = Ui.Run(() =>
        {
            var vm = CatalogViewModel();
            Assert.NotEmpty(vm.Items);
            var window = new ModelCatalogWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);

            var list = Find<ListBox>(window, l => l.ItemsSource == vm.Items);
            Assert.Equal(vm.Items.Count, list.ItemCount);
            // O catálogo só lista o que existe no sistema atual (campo "plataformas").
            var tag = AiPlatform.Tag(AiPlatform.CurrentOs);
            Assert.All(vm.Items, i => Assert.True(i.Entry.AvailableOn(tag), i.Name));
            Assert.All(vm.Items, i => Assert.True(AiPlatform.Supports(i.Entry.Capability), i.Name));
            Assert.Contains(All<TextBlock>(window), t => t.Text == vm.Selected!.Name);
            Assert.Contains(All<TextBlock>(window), t => t.Text == vm.Selected!.Device);

            // Instalação em andamento: barra de progresso visível e botão Instalar desabilitado.
            var bar = Find<ProgressBar>(window);
            Assert.False(bar.IsVisible);
            vm.IsBusy = true;
            vm.Percent = 42;
            vm.Status = "1 de 2: " + vm.Items[0].Name + " — Baixando… 12,0 MB de 26,0 MB";
            vm.Items[0].Progress = "Baixando… 12,0 MB de 26,0 MB";
            Layout(window);
            Assert.True(bar.IsVisible);
            Assert.False(Find<Button>(window, b => b.Name == "BotaoInstalar").IsEnabled);
            var file = Ui.Capture(window, "dialogo-catalogo-modelos");

            vm.IsBusy = false;
            Layout(window);
            Assert.True(Find<Button>(window, b => b.Name == "BotaoInstalar").IsEnabled);
            Click(ButtonNamed(window, "Fechar"));
            Assert.True(result.IsCompleted);
            host.Close();
            return file;
        });
        AssertPng(path);
    }

    // ---------------------------------------------------------------- Novo projeto

    [Fact]
    public void Novo_projeto_agrupa_modelos_e_cria_com_true()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Novo projeto é teste " + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Ui.Run(() =>
            {
                var settings = new DialogsFakes.MemorySettings(new AppSettings { DefaultProjectsDirectory = folder });
                var vm = new NewProjectViewModel(Stub.Of<IDialogService>(), settings);
                var window = new NewProjectWindow(vm);
                var (result, host) = ShowModal(window);
                Layout(window);

                // Um cabeçalho (desabilitado) por grupo antes dos modelos.
                var combo = Find<ComboBox>(window, c => c.Name == "ModeloCombo");
                var items = combo.Items.OfType<ComboBoxItem>().ToList();
                var groups = vm.Templates.Select(t => t.Group).Distinct().Count();
                Assert.Equal(vm.Templates.Count + groups, items.Count);
                Assert.Equal(groups, items.Count(i => !i.IsEnabled));
                Assert.Same(vm.SelectedTemplate, ((ComboBoxItem)combo.SelectedItem!).Tag);

                // Trocar o modelo pela lista muda o ViewModel, e o contrário também.
                var other = vm.Templates.First(t => !ReferenceEquals(t, vm.SelectedTemplate) && !t.IsCustom);
                combo.SelectedItem = items.First(i => ReferenceEquals(i.Tag, other));
                Assert.Same(other, vm.SelectedTemplate);
                vm.SelectedTemplate = vm.Templates[0];
                Assert.Same(vm.Templates[0], ((ComboBoxItem)combo.SelectedItem!).Tag);

                Find<TextBox>(window, t => t.Name == "NameTextBox").Text = "Férias na praia";
                Assert.Equal("Férias na praia", vm.Name);
                Layout(window);
                var file = Ui.Capture(window, "dialogo-novo-projeto");

                Click(ButtonNamed(window, "Criar projeto"));
                Assert.True(result.IsCompleted);
                Assert.True(result.Result);
                Assert.Equal("Férias na praia", vm.Result!.Name);
                host.Close();
                return file;
            });
            AssertPng(path);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Novo_projeto_cancelar_fecha_com_false_e_mostra_erro_de_validacao()
    {
        Ui.Run(() =>
        {
            var vm = new NewProjectViewModel(Stub.Of<IDialogService>(), new DialogsFakes.MemorySettings());
            vm.Name = "Nome inválido: *";
            var window = new NewProjectWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);
            Click(ButtonNamed(window, "Criar projeto"));
            Layout(window);
            Assert.False(result.IsCompleted);
            Assert.Contains(All<TextBlock>(window), t => t.IsVisible && t.Text?.StartsWith("O nome não pode conter") == true);
            Click(ButtonNamed(window, "Cancelar"));
            Assert.True(result.IsCompleted);
            Assert.False(result.Result);
            host.Close();
        });
    }

    // ---------------------------------------------------------------- Mídias não encontradas

    [Fact]
    public void Midias_ausentes_lista_as_midias_e_continua_sem_elas()
    {
        var path = Ui.Run(() =>
        {
            var (project, asset) = DialogsFakes.SampleProject();
            var other = new MediaAsset
            {
                Id = Guid.NewGuid(), Kind = MediaKind.Audio, Name = "Floresta Amazônica.wav", OriginalPath = "D:\\Áudios\\Floresta Amazônica.wav",
            };
            project = new MeuEditorDeVideo.Domain.Editing.AddMediaAssetsCommand([other]).Apply(project);
            var media = new MediaResolution(new Dictionary<Guid, MediaLocation>
            {
                [asset.Id] = new(asset.Id, MediaStatus.Missing, null),
                [other.Id] = new(other.Id, MediaStatus.Missing, null),
            });
            var vm = new MissingMediaViewModel(DialogsFakes.ProjectsWith(project, media), Stub.Of<IDialogService>());
            var window = new MissingMediaWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);

            Assert.Equal(2, vm.Items.Count);
            Assert.Equal(2, All<Button>(window).Count(b => b.Content as string == "Localizar…"));
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Praia.mp4");
            var file = Ui.Capture(window, "dialogo-midias-ausentes");

            Click(ButtonNamed(window, "Continuar sem as mídias"));
            Assert.True(result.IsCompleted);
            host.Close();
            return file;
        });
        AssertPng(path);
    }

    [Fact]
    public void Midias_ausentes_fecha_sozinha_quando_nao_ha_nada_a_localizar()
    {
        Ui.Run(() =>
        {
            var (project, _) = DialogsFakes.SampleProject();
            var vm = new MissingMediaViewModel(DialogsFakes.ProjectsWith(project), Stub.Of<IDialogService>());
            var window = new MissingMediaWindow(vm);
            var (result, host) = ShowModal(window);
            Dispatcher.UIThread.RunJobs();
            Assert.True(result.IsCompleted);
            host.Close();
        });
    }

    // ---------------------------------------------------------------- Fila de renderização

    private static RenderJob Job(string name, RenderJobStatus status, string template = "YouTube 1080p") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        OutputPath = "D:\\Exportações\\" + name + ".mp4",
        Settings = new ExportSettings(),
        TemplateName = template,
        Status = status,
        Files = status == RenderJobStatus.Completed ? ["D:\\Exportações\\" + name + ".mp4"] : [],
        CreatedAt = DateTimeOffset.Now,
    };

    [Fact]
    public void Fila_de_renderizacao_lista_trabalhos_e_remove()
    {
        var path = Ui.Run(() =>
        {
            var jobs = new List<RenderJob>
            {
                Job("Praia", RenderJobStatus.Completed),
                Job("Floresta Amazônica", RenderJobStatus.Running, "Instagram Reels"),
                Job("Cena extra", RenderJobStatus.Failed),
            };
            RenderQueueViewModel Create() => new(Stub.Of<IRenderQueueService>(s => s
                .Returns(nameof(IRenderQueueService.Jobs), jobs)
                .On(nameof(IRenderQueueService.Remove), a => { jobs.RemoveAll(j => j.Id == (Guid)a[0]!); return null; })));

            var vm = Create();
            var window = new RenderQueueWindow(vm) { Title = "Exportando…" };
            Assert.Equal("Exportando…", window.Title);
            window.Show();
            Layout(window);

            var list = Find<ListBox>(window, l => l.Name == "Lista");
            Assert.Equal(3, list.ItemCount);
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Floresta Amazônica");
            // Em execução: Cancelar visível; concluído: Abrir e Mostrar na pasta; falho: Retomar.
            Assert.Equal(1, All<Button>(window).Count(b => b.Content as string == "Cancelar" && b.IsVisible));
            Assert.Equal(1, All<Button>(window).Count(b => b.Content as string == "Abrir" && b.IsVisible));
            Assert.Equal(1, All<Button>(window).Count(b => b.Content as string == "Retomar" && b.IsVisible));
            var file = Ui.Capture(window, "dialogo-fila-renderizacao");

            // O botão Remover de uma linha chama o comando do ViewModel da janela.
            var remove = All<Button>(window).First(b => b.Content as string == "Remover" && b.IsVisible);
            Assert.True(remove.Command!.CanExecute(remove.CommandParameter));
            remove.Command.Execute(remove.CommandParameter);
            Assert.Equal(2, jobs.Count);
            window.Close();
            return file;
        });
        AssertPng(path);
    }

    [Fact]
    public void Fila_vazia_mostra_a_mensagem()
    {
        Ui.Run(() =>
        {
            var window = new RenderQueueWindow(new RenderQueueViewModel(Stub.Of<IRenderQueueService>()));
            Layout(window);
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Nenhuma exportação na fila." && t.IsVisible);
            window.Close();
        });
    }

    // ---------------------------------------------------------------- Exportar

    private static IExportTemplateStore Templates() => Stub.Of<IExportTemplateStore>(s => s.On(nameof(IExportTemplateStore.GetAll), _ =>
        new List<ExportTemplate>
        {
            new() { Id = "youtube", Name = "YouTube 1080p", Description = "H.264 + AAC, 1920 × 1080", IsBuiltIn = true,
                Settings = new ExportSettings { Width = 1920, Height = 1080 } },
            new() { Id = "web", Name = "Web leve", Description = "H.264, baixa taxa de bits", IsBuiltIn = true },
            new() { Id = "meu", Name = "Meu modelo da praia", Description = "Personalizado", Settings = new ExportSettings() },
        }));

    private static ExportViewModel ExportVm(string? directory = null)
    {
        var (project, _) = DialogsFakes.SampleProject();
        var settings = new DialogsFakes.MemorySettings(new AppSettings { DefaultExportDirectory = directory ?? Path.GetTempPath() });
        var engine = Stub.Of<IRenderEngine>(s => s.On(nameof(IRenderEngine.AvailableEncoders),
            a => new List<EncoderInfo> { new("libx264", "x264 (CPU)", false) }));
        return new ExportViewModel(Templates(), Stub.Of<IRenderQueueService>(), engine, Stub.Of<IDialogService>(), settings,
            DialogsFakes.ProjectsWith(project), MediaTime.Zero);
    }

    [Fact]
    public void Exportar_mostra_modelos_parametros_e_destino()
    {
        var path = Ui.Run(() =>
        {
            var vm = ExportVm();
            var window = new ExportWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);

            Assert.Equal(3, vm.Templates.Count);
            var list = Find<ListBox>(window, l => l.ItemsSource == vm.Templates);
            Assert.Equal(3, list.ItemCount);
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Meu modelo da praia");
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Vídeo" && t.Classes.Contains("section"));
            Assert.Contains(All<TextBox>(window), t => t.Text == "Férias na praia");
            var file = Ui.Capture(window, "dialogo-exportar");

            // O campo de resolução só habilita quando se desmarca "Usar a resolução do projeto".
            var width = All<TextBox>(window).First(t => t.Text == vm.WidthText && t.Parent is StackPanel);
            vm.UseProjectResolution = true;
            Layout(window);
            Assert.False(width.IsEffectivelyEnabled);
            vm.UseProjectResolution = false;
            Layout(window);
            Assert.True(width.IsEffectivelyEnabled);

            Click(ButtonNamed(window, "Cancelar"));
            Assert.True(result.IsCompleted);
            Assert.False(result.Result);
            host.Close();
            return file;
        });
        AssertPng(path);
    }

    // ---------------------------------------------------------------- Configurações

    private static SettingsViewModel SettingsVm(DialogsFakes.MemorySettings? settings = null)
    {
        var hardware = Stub.Of<IHardwareInfoService>(s => s.On(nameof(IHardwareInfoService.Get), _ => new HardwareInfo
        {
            CpuName = "CPU de teste",
            LogicalProcessors = 16,
            TotalMemoryBytes = 32L << 30,
            AvailableMemoryBytes = 20L << 30,
            Gpus = [new GpuInfo("GPU de teste", 8L << 30, true)],
            Disks = [new DiskInfo("Cache", "C:\\", 100L << 30, 1000L << 30)],
            HardwareEncoders = ["NVIDIA NVENC (GPU): H.264"],
            HardwareDecoding = true,
        }));
        var cache = Stub.Of<ICacheMaintenance>(s => s.On(nameof(ICacheMaintenance.Measure),
            _ => new List<CacheUsage> { new("Miniaturas, formas de onda e proxies", "C:\\cache\\media", 3L << 30, 120) }));
        var paths = Stub.Of<IAppPaths>(s => s
            .Returns(nameof(IAppPaths.Cache), "C:\\l\\cache")
            .Returns(nameof(IAppPaths.AiModels), "C:\\l\\ai-models"));
        return new SettingsViewModel(settings ?? new DialogsFakes.MemorySettings(new AppSettings { UserName = "Diego Mendes Rodrigues" }),
            Stub.Of<IDialogService>(), Stub.Of<IThemeService>(), hardware, cache, paths);
    }

    [Fact]
    public void Configuracoes_mostra_as_quatro_abas_e_os_atalhos_por_categoria()
    {
        var paths = Ui.Run(() =>
        {
            var vm = SettingsVm();
            var window = new SettingsWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);
            var tabs = Find<TabControl>(window, t => t.Name == "Abas");
            Assert.Equal(new object?[] { "Geral", "Desempenho", "Cache", "Atalhos" }, tabs.Items.OfType<TabItem>().Select(i => i.Header).ToArray());
            Assert.Contains(All<TextBox>(window), t => t.Text == "Diego Mendes Rodrigues");

            var files = new List<string> { Ui.Capture(window, "dialogo-configuracoes-geral") };
            tabs.SelectedIndex = 1;
            Layout(window);
            Dispatcher.UIThread.RunJobs();
            // O hardware é detectado de forma assíncrona na abertura; as linhas aparecem quando termina.
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Este computador");
            files.Add(Ui.Capture(window, "dialogo-configuracoes-desempenho"));
            tabs.SelectedIndex = 2;
            Layout(window);
            files.Add(Ui.Capture(window, "dialogo-configuracoes-cache"));
            tabs.SelectedIndex = 3;
            Layout(window);
            var groups = vm.Shortcuts.Select(s => s.Category).Distinct().Count();
            var list = Find<ItemsControl>(window, l => l.Name == "ListaAtalhos");
            Assert.Equal(groups, list.ItemCount);
            Assert.Contains(All<TextBox>(window), t => t.Text == vm.Shortcuts[0].Text);
            files.Add(Ui.Capture(window, "dialogo-configuracoes-atalhos"));

            Click(ButtonNamed(window, "Cancelar"));
            Assert.True(result.IsCompleted);
            Assert.False(result.Result);
            host.Close();
            return files;
        });
        paths.ForEach(AssertPng);
    }

    [Fact]
    public void Configuracoes_capturam_a_tecla_do_atalho_e_salvam_com_true()
    {
        Ui.Run(() =>
        {
            var settings = new DialogsFakes.MemorySettings(new AppSettings());
            var vm = SettingsVm(settings);
            var window = new SettingsWindow(vm);
            var (result, host) = ShowModal(window);
            Layout(window);
            Find<TabControl>(window, t => t.Name == "Abas").SelectedIndex = 3;
            Layout(window);

            var item = vm.Shortcuts[0];
            var field = All<TextBox>(window).First(t => t.DataContext == item);
            field.Focus();
            // Ctrl+Alt+F9 não está em uso no catálogo de atalhos.
            window.KeyPress(Key.F9, Ui.Principal | RawInputModifiers.Alt, PhysicalKey.F9, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(item.IsCustom, "o atalho deveria ter sido redefinido");
            Assert.Contains("F9", item.Text);

            item.ResetCommand.Execute(null);
            Assert.False(item.IsCustom);

            Click(ButtonNamed(window, "Salvar"));
            Assert.True(result.IsCompleted);
            Assert.True(result.Result);
            host.Close();
        });
    }

    // ---------------------------------------------------------------- Editor de legendas

    private static (SubtitleEditorViewModel Vm, IProjectService Projects) SubtitleVm()
    {
        var (project, _) = DialogsFakes.SampleProject();
        var seq = project.MainSequence;
        project = new MeuEditorDeVideo.Domain.Editing.AddTrackCommand(seq.Id, TrackKind.Subtitle, "Legendas").Apply(project);
        string[] lines = ["Bem-vindos à praia!", "O mar está calmo hoje.", "Vamos explorar a Floresta Amazônica."];
        for (var i = 0; i < lines.Length; i++)
        {
            var clip = new Clip
            {
                Id = Guid.NewGuid(),
                Start = MediaTime.FromSecondsApprox(i * 3),
                Duration = MediaTime.FromSecondsApprox(2.5),
                Text = lines[i],
                TextFormat = new TextFormat { Kind = TextKind.Subtitle },
            };
            project = new MeuEditorDeVideo.Domain.Editing.AddTextClipCommand(seq.Id, clip, TrackKind.Subtitle).Apply(project);
        }
        var projects = DialogsFakes.ProjectsWith(project);
        var dialogs = Stub.Of<IDialogService>();
        var library = new MeuEditorDeVideo.Presentation.ViewModels.Media.MediaLibraryViewModel(projects,
            Stub.Of<MeuEditorDeVideo.Application.Media.IMediaImportService>(), Stub.Of<IThumbnailProvider>(),
            Stub.Of<IWaveformProvider>(), dialogs, Stub.Of<IProxyService>(), new DialogsFakes.MemorySettings(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MeuEditorDeVideo.Presentation.ViewModels.Media.MediaLibraryViewModel>.Instance);
        var timeline = new TimelineViewModel(projects, dialogs, library);
        return (new SubtitleEditorViewModel(timeline, new TextLibraryViewModel(timeline, dialogs), dialogs), projects);
    }

    [Fact]
    public void Editor_de_legendas_lista_as_legendas_e_aceita_atalhos_de_desfazer()
    {
        var path = Ui.Run(() =>
        {
            var (vm, _) = SubtitleVm();
            var undone = 0;
            var undo = new CommunityToolkit.Mvvm.Input.RelayCommand(() => undone++);
            var redo = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            var window = new SubtitleEditorWindow(vm, undo, redo);
            window.Show();
            Layout(window);

            Assert.Equal(3, vm.Rows.Count);
            var list = Find<ListBox>(window, l => l.ItemsSource == vm.Rows);
            Assert.Equal(3, list.ItemCount);
            Assert.Contains(All<TextBox>(window), t => t.Text == "O mar está calmo hoje.");
            Assert.Contains(All<TextBlock>(window), t => t.Text == "Legendas"); // nome da trilha na caixa de trilhas
            Assert.Equal(6, All<TextBox>(window).Count(t => t.Classes.Contains("time")));
            var file = Ui.Capture(window, "dialogo-editor-legendas");

            window.KeyPress(Key.Z, Ui.Principal, PhysicalKey.Z, "z");
            Assert.Equal(1, undone);
            window.Close();
            return file;
        });
        AssertPng(path);
    }
}
