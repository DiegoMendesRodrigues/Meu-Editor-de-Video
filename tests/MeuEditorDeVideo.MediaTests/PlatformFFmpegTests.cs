using System.IO;
using FFmpeg.AutoGen;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.MediaEngine.Export;
using MeuEditorDeVideo.MediaEngine.Native;

namespace MeuEditorDeVideo.MediaTests;

/// <summary>Escolhas do FFmpeg por sistema operacional (funções puras com o SO como parâmetro: cobrem os três SOs em qualquer máquina).</summary>
public sealed class PlatformFFmpegTests
{
    [Theory]
    [InlineData(MediaOs.Windows, "avcodec-63.dll", "ffmpeg.exe")]
    [InlineData(MediaOs.Linux, "libavcodec.so.63", "ffmpeg")]
    [InlineData(MediaOs.MacOs, "libavcodec.63.dylib", "ffmpeg")]
    public void Marcador_e_executavel_por_so(MediaOs os, string marker, string cli)
    {
        Assert.Equal(marker, FFmpegPlatform.MarkerLibrary(os));
        Assert.Equal(cli, FFmpegPlatform.CliName(os));
    }

    [Fact]
    public void Pasta_lib_so_nos_sistemas_unix()
    {
        Assert.Equal(["bin"], FFmpegPlatform.LibrarySubfolders(MediaOs.Windows));
        Assert.Equal(["bin", "lib"], FFmpegPlatform.LibrarySubfolders(MediaOs.Linux));
        Assert.Equal(["bin", "lib"], FFmpegPlatform.LibrarySubfolders(MediaOs.MacOs));
    }

    [Fact]
    public void Executavel_e_achado_na_pasta_irma_bin_quando_as_bibliotecas_estao_em_lib()
    {
        var root = Path.Combine(Path.GetTempPath(), "med-ffcli-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "bin", "ffmpeg"), "");
            Assert.Equal(Path.Combine(root, "bin", "ffmpeg"), FFmpegPlatform.FindCli(Path.Combine(root, "lib"), MediaOs.Linux));
            Assert.Null(FFmpegPlatform.FindCli(Path.Combine(root, "lib"), MediaOs.Windows));
            Assert.Null(FFmpegPlatform.FindCli(null, MediaOs.Linux));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Dispositivos_de_decodificacao_por_so()
    {
        Assert.Equal([AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA], FFmpegPlatform.HardwareDeviceCandidates(MediaOs.Windows));
        Assert.Equal([AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA], FFmpegPlatform.HardwareDeviceCandidates(MediaOs.Linux));
        Assert.Equal([AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX], FFmpegPlatform.HardwareDeviceCandidates(MediaOs.MacOs));
    }

    [Theory]
    [InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, AVPixelFormat.AV_PIX_FMT_D3D11)]
    [InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI, AVPixelFormat.AV_PIX_FMT_VAAPI)]
    [InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA, AVPixelFormat.AV_PIX_FMT_CUDA)]
    [InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX)]
    public void Formato_de_pixel_de_hardware_por_dispositivo(AVHWDeviceType type, AVPixelFormat format)
        => Assert.Equal(format, FFmpegPlatform.HardwarePixelFormat(type));

    [Fact]
    public void Codificadores_de_hardware_por_so()
    {
        Assert.Equal(["h264_nvenc", "h264_amf", "h264_qsv", "h264_mf"], EncoderCatalog.HardwareNames(VideoCodec.H264, MediaOs.Windows));
        Assert.Equal(["av1_nvenc", "av1_amf", "av1_qsv", "av1_mf"], EncoderCatalog.HardwareNames(VideoCodec.Av1, MediaOs.Windows));
        Assert.Equal(["vp9_qsv"], EncoderCatalog.HardwareNames(VideoCodec.Vp9, MediaOs.Windows));
        Assert.Equal(["mpeg2_qsv"], EncoderCatalog.HardwareNames(VideoCodec.Mpeg2, MediaOs.Windows));
        Assert.Empty(EncoderCatalog.HardwareNames(VideoCodec.ProRes, MediaOs.Windows));

        Assert.Equal(["hevc_nvenc", "hevc_vaapi", "hevc_qsv"], EncoderCatalog.HardwareNames(VideoCodec.Hevc, MediaOs.Linux, includeVaapi: true));
        Assert.Equal(["hevc_nvenc", "hevc_qsv"], EncoderCatalog.HardwareNames(VideoCodec.Hevc, MediaOs.Linux, includeVaapi: false));

        Assert.Equal(["h264_videotoolbox"], EncoderCatalog.HardwareNames(VideoCodec.H264, MediaOs.MacOs));
        Assert.Equal(["hevc_videotoolbox"], EncoderCatalog.HardwareNames(VideoCodec.Hevc, MediaOs.MacOs));
        Assert.Empty(EncoderCatalog.HardwareNames(VideoCodec.Av1, MediaOs.MacOs));
    }

    [Fact]
    public void Vaapi_nao_e_oferecido_enquanto_a_codificacao_nao_esta_implementada()
    {
        Assert.False(EncoderCatalog.VaapiEncodingSupported);
        Assert.DoesNotContain("h264_vaapi", EncoderCatalog.CandidateOrder(VideoCodec.H264, EncoderPreference.Auto, false, MediaOs.Linux));
    }

    [Fact]
    public void Ordem_de_tentativa_por_so()
    {
        Assert.Equal(["h264_videotoolbox", "libx264"], EncoderCatalog.CandidateOrder(VideoCodec.H264, EncoderPreference.Auto, false, MediaOs.MacOs));
        Assert.Equal(["h264_nvenc", "h264_amf", "h264_qsv", "libx264"], EncoderCatalog.CandidateOrder(VideoCodec.H264, EncoderPreference.Auto, false, MediaOs.Windows));
        Assert.Equal(["h264_nvenc", "h264_amf", "h264_qsv", "h264_mf", "libx264"], EncoderCatalog.CandidateOrder(VideoCodec.H264, EncoderPreference.Hardware, false, MediaOs.Windows));
        Assert.Equal(["libx264"], EncoderCatalog.CandidateOrder(VideoCodec.H264, EncoderPreference.Software, false, MediaOs.Linux));
        Assert.Equal(["libx264"], EncoderCatalog.CandidateOrder(VideoCodec.H264, EncoderPreference.Auto, true, MediaOs.MacOs));
    }

    [Theory]
    [InlineData("h264_videotoolbox", true)]
    [InlineData("hevc_vaapi", true)]
    [InlineData("h264_nvenc", true)]
    [InlineData("libx264", false)]
    public void Reconhece_codificador_de_hardware(string name, bool hardware) => Assert.Equal(hardware, EncoderCatalog.IsHardware(name));

    [Fact]
    public void Nomes_exibidos_dos_novos_codificadores()
    {
        Assert.Equal("Apple VideoToolbox (GPU)", EncoderCatalog.DisplayName("h264_videotoolbox"));
        Assert.Equal("VA-API (GPU)", EncoderCatalog.DisplayName("hevc_vaapi"));
    }
}
