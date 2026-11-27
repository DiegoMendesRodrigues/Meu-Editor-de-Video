// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.MediaEngine.Native;
using MeuEditorDeVideo.MediaEngine.Recording;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>Gravação de tela por sistema operacional: argumentos do ffmpeg e leitura da saída das ferramentas (funções puras, com textos de exemplo).</summary>
public class RecordingCommandLinesTests
{
    private static string Join(IEnumerable<string> args) => string.Join(' ', args);

    // ------------------------------------------------------------ Linux

    [Fact]
    public void Linux_x11grab_screen_webcam_and_pulse_microphone()
    {
        var o = new ScreenRecordingOptions("/tmp/g.mp4", true, "Integrated Camera (/dev/video0)", "alsa_input.pci-0000_00_1f.3.analog-stereo");
        var text = Join(RecordingCommandLines.BuildLinux(o, ":0", new ScreenArea(0, 0, 0, 1920, 1080, false)));
        Assert.Contains("-f x11grab -framerate 30 -draw_mouse 1 -video_size 1920x1080 -i :0+0,0", text);
        Assert.Contains("-f v4l2 -i /dev/video0", text);
        Assert.Contains("-f pulse -i alsa_input.pci-0000_00_1f.3.analog-stereo", text);
        Assert.Contains("[1:v]scale=192:-2[cam]", text);
        Assert.Contains("-map 2:a", text);
        Assert.EndsWith("/tmp/g.mp4", text);
    }

    [Fact]
    public void Linux_area_uses_offsets_and_microphone_only_defaults_to_pulse_default()
    {
        var area = new ScreenArea(2, 1920, 100, 1280, 720, false);
        var text = Join(RecordingCommandLines.BuildLinux(new ScreenRecordingOptions("o.mp4", true, null, "", ShowCursor: false), ":1.0", area));
        Assert.Contains("-draw_mouse 0 -video_size 1280x720 -i :1.0+1920,100", text);
        Assert.Contains("-f pulse -i default", text);
        var camOnly = Join(RecordingCommandLines.BuildLinux(new ScreenRecordingOptions("o.mp4", false, "/dev/video2", null), ":0", null));
        Assert.DoesNotContain("x11grab", camOnly);
        Assert.Contains("-f v4l2 -i /dev/video2", camOnly);
        Assert.Throws<InvalidOperationException>(() => RecordingCommandLines.BuildLinux(new ScreenRecordingOptions("o.mp4", true, null, null), ":0", null));
        Assert.Throws<InvalidOperationException>(() => RecordingCommandLines.BuildLinux(new ScreenRecordingOptions("o.mp4", false, null, "default"), ":0", null));
    }

    [Fact]
    public void Linux_session_detection()
    {
        static Func<string, string?> Env(params (string, string)[] vars) => name => vars.Where(v => v.Item1 == name).Select(v => v.Item2).FirstOrDefault();
        Assert.Equal(LinuxSession.X11, RecordingCommandLines.DetectLinuxSession(Env(("XDG_SESSION_TYPE", "x11"), ("DISPLAY", ":0"))));
        Assert.Equal(LinuxSession.X11, RecordingCommandLines.DetectLinuxSession(Env(("DISPLAY", ":0"))));
        Assert.Equal(LinuxSession.Wayland, RecordingCommandLines.DetectLinuxSession(Env(("XDG_SESSION_TYPE", "wayland"), ("DISPLAY", ":0"), ("WAYLAND_DISPLAY", "wayland-0"))));
        Assert.Equal(LinuxSession.Wayland, RecordingCommandLines.DetectLinuxSession(Env(("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":0"))));
        Assert.Equal(LinuxSession.None, RecordingCommandLines.DetectLinuxSession(Env()));
        Assert.Equal(LinuxSession.None, RecordingCommandLines.DetectLinuxSession(Env(("XDG_SESSION_TYPE", "x11"))));
    }

    [Fact]
    public void Linux_pactl_sources_skip_monitors()
    {
        const string output = "0\talsa_output.pci-0000_00_1f.3.analog-stereo.monitor\tPipeWire\ts32le 2ch 48000Hz\tSUSPENDED\n" +
                              "1\talsa_input.pci-0000_00_1f.3.analog-stereo\tPipeWire\ts32le 2ch 48000Hz\tRUNNING\n" +
                              "2\talsa_input.usb-Blue_Yeti-00.analog-stereo\tmodule-alsa-card.c\ts16le 2ch 44100Hz\tIDLE\n";
        Assert.Equal(["default", "alsa_input.pci-0000_00_1f.3.analog-stereo", "alsa_input.usb-Blue_Yeti-00.analog-stereo"], RecordingCommandLines.ParsePactlSources(output));
        Assert.Equal(["default"], RecordingCommandLines.ParsePactlSources(""));
    }

    [Fact]
    public void Linux_video4linux_cameras_keep_only_capture_nodes()
    {
        var cameras = RecordingCommandLines.ParseVideo4LinuxCameras(
        [
            ("/dev/video1", "Integrated Camera: Integrated I", "1"),
            ("/dev/video0", "Integrated Camera: Integrated C", "0"),
            ("/dev/video10", "HD Webcam", null),
        ]);
        Assert.Equal(["Integrated Camera: Integrated C (/dev/video0)", "HD Webcam (/dev/video10)"], cameras);
        Assert.Equal("/dev/video10", RecordingCommandLines.V4lPath(cameras[1]));
        Assert.Equal("/dev/video3", RecordingCommandLines.V4lPath("/dev/video3"));
    }

    [Fact]
    public void Linux_xrandr_monitors_and_xdpyinfo()
    {
        const string xrandr = """
            Screen 0: minimum 320 x 200, current 3840 x 1080, maximum 16384 x 16384
            eDP-1 connected 1920x1080+1920+0 (normal left inverted right x axis y axis) 344mm x 193mm
               1920x1080     60.00*+  59.97
            HDMI-1 connected primary 1921x1080+0+0 (normal left inverted right x axis y axis) 530mm x 300mm
               1920x1080     60.00*+
            DP-1 disconnected (normal left inverted right x axis y axis)
            """;
        var screens = RecordingCommandLines.ParseXrandr(xrandr);
        Assert.Equal([new ScreenArea(1, 0, 0, 1920, 1080, true), new ScreenArea(2, 1920, 0, 1920, 1080, false)], screens);
        Assert.Equal(new ScreenArea(0, 0, 0, 3840, 1080, false), RecordingCommandLines.BoundingArea(screens));
        Assert.Null(RecordingCommandLines.BoundingArea([]));
        Assert.Empty(RecordingCommandLines.ParseXrandr("Can't open display"));

        Assert.Equal([new ScreenArea(1, 0, 0, 1366, 768, true)],
            RecordingCommandLines.ParseXdpyinfo("screen #0:\n  dimensions:    1366x768 pixels (361x203 millimeters)\n"));
    }

    [Fact]
    public void Linux_window_lists_from_wmctrl_xprop_and_xwininfo()
    {
        const string wmctrl =
            "0x01e00003 -1 1234 0    0    1920 30   host xfce4-panel\n" +
            "0x04600003  0 4321 100  50   800  600  host Terminal - usuário@host: ~\n" +
            "0x05200007  1 999  -20  10   1000 700  host Meu documento.odt - LibreOffice\n";
        var windows = RecordingCommandLines.ParseWmctrl(wmctrl);
        Assert.Equal(2, windows.Count);
        Assert.Equal(new X11Window(0x04600003, "Terminal - usuário@host: ~", 100, 50, 800, 600, 4321), windows[0]);
        Assert.Equal(-20, windows[1].X);

        Assert.Equal([0x1400003L, 0x1600004L], RecordingCommandLines.ParseXpropClientList("_NET_CLIENT_LIST(WINDOW): window id # 0x1400003, 0x1600004\n"));
        Assert.Empty(RecordingCommandLines.ParseXpropClientList("_NET_CLIENT_LIST:  not found.\n"));
        Assert.Equal(4321, RecordingCommandLines.ParseXpropPid("_NET_WM_PID(CARDINAL) = 4321\n"));
        Assert.Equal(0, RecordingCommandLines.ParseXpropPid("_NET_WM_PID:  not found.\n"));

        const string xwininfo = """

            xwininfo: Window id: 0x1400003 "Firefox — Notícias"

              Absolute upper-left X:  120
              Absolute upper-left Y:  64
              Relative upper-left X:  0
              Relative upper-left Y:  0
              Width: 1280
              Height: 720
              Depth: 24
              Map State: IsViewable
            """;
        Assert.Equal(new X11Window(0x1400003, "Firefox — Notícias", 120, 64, 1280, 720, 0), RecordingCommandLines.ParseXwininfo(xwininfo));
        Assert.Null(RecordingCommandLines.ParseXwininfo(xwininfo.Replace("IsViewable", "IsUnMapped")));
        Assert.Null(RecordingCommandLines.ParseXwininfo("xwininfo: error: No such window with id 0x1."));
    }

    [Fact]
    public void Linux_failure_messages()
    {
        Assert.Contains("x11grab", RecordingCommandLines.FriendlyFailure(MediaOs.Linux, "Unknown input format: 'x11grab'"));
        Assert.Contains("grupo", RecordingCommandLines.FriendlyFailure(MediaOs.Linux, "/dev/video0: Permission denied"));
        Assert.Null(RecordingCommandLines.FriendlyFailure(MediaOs.Linux, "outra coisa"));
        Assert.Contains("Wayland", RecordingCommandLines.WaylandMessage);
    }

    // ------------------------------------------------------------ macOS

    private const string AvfOutput = """
        [AVFoundation indev @ 0x7f8a3c704380] AVFoundation video devices:
        [AVFoundation indev @ 0x7f8a3c704380] [0] FaceTime HD Camera
        [AVFoundation indev @ 0x7f8a3c704380] [1] Capture screen 0
        [AVFoundation indev @ 0x7f8a3c704380] [2] Capture screen 1
        [AVFoundation indev @ 0x7f8a3c704380] AVFoundation audio devices:
        [AVFoundation indev @ 0x7f8a3c704380] [0] MacBook Pro Microphone
        [AVFoundation indev @ 0x7f8a3c704380] [1] Blue Yeti
        : Input/output error
        """;

    [Fact]
    public void Mac_avfoundation_device_list()
    {
        var devices = RecordingCommandLines.ParseAvfoundationDevices(AvfOutput);
        Assert.Equal([new AvfDevice(0, "FaceTime HD Camera")], devices.Cameras);
        Assert.Equal([new AvfDevice(1, "Capture screen 0"), new AvfDevice(2, "Capture screen 1")], devices.Screens);
        Assert.Equal([new AvfDevice(0, "MacBook Pro Microphone"), new AvfDevice(1, "Blue Yeti")], devices.Audio);
        Assert.Empty(RecordingCommandLines.ParseAvfoundationDevices("nada").Video);
    }

    [Fact]
    public void Mac_builds_avfoundation_arguments()
    {
        var o = new ScreenRecordingOptions("/Users/a/g.mp4", true, "FaceTime HD Camera", "Blue Yeti", FrameRate: 24, Corner: WebcamCorner.TopLeft);
        var args = RecordingCommandLines.BuildMac(o, 2560, screenIndex: 2, cameraIndex: 0, microphoneIndex: 1);
        var text = Join(args);
        Assert.Contains("-f avfoundation -framerate 24 -capture_cursor 1 -i 2:none", text);
        Assert.Contains("-f avfoundation -i 0:none", text);
        Assert.Contains("-f avfoundation -i :1", text);
        Assert.Contains("[1:v]scale=256:-2[cam]", text);
        Assert.Contains("overlay=24:24", text);
        Assert.Contains("-map 2:a", text);
        Assert.Equal("/Users/a/g.mp4", args[^1]);

        var noCursor = Join(RecordingCommandLines.BuildMac(new ScreenRecordingOptions("o.mp4", true, null, null, ShowCursor: false), 1920, 1, null, null));
        Assert.Contains("-capture_cursor 0 -i 1:none", noCursor);
        Assert.Throws<InvalidOperationException>(() => RecordingCommandLines.BuildMac(o, 1920, 1, null, 1));
        Assert.Throws<InvalidOperationException>(() => RecordingCommandLines.BuildMac(o, 1920, null, 0, 1));
    }

    [Fact]
    public void Mac_resolutions_and_permission_message()
    {
        const string profiler = """
            Graphics/Displays:
              Apple M2:
                Displays:
                  Color LCD:
                    Resolution: 2560 x 1664 Retina
                  LG HDR:
                    Resolution: 3840 x 2160 (2160p/4K UHD 1 - Ultra High Definition)
            """;
        Assert.Equal([(2560, 1664), (3840, 2160)], RecordingCommandLines.ParseMacResolutions(profiler));
        Assert.Equal(RecordingCommandLines.MacPermissionMessage, RecordingCommandLines.FriendlyFailure(MediaOs.MacOs, "[AVFoundation indev @ 0x1] Error opening input: Input/output error"));
        Assert.Contains("Privacidade e Segurança", RecordingCommandLines.MacPermissionMessage);
        Assert.Null(RecordingCommandLines.FriendlyFailure(MediaOs.MacOs, "Unrelated"));
    }

    // ------------------------------------------------------------ Windows (comportamento preservado)

    [Fact]
    public void Windows_arguments_are_unchanged()
    {
        var text = Join(RecordingCommandLines.BuildWindows(new ScreenRecordingOptions(@"C:\v\g.mp4", true, "Cam", "Mic"), 1920));
        Assert.StartsWith("-hide_banner -y -loglevel error -thread_queue_size 1024 -f gdigrab -framerate 30 -draw_mouse 1 -i desktop " +
                          "-thread_queue_size 1024 -f dshow -rtbufsize 256M -i video=Cam -thread_queue_size 1024 -f dshow -i audio=Mic ", text);
        Assert.EndsWith("-c:v libx264 -preset veryfast -crf 22 -r 30 -movflags +faststart C:\\v\\g.mp4", text);
    }

    [Fact]
    public void Implementation_is_chosen_per_operating_system()
    {
        var recorder = ScreenRecorderFactory.Create();
        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsScreenRecorder>(recorder);
        else if (OperatingSystem.IsMacOS()) Assert.IsType<MacScreenRecorder>(recorder);
        else Assert.IsType<LinuxScreenRecorder>(recorder);
    }
}
