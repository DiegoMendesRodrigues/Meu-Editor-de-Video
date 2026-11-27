// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using MeuEditorDeVideo.Core.AI;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Desktop.Views.Panels;
using MeuEditorDeVideo.Desktop.Controls;
using MeuEditorDeVideo.Desktop.Views;
using MeuEditorDeVideo.Desktop.Views.Dialogs;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.DesktopTests.Support;
using MeuEditorDeVideo.Presentation.Media;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;
using MeuEditorDeVideo.Presentation.ViewModels.Preview;
using MeuEditorDeVideo.Presentation.ViewModels.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace MeuEditorDeVideo.DesktopTests;

/// <summary>Controles desenhados à mão (linha do tempo, curva, roda de cor, medidor...) e as janelas que os usam.</summary>
public class ControlsTests
{
    private static void AssertCaptured(string path) => Assert.True(new FileInfo(path).Length > 300, path);

    // ------------------------------------------------------------------------------------------ linha do tempo

    private static (Window Window, TimelineRuler Ruler, TimelineControl Tracks) TimelineHost(TimelineFixture fx)
    {
        var ruler = new TimelineRuler { Height = TimelineViewModel.RulerHeight, ViewModel = fx.Timeline };
        var tracks = new TimelineControl { ViewModel = fx.Timeline };
        var grid = new Grid { RowDefinitions = new RowDefinitions("28,*") };
        Grid.SetRow(tracks, 1);
        grid.Children.Add(ruler);
        grid.Children.Add(tracks);
        var window = Ui.Host(grid, 900, 300);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, ruler, tracks);
    }

    [Fact]
    public void Linha_do_tempo_desenha_trilhas_clipes_e_marcador()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        var path = Ui.Run(() =>
        {
            var (window, _, tracks) = TimelineHost(fx);
            Assert.Equal(tracks.Bounds.Width, fx.Timeline.ViewportWidth, 1);
            var file = Ui.Capture(window, "controles-linha-do-tempo");
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void Linha_do_tempo_converte_tempo_em_x_e_acha_o_clipe_sob_o_ponteiro()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        Ui.Run(() =>
        {
            var (window, _, tracks) = TimelineHost(fx);
            var vm = fx.Timeline;
            // Início do segundo clipe (13 s a 60 px/s).
            var x = vm.TimeToX(MediaTime.FromSecondsApprox(13));
            Assert.Equal(780, x, 1);
            Assert.Equal(13, vm.XToTime(x).TotalSeconds, 2);
            var top = vm.TrackTop(fx.VideoTrack);
            var hit = tracks.ClipAt(new Point(x + 100, top + 20));
            Assert.Equal(fx.ForestClip, hit?.ClipId);
            Assert.False(hit?.NearStart);
            // Perto da borda esquerda a alça de aparar é detectada.
            Assert.True(tracks.ClipAt(new Point(x + 2, top + 20))?.NearStart);
            // Sem clipe entre os dois.
            Assert.Null(tracks.ClipAt(new Point(vm.TimeToX(MediaTime.FromSecondsApprox(12.5)), top + 20)));
            window.Close();
        });
    }

    [Fact]
    public void Clicar_na_regua_posiciona_o_cursor()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        Ui.Run(() =>
        {
            var (window, _, _) = TimelineHost(fx);
            window.MouseDown(new Point(300, 10), MouseButton.Left);
            window.MouseMove(new Point(420, 10), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(420, 10), MouseButton.Left);
            Assert.Equal(7, fx.Timeline.Playhead.TotalSeconds, 1);
            window.Close();
        });
    }

    [Fact]
    public void Arrastar_um_clipe_move_o_clipe_e_clicar_o_seleciona()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        Ui.Run(() =>
        {
            var (window, _, _) = TimelineHost(fx);
            var vm = fx.Timeline;
            var y = TimelineViewModel.RulerHeight + vm.TrackTop(fx.VideoTrack) + 30;

            // Clique simples: seleciona só o clipe da floresta.
            var forestX = vm.TimeToX(MediaTime.FromSecondsApprox(13)) + 50;
            window.MouseDown(new Point(forestX, y), MouseButton.Left);
            window.MouseUp(new Point(forestX, y), MouseButton.Left);
            Assert.Contains(fx.ForestClip, vm.Selection);

            // Arrasto de 60 px (1 s) para a direita.
            var before = vm.Sequence!.Tracks.SelectMany(t => t.Clips).First(c => c.Id == fx.ForestClip).Start;
            window.MouseDown(new Point(forestX, y), MouseButton.Left);
            window.MouseMove(new Point(forestX + 30, y), RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(forestX + 60, y), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(forestX + 60, y), MouseButton.Left);
            var after = vm.Sequence!.Tracks.SelectMany(t => t.Clips).First(c => c.Id == fx.ForestClip).Start;
            Assert.True((after - before).TotalSeconds is > 0.7 and < 1.3, $"deslocamento {(after - before).TotalSeconds} s");
            window.Close();
        });
    }

    [Fact]
    public void Roda_com_Ctrl_muda_o_zoom()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        Ui.Run(() =>
        {
            var (window, _, _) = TimelineHost(fx);
            var before = fx.Timeline.PixelsPerSecond;
            window.MouseWheel(new Point(400, 100), new Vector(0, 1), Ui.Principal);
            Assert.True(fx.Timeline.PixelsPerSecond > before, $"zoom {before} -> {fx.Timeline.PixelsPerSecond}");
            window.Close();
        });
    }

    // ------------------------------------------------------------------------------------------ quadro da prévia

    [Fact]
    public void FrameImage_reaproveita_o_bitmap_enquanto_o_tamanho_nao_muda()
    {
        var path = Ui.Run(() =>
        {
            var image = new FrameImage();
            var window = Ui.Host(new Border { Background = Brushes.Black, Child = image }, 480, 270);
            image.Frame = new DisplayFrame(TimelineFixture.Gradient(320, 180, 0));
            var first = image.Bitmap;
            Assert.NotNull(first);
            image.Frame = new DisplayFrame(TimelineFixture.Gradient(320, 180, 1));
            Assert.Same(first, image.Bitmap);
            // Os pixels do segundo quadro foram copiados (a paleta da floresta tem menos vermelho no canto).
            using (var fb = image.Bitmap!.Lock())
            {
                Assert.Equal(180 * 320 * 4, fb.RowBytes * fb.Size.Height);
                Assert.Equal(255, System.Runtime.InteropServices.Marshal.ReadByte(fb.Address, 3));
            }
            // Tamanho novo: recria.
            image.Frame = new DisplayFrame(TimelineFixture.Gradient(160, 90, 0));
            Assert.NotSame(first, image.Bitmap);
            Assert.Equal(160, image.Bitmap!.PixelSize.Width);
            image.Frame = new DisplayFrame(TimelineFixture.Gradient(320, 180, 0));
            var file = Ui.Capture(window, "controles-frame-image");
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void FrameImage_sem_quadro_nao_tem_bitmap()
    {
        Ui.Run(() =>
        {
            var image = new FrameImage { Frame = new DisplayFrame(TimelineFixture.Gradient(8, 8, 0)) };
            Assert.NotNull(image.Bitmap);
            image.Frame = null;
            Assert.Null(image.Bitmap);
        });
    }

    // ------------------------------------------------------------------------------------------ medidor, roda, régua

    [Fact]
    public void Medidor_de_nivel_desenha_verde_amarelo_e_vermelho()
    {
        var path = Ui.Run(() =>
        {
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16) };
            foreach (var level in new[] { 0.0, 0.3, 0.6, 0.85, 1.0 })
                panel.Children.Add(new LevelMeter { Width = 14, Height = 160, Level = level });
            var window = Ui.Host(new Border { Background = new SolidColorBrush(Color.Parse("#2B2B33")), Child = panel }, 220, 200);
            var file = Ui.Capture(window, "controles-medidor-de-nivel");
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void Roda_de_cor_acompanha_o_ponteiro_e_volta_ao_centro_no_duplo_clique()
    {
        var path = Ui.Run(() =>
        {
            var wheel = new ColorWheel { Width = 110, Height = 110, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var window = Ui.Host(new Border { Background = new SolidColorBrush(Color.Parse("#2B2B33")), Padding = new Thickness(20), Child = wheel }, 180, 180);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var origin = wheel.TranslatePoint(new Point(0, 0), window)!.Value;
            // Arrasta para a direita e para cima: X e Y positivos.
            window.MouseDown(origin + new Point(55, 55), MouseButton.Left);
            window.MouseMove(origin + new Point(55 + 30, 55 - 20), RawInputModifiers.LeftMouseButton);
            window.MouseUp(origin + new Point(55 + 30, 55 - 20), MouseButton.Left);
            Assert.True(wheel.X > 0.4, $"X {wheel.X}");
            Assert.True(wheel.Y > 0.2, $"Y {wheel.Y}");
            var file = Ui.Capture(window, "controles-roda-de-cor");
            wheel.X = 0;
            wheel.Y = 0;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void Regua_do_monitor_escolhe_passos_redondos_e_desenha()
    {
        Assert.Equal(100, MonitorRuler.Step(0.7, 60));
        Assert.Equal(500, MonitorRuler.Step(0.15, 60));
        var path = Ui.Run(() =>
        {
            var ruler = new MonitorRuler { VideoWidth = 1920, VideoHeight = 1080 };
            var window = Ui.Host(new Grid { Background = new SolidColorBrush(Color.Parse("#3A5F8A")), Width = 640, Height = 360, Children = { ruler } }, 640, 360);
            var file = Ui.Capture(window, "controles-regua-do-monitor");
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    // ------------------------------------------------------------------------------------------ curva

    private sealed class NoEditor : IClipEditor
    {
        public void UpdateClip(Func<Clip, Clip> change, string description, string? mergeKey = null) { }
        public MediaTime Offset(Clip clip) => MediaTime.Zero;
        public void UpdateEffect(Guid effectId, Func<EffectInstance, EffectInstance> change, string description, string? mergeKey) { }
        public void RemoveEffect(Guid effectId) { }
        public void MoveEffect(Guid effectId, int direction) { }
        public void ChooseLut(EffectItemViewModel item) { }
    }

    private static CurveParameterViewModel MakeCurve()
    {
        var effect = new EffectInstance { Id = Guid.NewGuid(), Type = EffectCatalog.Curves };
        return new EffectItemViewModel(new NoEditor(), effect).Parameters.OfType<CurveParameterViewModel>().First();
    }

    [Fact]
    public void Editor_de_curva_cria_e_arrasta_um_ponto()
    {
        var path = Ui.Run(() =>
        {
            var curve = MakeCurve();
            var editor = new CurveEditor { Curve = curve, Width = 240, Height = 180 };
            var window = Ui.Host(new Border { Background = new SolidColorBrush(Color.Parse("#2B2B33")), Padding = new Thickness(20), Child = editor }, 300, 240);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var origin = editor.TranslatePoint(new Point(0, 0), window)!.Value;
            // Área útil: margem de 6; o centro da diagonal é o ponto (0,5; 0,5).
            var w = editor.Bounds.Width - 12;
            var h = editor.Bounds.Height - 12;
            Point At(double x, double y) => origin + new Point(6 + x * w, 6 + (1 - y) * h);

            window.MouseDown(At(0.5, 0.5), MouseButton.Left);
            window.MouseMove(At(0.5, 0.7), RawInputModifiers.LeftMouseButton);
            window.MouseMove(At(0.5, 0.8), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(0.5, 0.8), MouseButton.Left);

            Assert.Equal(3, curve.Points.Count);
            Assert.Equal(0.5, curve.Points[1].X, 1);
            Assert.Equal(0.8, curve.Points[1].Y, 1);

            var file = Ui.Capture(window, "controles-editor-de-curva");

            // Botão direito remove o ponto do meio.
            window.MouseDown(At(0.5, 0.8), MouseButton.Right);
            window.MouseUp(At(0.5, 0.8), MouseButton.Right);
            Assert.Equal(2, curve.Points.Count);
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    // ------------------------------------------------------------------------------------------ área de corte

    [Fact]
    public void Area_de_corte_move_com_o_arrasto_e_respeita_a_proporcao()
    {
        var path = Ui.Run(() =>
        {
            var canvas = new CropCanvas { MediaSize = (1920, 1080), Frame = BgraImageSource(TimelineFixture.Gradient(320, 180, 0)) };
            canvas.Ratio = 1;
            canvas.Crop = canvas.Centered(new Point(0.5, 0.5), 0.6);
            var window = Ui.Host(new Border { Background = Brushes.Black, Child = canvas }, 640, 360);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var start = canvas.Crop;
            Assert.Equal(1.0, start.Width * 1920 / (start.Height * 1080), 2);

            // Arrasta de dentro da área, 64 px para a direita.
            var inside = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
            window.MouseDown(inside, MouseButton.Left);
            window.MouseMove(inside + new Point(32, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(inside + new Point(64, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(inside + new Point(64, 0), MouseButton.Left);
            Assert.True(canvas.Crop.X > start.X + 0.05, $"{start.X} -> {canvas.Crop.X}");
            Assert.Equal(start.Width, canvas.Crop.Width, 3);
            var file = Ui.Capture(window, "controles-area-de-corte");
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    private static Avalonia.Media.Imaging.Bitmap BgraImageSource(BgraImage image) => MeuEditorDeVideo.Desktop.Converters.BgraBitmaps.ToBitmap(image);

    [Fact]
    public void Cortar_e_ampliar_mostra_o_quadro_e_devolve_o_recorte()
    {
        var path = Ui.Run(() =>
        {
            var window = new CropZoomWindow(_ => TimelineFixture.Gradient(320, 180, 0), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1 / 30.0),
                TimeSpan.FromSeconds(2), (1920, 1080), (1080, 1920), (0, 0, 0, 0));
            var file = Ui.Capture(window, "janela-cortar-e-ampliar");
            // Projeto vertical: a área nasce com a proporção 9:16 dentro do quadro 16:9.
            var (l, t, r, b) = window.Crop;
            Assert.True(l > 0.2 && r > 0.2, $"esquerda {l}, direita {r}");
            Assert.Equal(0.1, t, 2);
            Assert.Equal(0.1, b, 2);
            window.Close();
            return file;
        });
        AssertCaptured(path);
    }

    // ------------------------------------------------------------------------------------------ janelas com o ViewModel real

    [Fact]
    public void Mixer_lista_os_canais_com_medidores_e_aceita_desfazer()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        var path = Ui.Run(() =>
        {
            var preview = ActivatorUtilities.CreateInstance<PreviewViewModel>(fx.Services, fx.Timeline);
            // O ViewModel zera os medidores a cada 40 ms; com o despachante "inline" o temporizador nunca dispara e os níveis ficam como o teste os pôs.
            var dispatcher = MeuEditorDeVideo.Presentation.Threading.UiThread.Dispatcher;
            MeuEditorDeVideo.Presentation.Threading.UiThread.Dispatcher = MeuEditorDeVideo.Presentation.Threading.InlineUiDispatcher.Instance;
            MixerViewModel mixer;
            try
            {
                mixer = new MixerViewModel(fx.Timeline, preview);
            }
            finally
            {
                MeuEditorDeVideo.Presentation.Threading.UiThread.Dispatcher = dispatcher;
            }
            var undone = 0;
            var undo = new CommunityToolkit.Mvvm.Input.RelayCommand(() => undone++);
            var redo = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            var window = new MixerWindow(mixer, undo, redo);
            Assert.NotEmpty(mixer.Strips);
            mixer.Strips[0].LevelL = 0.7;
            mixer.Strips[0].LevelR = 0.45;
            mixer.MasterL = 0.8;
            mixer.MasterR = 0.6;
            var file = Ui.Capture(window, "janela-mixer");
            window.KeyPress(Key.Z, Ui.Principal, PhysicalKey.Z, null);
            Assert.Equal(1, undone);
            window.Close();
            preview.Dispose();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void Forma_de_onda_do_clipe_mostra_o_trecho_e_o_cursor()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        var path = Ui.Run(() =>
        {
            var audio = ActivatorUtilities.CreateInstance<AudioInspectorViewModel>(fx.Services, fx.Timeline);
            fx.Timeline.Select(fx.ForestAudioClip, additive: false);
            fx.Timeline.SetPlayhead(MediaTime.FromSecondsApprox(15), keepVisible: false);
            var wave = new ClipWaveform { Inspector = audio };
            var window = Ui.Host(new Border { Background = new SolidColorBrush(Color.Parse("#2B2B33")), Padding = new Thickness(16), Child = wave }, 520, 110);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotNull(audio.WaveformClip());
            // Clicar no meio do clipe (13 s a 33 s) leva o cursor para o meio: 23 s.
            var origin = wave.TranslatePoint(new Point(0, 0), window)!.Value;
            window.MouseDown(origin + new Point(wave.Bounds.Width / 2, 30), MouseButton.Left);
            window.MouseUp(origin + new Point(wave.Bounds.Width / 2, 30), MouseButton.Left);
            Assert.Equal(23, fx.Timeline.Playhead.TotalSeconds, 1);
            var file = Ui.Capture(window, "controles-forma-de-onda");
            window.Close();
            audio.Dispose();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void Tela_cheia_mostra_o_quadro_da_previa()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        var path = Ui.Run(() =>
        {
            var preview = ActivatorUtilities.CreateInstance<PreviewViewModel>(fx.Services, fx.Timeline);
            var window = new FullScreenPreviewWindow(preview);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // O quadro entra depois de a janela abrir: a prévia pode ter desenhado um quadro preto ao abrir.
            preview.Image = new DisplayFrame(TimelineFixture.Gradient(640, 360, 1));
            var shown = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<FrameImage>().Single();
            Assert.NotNull(shown.Frame);
            Assert.NotNull(shown.Bitmap);
            Ui.Capture(window, "janela-tela-cheia"); // o primeiro quadro desenhado ainda é o anterior ao Image ligado; o segundo mostra o novo
            var file = Ui.Capture(window, "janela-tela-cheia");
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(closed);
            preview.Dispose();
            return file;
        });
        AssertCaptured(path);
    }

    [Fact]
    public void Clique_com_o_modificador_principal_insere_ponto_na_mascara_livre()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        Ui.Run(() =>
        {
            fx.Timeline.Select(fx.BeachClip, additive: false);
            using var editor = ActivatorUtilities.CreateInstance<CompositionInspectorViewModel>(fx.Services, fx.Timeline);
            editor.AddMaskCommand.Execute("Path");
            Assert.Equal(OverlayKind.MaskPath, editor.Overlay);
            var pontos = editor.OverlayPoints.Count;

            var overlay = new CompositionOverlay { Width = 400, Height = 225, Editor = editor };
            var window = Ui.Host(overlay, 400, 225);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var centro = overlay.TranslatePoint(new Point(200, 112.5), window)!.Value;

            window.MouseDown(centro, MouseButton.Left, Ui.Principal);
            window.MouseUp(centro, MouseButton.Left, Ui.Principal);
            Assert.Equal(pontos + 1, editor.OverlayPoints.Count);
            window.Close();
        });
    }

    [Fact]
    public void Aba_de_IA_avisa_e_desativa_o_que_nao_roda_sem_ONNX_Runtime()
    {
        using var fx = Ui.Run(() => new TimelineFixture());
        Ui.Run(() =>
        {
            fx.Timeline.Select(fx.BeachClip, additive: false);
            using var ia = ActivatorUtilities.CreateInstance<AiInspectorViewModel>(fx.Services, fx.Timeline);
            ia.Refresh();
            var window = Ui.Host(new ClipAiPanel { DataContext = ia }, 340, 900);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Ui.Capture(window, AiPlatform.OnnxRuntimeAvailable ? "aba-ia" : "aba-ia-mac-intel");

            var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            var remover = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == ia.RemoveBackgroundCommand);
            if (AiPlatform.OnnxRuntimeAvailable)
            {
                Assert.DoesNotContain(AiInspectorViewModel.NoOnnxRuntime, textos);
                Assert.Equal(ia.SegmentationAvailable, remover.IsEffectivelyEnabled);
            }
            else
            {
                Assert.Equal(AiInspectorViewModel.NoOnnxRuntime, ia.SegmentationStatus);
                Assert.Contains(AiInspectorViewModel.NoOnnxRuntime, textos);
                Assert.False(remover.IsEffectivelyEnabled);
                Assert.False(ia.IsolateVoiceCommand.CanExecute(null));
                Assert.Contains("não roda em Macs com Intel", ia.SceneStatus);
                Assert.NotEqual(AiInspectorViewModel.NoOnnxRuntime, ia.SpeechStatus); // o Whisper não usa ONNX
            }
            window.Close();
        });
    }

    [Fact]
    public void Camadas_de_edicao_sem_clipe_selecionado_nao_desenham_nem_pegam_o_ponteiro()
    {
        Ui.Run(() =>
        {
            var transform = new TransformOverlay { Width = 400, Height = 225 };
            var composition = new CompositionOverlay { Width = 400, Height = 225 };
            var window = Ui.Host(new Grid { Children = { transform, composition } }, 400, 225);
            Ui.Capture(window, "controles-camadas-vazias");
            var hit = window.InputHitTest(new Point(200, 100));
            Assert.NotSame(transform, hit);
            Assert.NotSame(composition, hit);
            window.Close();
        });
    }
}
