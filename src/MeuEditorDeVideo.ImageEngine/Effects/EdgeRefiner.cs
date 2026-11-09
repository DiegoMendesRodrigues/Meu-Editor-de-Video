using MeuEditorDeVideo.Core.Media;
using OpenCvSharp;

namespace MeuEditorDeVideo.ImageEngine.Effects;

/// <summary>
/// Refino da máscara de transparência (efeito "Refinar bordas"): contrai ou expande a área opaca, suaviza a transição
/// e limpa a cor das bordas (remove o reflexo da cor de fundo, típico após chroma key). Trabalha em BGRA não
/// pré-multiplicado; a imagem de entrada nunca é alterada.
/// </summary>
internal static class EdgeRefiner
{
    /// <param name="chokePx">Positivo contrai a área opaca; negativo expande (em pixels).</param>
    /// <param name="featherPx">Raio de suavização da transição (em pixels); abaixo de 0,5 não suaviza.</param>
    /// <param name="decontaminate">Intensidade da limpeza de cor das bordas, de 0 a 1.</param>
    public static BgraImage Apply(BgraImage src, double chokePx, double featherPx, double decontaminate)
    {
        int w = src.Width, h = src.Height;
        var px = (byte[])src.Pixels.Clone();
        var n = w * h;
        if (n == 0) return new BgraImage(w, h, px);

        var orig = new byte[n];
        for (var i = 0; i < n; i++) orig[i] = px[i * 4 + 3];

        using var origAlpha = new Mat(h, w, MatType.CV_8UC1);
        origAlpha.SetArray(orig);
        using var alpha = origAlpha.Clone();

        // Contrair (positivo) ou expandir (negativo) com kernel elíptico.
        var r = (int)Math.Round(Math.Abs(chokePx));
        if (r > 0)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * r + 1, 2 * r + 1));
            using var morphed = new Mat();
            if (chokePx > 0) Cv2.Erode(alpha, morphed, kernel);
            else Cv2.Dilate(alpha, morphed, kernel);
            morphed.CopyTo(alpha);
        }

        // Suavizar a transição.
        if (featherPx >= 0.5)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(alpha, blurred, new Size(0, 0), featherPx / 2);
            blurred.CopyTo(alpha);
        }

        alpha.GetArray(out byte[] outAlpha);

        // Limpar a cor das bordas: cor estimada da vizinhança opaca, misturada nos pixels semitransparentes.
        var amount = Math.Clamp(decontaminate, 0, 1);
        if (amount > 0)
        {
            var sigma = Math.Max(2, featherPx);
            // Blur de (cor × alfa) e de alfa, plano a plano (B, G, R, alfa).
            var alphaF = new float[n];
            for (var i = 0; i < n; i++) alphaF[i] = orig[i] / 255f;
            using var alphaMat = new Mat(h, w, MatType.CV_32FC1);
            alphaMat.SetArray(alphaF);
            using var denMat = new Mat();
            Cv2.GaussianBlur(alphaMat, denMat, new Size(0, 0), sigma);
            denMat.GetArray(out float[] den);

            var numerators = new float[3][];
            for (var c = 0; c < 3; c++)
            {
                var plane = new float[n];
                for (var i = 0; i < n; i++) plane[i] = px[i * 4 + c] / 255f * alphaF[i];
                using var planeMat = new Mat(h, w, MatType.CV_32FC1);
                planeMat.SetArray(plane);
                using var numMat = new Mat();
                Cv2.GaussianBlur(planeMat, numMat, new Size(0, 0), sigma);
                numMat.GetArray(out numerators[c]);
            }

            for (var i = 0; i < n; i++)
            {
                var na = outAlpha[i];
                if (na < 1 || na > 254) continue;
                var d = den[i];
                if (d < 1e-6f) continue;
                var weight = amount * (1 - na / 255.0);
                for (var c = 0; c < 3; c++)
                {
                    var estimate = numerators[c][i] / d * 255.0;
                    var current = px[i * 4 + c];
                    px[i * 4 + c] = (byte)Math.Clamp(Math.Round(current + (estimate - current) * weight), 0, 255);
                }
            }
        }

        for (var i = 0; i < n; i++) px[i * 4 + 3] = outAlpha[i];
        return new BgraImage(w, h, px);
    }
}
