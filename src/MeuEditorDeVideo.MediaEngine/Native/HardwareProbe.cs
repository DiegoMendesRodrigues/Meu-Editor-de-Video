// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using FFmpeg.AutoGen;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Verificação da decodificação de vídeo por hardware (§12.3, RNF008).</summary>
public static unsafe class HardwareProbe
{
    private static readonly Lock Gate = new();
    private static bool _probed;
    private static AVHWDeviceType _device = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    /// <summary>Primeiro dispositivo de hardware (D3D11VA, VAAPI, CUDA ou VideoToolbox, conforme o SO) que pode ser criado, ou nenhum.</summary>
    public static AVHWDeviceType WorkingDevice()
    {
        lock (Gate)
        {
            if (_probed) return _device;
            _device = Probe(FFmpegPlatform.HardwareDeviceCandidates(FFmpegPlatform.Current));
            _probed = true;
            return _device;
        }
    }

    /// <summary>Um dispositivo de decodificação por GPU pode ser criado neste SO. O resultado é guardado.</summary>
    public static bool HardwareVideoDecoding() => WorkingDevice() != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    /// <summary>Mantido pelo nome histórico (Windows/D3D11VA); equivale a <see cref="HardwareVideoDecoding"/> em qualquer SO.</summary>
    public static bool D3D11VideoDecoding() => HardwareVideoDecoding();

    private static AVHWDeviceType Probe(IReadOnlyList<AVHWDeviceType> candidates)
    {
        try
        {
            FFmpegRuntime.EnsureInitialized();
            foreach (var type in candidates)
            {
                AVBufferRef* device = null;
                if (ffmpeg.av_hwdevice_ctx_create(&device, type, null, null, 0) >= 0)
                {
                    ffmpeg.av_buffer_unref(&device);
                    return type;
                }
            }
        }
        catch (Exception ex) when (ex is MeuEditorDeVideo.Core.Errors.MedException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Sem FFmpeg ou sem suporte: decodificação por software.
        }
        return AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
    }
}
