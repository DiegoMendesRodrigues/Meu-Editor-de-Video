// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Core.Abstractions;
using MeuEditorDeVideo.Core.Updates;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.DesktopTests.Support;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Janelas montadas só em código (Prompt, Choice, Texto para fala, Reenquadrar, Pacotes, Gravar tela, Atualização, Boas-vindas).</summary>
public class CodeWindowsTests
{
    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => Avalonia.Automation.AutomationProperties.GetName(c) == name);

    private static Button Btn(Control root, string text) =>
        root.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump(Func<bool> done, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.True(done(), "condição não atendida a tempo");
    }

    private static void AssertPng(string path) => Assert.True(new FileInfo(path).Length > 1000, path);

    [Fact]
    public void Prompt_devolve_o_texto_digitado_ao_confirmar()
    {
        Ui.Run(() =>
        {
            var w = new PromptWindow("Renomear", "Novo nome do clipe", "Praia.mp4");
            w.Show();
            AssertPng(Ui.Capture(w, "prompt"));
            Assert.Equal("Praia.mp4", w.Value);
            Find<TextBox>(w, "Novo nome do clipe").Text = "Floresta Amazônica.mp4";
            Click(Btn(w, "OK"));
            Assert.False(w.IsVisible);
            Assert.Equal("Floresta Amazônica.mp4", w.Value);
        });
    }

    [Fact]
    public void Prompt_cancelado_fecha_sem_confirmar()
    {
        Ui.Run(() =>
        {
            var w = new PromptWindow("Renomear", "Nome", "Praia");
            var closed = new List<object?>();
            w.Show();
            w.Closing += (_, _) => closed.Add(true);
            Click(Btn(w, "Cancelar"));
            Assert.False(w.IsVisible);
            Assert.Single(closed);
        });
    }

    [Fact]
    public void Choice_devolve_o_indice_escolhido()
    {
        Ui.Run(() =>
        {
            var w = new ChoiceWindow("Visualizador de áudio", "Música a visualizar",
                ["Trilha de Praia.mp3", "Ambiente da Floresta Amazônica.wav", "Narração.m4a"], 1, "Inserir");
            w.Show();
            Assert.Equal(1, w.SelectedIndex);
            AssertPng(Ui.Capture(w, "choice"));
            Find<ComboBox>(w, "Música a visualizar").SelectedIndex = 2;
            Click(Btn(w, "Inserir"));
            Assert.False(w.IsVisible);
            Assert.Equal(2, w.SelectedIndex);
        });
    }

    [Fact]
    public void Texto_para_fala_devolve_texto_voz_e_velocidade()
    {
        Ui.Run(() =>
        {
            var w = new TextToSpeechWindow(["Microsoft David - English (United States)", "Microsoft Maria - Portuguese (Brazil)"]);
            w.Show();
            // A voz em português é a preferida.
            Assert.Equal("Microsoft Maria - Portuguese (Brazil)", w.Voice);
            // Sem texto, o botão não fecha.
            Click(Btn(w, "Gerar e inserir"));
            Assert.True(w.IsVisible);
            Find<TextBox>(w, "Texto a ser falado").Text = "  Bem-vindo à Praia e à Floresta Amazônica.  ";
            Find<Slider>(w, "Velocidade da fala").Value = 3;
            AssertPng(Ui.Capture(w, "texto-para-fala"));
            Click(Btn(w, "Gerar e inserir"));
            Assert.False(w.IsVisible);
            Assert.Equal("Bem-vindo à Praia e à Floresta Amazônica.", w.Text);
            Assert.Equal(3, w.Rate);
        });
    }

    [Fact]
    public void Texto_para_fala_sem_vozes_avisa()
    {
        Ui.Run(() =>
        {
            var w = new TextToSpeechWindow([]);
            AssertPng(Ui.Capture(w, "texto-para-fala-sem-vozes"));
            Assert.Null(w.Voice);
            w.Close();
        });
    }

    [Theory]
    [InlineData(0, 9, 16)]
    [InlineData(1, 1, 1)]
    [InlineData(3, 16, 9)]
    public void Reenquadrar_devolve_o_formato_escolhido(int index, int width, int height)
    {
        Ui.Run(() =>
        {
            var w = new ReframeWindow("1920 × 1080", true);
            w.Show();
            Assert.Equal((9, 16), w.Format);
            Assert.True(w.FollowHead);
            Find<ComboBox>(w, "Novo formato").SelectedIndex = index;
            if (index == 0) AssertPng(Ui.Capture(w, "reenquadrar"));
            Click(Btn(w, "Reenquadrar"));
            Assert.False(w.IsVisible);
            Assert.Equal((width, height), w.Format);
        });
    }

    [Fact]
    public void Reenquadrar_sem_modelo_nao_acompanha()
    {
        Ui.Run(() =>
        {
            var w = new ReframeWindow("1920 × 1080", false);
            AssertPng(Ui.Capture(w, "reenquadrar-sem-modelo"));
            Assert.Null(w.FollowHead);
            Assert.False(Find<ComboBox>(w, "Acompanhar").IsEnabled);
            w.Close();
        });
    }

    [Fact]
    public void Pacotes_instala_e_remove_e_avisa_que_mudou()
    {
        Ui.Run(() =>
        {
            var store = new FakeContentPackStore();
            var dialogs = new FakeDialogService { PickedFile = "C:\\Pacotes\\novo.medpack" };
            var w = new ContentPacksWindow(store, dialogs);
            w.Show();
            AssertPng(Ui.Capture(w, "pacotes"));
            var list = Find<ListBox>(w, "Pacotes instalados");
            Assert.Equal(2, list.ItemCount);
            Assert.False(Btn(w, "Exportar…").IsEnabled);
            Assert.False(w.Changed);

            // O pacote incluído no programa não pode ser removido.
            list.SelectedIndex = 0;
            Assert.True(Btn(w, "Exportar…").IsEnabled);
            Assert.False(Btn(w, "Remover").IsEnabled);

            Click(Btn(w, "Instalar pacote…"));
            Assert.True(w.Changed);
            Assert.Equal(3, list.ItemCount);
            Assert.Contains(dialogs.Infos, i => i.StartsWith("Pacote instalado"));
            AssertPng(Ui.Capture(w, "pacotes-instalado"));

            list.SelectedIndex = 1;
            Click(Btn(w, "Remover"));
            Assert.Equal(2, list.ItemCount);
            w.Close();
        });
    }

    [Fact]
    public void Boas_vindas_nao_fecha_sem_salvar_e_devolve_as_preferencias()
    {
        Ui.Run(() =>
        {
            var w = new WelcomeWindow(new UserPreferences(null, null, null, null, null));
            w.Show();
            AssertPng(Ui.Capture(w, "boas-vindas"));
            var save = Btn(w, "Salvar as preferências");
            Assert.False(save.IsEnabled); // nome vazio

            w.Close();
            Assert.True(w.IsVisible); // Alt+F4/X não fecha sem salvar

            Find<TextBox>(w, "Nome Completo").Text = "José Antônio da Conceição";
            Find<ComboBox>(w, "Idioma do sistema").SelectedIndex = 3; // Inglês
            Find<ComboBox>(w, "Tema").SelectedIndex = 1; // Escuro
            Find<ComboBox>(w, "Verificar mensalmente pelas novas atualizações").SelectedIndex = 1; // Não
            Assert.True(save.IsEnabled);
            AssertPng(Ui.Capture(w, "boas-vindas-preenchida"));
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(w.IsVisible);

            Click(save);
            Assert.False(w.IsVisible);
            Assert.Equal("José Antônio da Conceição", w.Result.Name);
            Assert.Equal("en", w.Result.Language);
            Assert.Equal("Dark", w.Result.ThemeColor);
            Assert.False(w.Result.CheckForNewVersions);
            Assert.NotNull(w.Result.LastCheck);
        });
    }

    [Fact]
    public void Boas_vindas_parte_das_preferencias_do_registro()
    {
        Ui.Run(() =>
        {
            var w = new WelcomeWindow(new UserPreferences("Maria", "pt-PT", "System", true, null));
            w.Show();
            var save = Btn(w, "Salvar as preferências");
            Assert.True(save.IsEnabled);
            Click(save);
            Assert.Equal(("Maria", "pt-PT", "System", true), (w.Result.Name, w.Result.Language, w.Result.ThemeColor, w.Result.CheckForNewVersions));
        });
    }

    [Fact]
    public void Atualizacao_mostra_versao_nova_com_links_e_versao_atual()
    {
        Ui.Run(() =>
        {
            var info = new UpdateInfo("1.4.0", "Alemão, Espanhol, Francês, Inglês, Português", "Windows, Linux e macOS", "Diego Mendes Rodrigues",
                "https://apps.microsoft.com/detail/exemplo", "https://aplicativosia.online/instalar");
            var w = UpdateWindow.CreateNewVersion(info);
            w.Show();
            AssertPng(Ui.Capture(w, "atualizacao-nova"));
            var links = w.GetVisualDescendants().OfType<HyperlinkButton>().ToList();
            // O link da Microsoft Store aparece só no Windows; o do site, sempre por último.
            Assert.Equal(UpdateEndpoints.ShowsMicrosoftStoreLink ? 2 : 1, links.Count);
            Assert.Equal("https://aplicativosia.online/instalar", links[^1].Content);
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Português") == true || t.Inlines?.Any(i => i is Avalonia.Controls.Documents.Run r && r.Text?.Contains("Português") == true) == true);
            Click(Btn(w, "Fechar essa janela"));
            Assert.False(w.IsVisible);

            var ok = UpdateWindow.CreateUpToDate();
            AssertPng(Ui.Capture(ok, "atualizacao-atual"));
            ok.Close();
        });
    }

    [Fact]
    public void Gravar_tela_lista_dispositivos_e_grava_com_contagem_e_F10()
    {
        Ui.Run(() =>
        {
            var recorder = new FakeScreenRecorder();
            var w = new ScreenRecordWindow(recorder, "C:\\Vídeos\\Gravação.mp4") { CountdownSeconds = 1 };
            w.Show();
            var record = Btn(w, "Gravar");
            Pump(() => record.IsEnabled);
            AssertPng(Ui.Capture(w, "gravar-tela"));
            Assert.Equal(["(nenhum)", "Câmera integrada HD"], Find<ComboBox>(w, "Webcam").Items.Cast<string>());
            Assert.Equal(1, Find<ComboBox>(w, "Microfone").SelectedIndex);
            Assert.Equal(3, Find<ComboBox>(w, "Tela a gravar").ItemCount); // todas + 2 telas

            Find<ComboBox>(w, "O que gravar").SelectedIndex = 1;
            Assert.True(Find<ComboBox>(w, "Janela a gravar").IsVisible);
            Assert.Equal(2, Find<ComboBox>(w, "Janela a gravar").ItemCount);
            AssertPng(Ui.Capture(w, "gravar-tela-janela"));
            Find<ComboBox>(w, "O que gravar").SelectedIndex = 0;

            Click(record);
            Pump(() => recorder.Started is not null, 8000);
            Assert.True(recorder.Started!.Screen);
            Assert.Equal("Microfone (Realtek)", recorder.Started.Microphone);
            Assert.Equal("C:\\Vídeos\\Gravação.mp4", recorder.Started.OutputPath);
            Pump(() => (string?)record.Content == "Parar");

            // Fechar durante a gravação é recusado.
            w.Close();
            Assert.True(w.IsVisible);

            w.KeyPress(Key.F10, RawInputModifiers.None, PhysicalKey.F10, null);
            Pump(() => !w.IsVisible);
            Assert.Equal("C:\\Vídeos\\Gravação.mp4", w.RecordedPath);
            Assert.True(w.InsertOnTimeline);
        });
    }

    [Fact]
    public void Gravar_tela_sem_captura_de_tela_mostra_o_motivo_e_deixa_so_webcam()
    {
        Ui.Run(() =>
        {
            var recorder = new FakeScreenRecorder { ScreenCaptureUnavailableReason = "A gravação de tela não funciona em sessões Wayland; use uma sessão X11." };
            var w = new ScreenRecordWindow(recorder, "C:\\Vídeos\\Gravação.mp4") { CountdownSeconds = 0 };
            w.Show();
            Pump(() => Btn(w, "Gravar").IsEnabled);
            var screen = w.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content as string == "Gravar a tela");
            Assert.False(screen.IsEnabled);
            Assert.False(screen.IsChecked);
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Wayland") == true);
            AssertPng(Ui.Capture(w, "gravar-tela-wayland"));

            // Só a tela e nenhuma webcam: nada para gravar.
            Click(Btn(w, "Gravar"));
            Pump(() => Btn(w, "Gravar").IsEnabled);
            Assert.Null(recorder.Started);
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Escolha gravar a tela, a webcam ou as duas.");
            w.Close();
        });
    }
}
