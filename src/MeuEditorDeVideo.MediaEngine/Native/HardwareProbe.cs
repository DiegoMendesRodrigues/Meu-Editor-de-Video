using FFmpeg.AutoGen;

namespace MeuEditorDeVideo.MediaEngine.Native;

/// <summary>Verificação da decodificação de vídeo por hardware (§12.3, RNF008).</summary>
public static unsafe class HardwareProbe
{
    private static bool? _d3d11va;

    /// <summary>Um dispositivo D3D11VA pode ser criado (decodificação por GPU disponível). O resultado é guardado.</summary>
    public static bool D3D11VideoDecoding()
    {
        if (_d3d11va is { } known) return known;
        var ok = false;
        try
        {
            FFmpegRuntime.EnsureInitialized();
            AVBufferRef* device = null;
            if (ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0) >= 0)
            {
                ok = true;
                ffmpeg.av_buffer_unref(&device);
            }
        }
        catch (Exception ex) when (ex is MeuEditorDeVideo.Core.Errors.MedException or DllNotFoundException or EntryPointNotFoundException)
        {
            ok = false;
        }
        _d3d11va = ok;
        return ok;
    }
}
