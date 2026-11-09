using System.Runtime.InteropServices;
using MeuEditorDeVideo.Core.Media;
using OpenCvSharp;

namespace MeuEditorDeVideo.AIEngine.Vision;

/// <summary>Conversões entre <see cref="BgraImage"/>/máscaras e matrizes OpenCV.</summary>
internal static class CvImage
{
    public static Mat ToMat(BgraImage image)
    {
        var mat = new Mat(image.Height, image.Width, MatType.CV_8UC4);
        Marshal.Copy(image.Pixels, 0, mat.Data, image.Pixels.Length);
        return mat;
    }

    public static BgraImage ToImage(Mat bgra)
    {
        using var continuous = bgra.IsContinuous() ? null : bgra.Clone();
        var src = continuous ?? bgra;
        var px = new byte[src.Width * src.Height * 4];
        Marshal.Copy(src.Data, px, 0, px.Length);
        return new BgraImage(src.Width, src.Height, px);
    }

    public static Mat Gray(byte[] alpha, int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1);
        Marshal.Copy(alpha, 0, mat.Data, alpha.Length);
        return mat;
    }

    public static byte[] Bytes(Mat gray)
    {
        using var continuous = gray.IsContinuous() ? null : gray.Clone();
        var src = continuous ?? gray;
        var data = new byte[src.Width * src.Height];
        Marshal.Copy(src.Data, data, 0, data.Length);
        return data;
    }

    /// <summary>Lê imagem de caminho com acentos (o ImRead do OpenCV não aceita UTF-16 no Windows).</summary>
    public static Mat Read(string path, ImreadModes mode = ImreadModes.Unchanged) =>
        Cv2.ImDecode(File.ReadAllBytes(path), mode) is var m && !m.Empty() ? m : throw new InvalidDataException("Não foi possível ler a imagem.");

    public static void WritePng(string path, Mat mat)
    {
        Cv2.ImEncode(".png", mat, out var bytes);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
