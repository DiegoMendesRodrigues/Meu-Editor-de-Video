namespace MeuEditorDeVideo.AudioEngine.Dsp;

/// <summary>FFT complexa radix-2 in-place (tamanho potência de 2).</summary>
public static class Fft
{
    public static void Transform(double[] re, double[] im, bool inverse = false)
    {
        var n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0) throw new ArgumentException("Tamanho da FFT deve ser potência de 2.");

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    var ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }

        if (inverse)
            for (var i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
    }

    public static int NextPowerOfTwo(int n)
    {
        var p = 1;
        while (p < n) p <<= 1;
        return p;
    }
}
