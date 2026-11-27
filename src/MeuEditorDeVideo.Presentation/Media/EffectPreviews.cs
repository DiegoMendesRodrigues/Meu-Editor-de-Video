// Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
using System.Collections.Concurrent;
using System.Globalization;
using MeuEditorDeVideo.Presentation.ViewModels.Inspector;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Effects;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.ImageEngine.Effects;

namespace MeuEditorDeVideo.Presentation.Media;

/// <summary>
/// Miniaturas dos efeitos e predefinições de cor: uma cena de praia com uma pessoa, gerada em código, passa pelo próprio
/// <see cref="EffectProcessor"/>, então a miniatura mostra o que o efeito realmente faz (os efeitos animados são
/// mostrados num instante em que aparecem bem). Geradas uma vez e mantidas em cache.
/// </summary>
public static class EffectPreviews
{
    public const int Width = 160;
    public const int Height = 100;

    private static readonly ConcurrentDictionary<string, BgraImage?> Cache = new(StringComparer.Ordinal);
    private static readonly Lazy<BgraImage> Sample = new(CreateSample);
    private static readonly Lazy<BgraImage> FilterSample = new(CreateFilterSample);

    /// <summary>Miniatura de um tipo de efeito do catálogo, de uma predefinição de cor ("preset:id") ou de um efeito pronto ("vfx:id"); null se desconhecido.</summary>
    public static BgraImage? Get(string? key) => key is null ? null : Cache.GetOrAdd(key, Create);

    private static BgraImage? Create(string key)
    {
        if (FilterPresets.Find(key) is { } filter)
        {
            try
            {
                return ToBitmap(EffectProcessor.Apply(FilterSample.Value, filter.Build(), new EffectContext(0.3, 0)).Image);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                return ToBitmap(FilterSample.Value);
            }
        }
        var time = 1.2;
        IReadOnlyList<EffectInstance>? effects;
        if (key.StartsWith("preset:", StringComparison.Ordinal)) effects = ColorPresets.All.FirstOrDefault(p => p.PreviewKey == key)?.Build();
        else if (VideoEffectPresets.Find(key) is { } preset)
        {
            effects = preset.Build();
            time = preset.PreviewTime;
        }
        else effects = Demo(key);
        if (effects is null) return null;
        try
        {
            var ctx = new EffectContext(0.3, (long)(time * 30), time, VideoEffectPresets.PreviewDuration);
            var (image, alpha) = EffectProcessor.Apply(Sample.Value, effects, ctx);
            // Movimento (tremidos, pulsos): sobrepõe instantes vizinhos, como um rastro, para a miniatura parada mostrá-lo.
            if (VideoEffectPresets.Find(key) is { Motion: true })
            {
                for (var k = 1; k <= 2; k++)
                {
                    var later = ctx with { ClipTime = time + k * 0.07, Frame = ctx.Frame + k * 2 };
                    image = EffectProcessor.Mix(image, EffectProcessor.Apply(Sample.Value, effects, later).Image, 1.0 / (k + 1));
                }
            }
            return ToBitmap(alpha ? OverChecker(image) : image);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return ToBitmap(Sample.Value);
        }
    }

    /// <summary>Instância de demonstração: valores que deixam o efeito evidente numa imagem pequena.</summary>
    private static IReadOnlyList<EffectInstance>? Demo(string type)
    {
        if (EffectCatalog.Find(type) is null) return null;
        var e = EffectCatalog.Create(type);
        e = type switch
        {
            EffectCatalog.ColorAdjust => With(e, ("saturation", 40), ("temperature", 30), ("contrast", 20)),
            EffectCatalog.WhiteBalance => EffectCatalog.WithColor(e, "neutral", 0.42, 0.5, 0.66),
            EffectCatalog.Curves => EffectCatalog.WithCurve(e, "master", [(0, 0), (0.25, 0.12), (0.75, 0.88), (1, 1)]),
            EffectCatalog.Wheels => EffectCatalog.WithWheel(EffectCatalog.WithWheel(e, "lift", -0.4, -0.3, 0), "gain", 0.35, 0.25, 0.05),
            // Sem arquivo .cube, a LUT é ilustrada por uma gradação estilizada.
            EffectCatalog.Lut => With(EffectCatalog.Create(EffectCatalog.ColorAdjust), ("temperature", -30), ("tint", 25), ("contrast", 30), ("hue", 15)),
            EffectCatalog.Hsl => With(e, ("blue.h", 25), ("blue.s", 60), ("green.h", -25), ("green.s", 50), ("orange.s", 70)),
            EffectCatalog.Blur => With(e, ("radius", 12)),
            EffectCatalog.Sharpen => With(e, ("amount", 200)),
            EffectCatalog.Vignette => With(e, ("amount", 90)),
            EffectCatalog.Pixelate => With(e, ("size", 26)),
            EffectCatalog.Noise => With(e, ("amount", 60)),
            EffectCatalog.Posterize => With(e, ("levels", 4)),
            _ => e,
        };
        // Refinar bordas só faz sentido sobre uma transparência: demonstra depois do chroma key.
        return type == EffectCatalog.EdgeRefine ? [EffectCatalog.Create(EffectCatalog.ChromaKey), e] : [e];
    }

    private static EffectInstance With(EffectInstance e, params (string Key, double Value)[] values) =>
        values.Aggregate(e, (acc, v) => EffectCatalog.WithValue(acc, v.Key, v.Value));

    /// <summary>
    /// Praia com uma pessoa no centro: céu com nuvens e sol, mar, areia e folhagem verde (para o chroma key). A pessoa
    /// deixa visíveis os efeitos de destaque, lupa e retoque, como nas miniaturas de editores comerciais.
    /// </summary>
    private static BgraImage CreateSample()
    {
        var px = new byte[Width * Height * 4];
        const double Cx = Width / 2.0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                double fy = y / (double)Height, fx = x / (double)Width;
                double r, g, b;
                if (fy < 0.52)
                {
                    // Céu: azul no alto, claro perto do horizonte, com nuvens e sol.
                    var t = fy / 0.52;
                    r = Lerp(70, 205, t); g = Lerp(140, 228, t); b = Lerp(225, 245, t);
                    var cloud = Math.Max(Blob(fx, fy, 0.2, 0.16, 0.13, 0.05), Blob(fx, fy, 0.3, 0.12, 0.09, 0.045));
                    cloud = Math.Max(cloud, Blob(fx, fy, 0.68, 0.3, 0.12, 0.04));
                    r = Lerp(r, 252, cloud); g = Lerp(g, 252, cloud); b = Lerp(b, 255, cloud);
                    var d = Math.Sqrt(Math.Pow((fx - 0.84) * 1.6, 2) + Math.Pow(fy - 0.15, 2));
                    if (d < 0.07) { r = 255; g = 242; b = 190; }
                    else if (d < 0.2) { var k = (0.2 - d) / 0.13 * 0.45; r = Lerp(r, 255, k); g = Lerp(g, 230, k); b = Lerp(b, 170, k); }
                }
                else if (fy < 0.7 + 0.015 * Math.Sin(fx * 18))
                {
                    // Mar com reflexos.
                    var t = (fy - 0.52) / 0.18;
                    r = Lerp(25, 60, t); g = Lerp(105, 165, t); b = Lerp(165, 195, t);
                    if (Math.Sin(x * 0.9 + y * 2.1) > 0.93) { r += 70; g += 70; b += 60; }
                }
                else
                {
                    // Areia com espuma na beira.
                    var n = Math.Sin(x * 2.3) * Math.Cos(y * 1.7) * 8;
                    r = 232 + n; g = 207 + n; b = 160 + n;
                    if (fy < 0.73) { r = 245; g = 245; b = 240; }
                }
                // Folhagem verde à esquerda e à direita.
                var bush = Math.Max(Blob(fx, fy, 0.05, 0.86, 0.12, 0.16), Blob(fx, fy, 0.97, 0.9, 0.1, 0.14));
                if (bush > 0.5) { var n = Math.Sin(x * 1.3) * Math.Cos(y * 1.9) * 18; r = 40 + n; g = 150 + n; b = 60; }

                // Pessoa: cabeça, cabelo, pescoço e tronco com camiseta.
                double dx = x + 0.5 - Cx, hy = y + 0.5 - 37;
                var head = Math.Sqrt(dx * dx / (8.5 * 8.5) + hy * hy / (10.5 * 10.5));
                double half = y < 47 ? 0 : y < 55 ? 8 + (y - 47) * 2.0 : Math.Min(27, 24 + (y - 55) * 0.2);
                if (head < 1)
                {
                    var hair = hy < -3 + Math.Abs(dx) * 0.35 || head > 0.88 && hy < 3;
                    (r, g, b) = hair ? (55.0, 38.0, 28.0) : (228.0 - Math.Max(0, dx) * 2, 178.0 - Math.Max(0, dx) * 2, 142.0);
                    if (!hair && Math.Abs(hy - 1) < 1 && Math.Abs(Math.Abs(dx) - 3.2) < 1) (r, g, b) = (40, 30, 30);
                }
                else if (y >= 45 && y < 50 && Math.Abs(dx) < 4.5) { r = 210; g = 160; b = 128; }
                else if (y >= 47 && Math.Abs(dx) < half)
                {
                    var shade = 1 - Math.Max(0, dx) / 90;
                    r = 225 * shade; g = 70 * shade; b = 80 * shade;
                    if (y < 52 && Math.Abs(dx) < 5) { r = 210; g = 160; b = 128; }
                }

                var i = (y * Width + x) * 4;
                px[i] = ToByte(b);
                px[i + 1] = ToByte(g);
                px[i + 2] = ToByte(r);
                px[i + 3] = 255;
            }
        }
        return new BgraImage(Width, Height, px);
    }

    /// <summary>
    /// Cena colorida para os filtros: céu com nuvens e sol, mar turquesa, areia, um farol listrado, uma palmeira, um
    /// veleiro de vela colorida e um guarda-sol. Tem tons de pele, verdes, azuis, vermelhos e amarelos, então a miniatura
    /// mostra bem a mudança de cor, clima e contraste de cada filtro. Desenhada com 3×3 amostras por pixel (bordas suaves).
    /// </summary>
    private static BgraImage CreateFilterSample()
    {
        const int Ss = 3;
        var px = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                double r = 0, g = 0, b = 0;
                for (var sy = 0; sy < Ss; sy++)
                {
                    for (var sx = 0; sx < Ss; sx++)
                    {
                        var (pr, pg, pb) = FilterScene((x + (sx + 0.5) / Ss) / Width, (y + (sy + 0.5) / Ss) / Height);
                        r += pr; g += pg; b += pb;
                    }
                }
                var i = (y * Width + x) * 4;
                px[i] = ToByte(b / (Ss * Ss));
                px[i + 1] = ToByte(g / (Ss * Ss));
                px[i + 2] = ToByte(r / (Ss * Ss));
                px[i + 3] = 255;
            }
        }
        return new BgraImage(Width, Height, px);
    }

    /// <summary>Cor da cena dos filtros no ponto (fx, fy) em frações do quadro.</summary>
    private static (double R, double G, double B) FilterScene(double fx, double fy)
    {
        const double Horizon = 0.56;
        double r, g, b;
        if (fy < Horizon)
        {
            var t = fy / Horizon;
            r = Lerp(40, 190, t); g = Lerp(110, 222, t); b = Lerp(215, 245, t);
            var cloud = Math.Max(Blob(fx, fy, 0.18, 0.2, 0.16, 0.06), Blob(fx, fy, 0.28, 0.15, 0.1, 0.06));
            cloud = Math.Max(cloud, Math.Max(Blob(fx, fy, 0.62, 0.26, 0.14, 0.05), Blob(fx, fy, 0.7, 0.22, 0.08, 0.05)));
            r = Lerp(r, 255, cloud); g = Lerp(g, 255, cloud); b = Lerp(b, 255, cloud);
            var d = Math.Sqrt(Math.Pow((fx - 0.9) * 1.6, 2) + Math.Pow(fy - 0.12, 2));
            if (d < 0.06) { r = 255; g = 246; b = 200; }
            else if (d < 0.22) { var k = (0.22 - d) / 0.16 * 0.5; r = Lerp(r, 255, k); g = Lerp(g, 225, k); b = Lerp(b, 160, k); }
        }
        else if (fy < 0.74 + 0.012 * Math.Sin(fx * 22))
        {
            var t = (fy - Horizon) / 0.18;
            r = Lerp(10, 40, t); g = Lerp(110, 190, t); b = Lerp(170, 200, t);
            if (Math.Sin(fx * 140 + fy * 330) > 0.9) { r += 60; g += 55; b += 45; }
        }
        else
        {
            var n = Math.Sin(fx * 300) * Math.Cos(fy * 270) * 7;
            r = 236 + n; g = 210 + n; b = 165 + n;
            if (fy < 0.77) { r = 248; g = 250; b = 246; }
        }

        // Farol: torre listrada de vermelho e branco sobre uma rocha, com lanterna amarela.
        const double Lx = 0.24;
        if (Blob(fx, fy, Lx, 0.62, 0.13, 0.08) > 0.6) { var n = Math.Sin(fx * 90) * 12; (r, g, b) = (95 + n, 88 + n, 80 + n); }
        var half = Lerp(0.035, 0.05, Math.Clamp((fy - 0.22) / 0.36, 0, 1));
        if (fy > 0.22 && fy < 0.6 && Math.Abs(fx - Lx) < half)
        {
            var stripe = (int)((fy - 0.22) / 0.075) % 2 == 0;
            var shade = fx > Lx ? 0.82 : 1;
            (r, g, b) = stripe ? (215 * shade, 40 * shade, 40 * shade) : (245 * shade, 245 * shade, 240 * shade);
        }
        if (fy > 0.15 && fy <= 0.22 && Math.Abs(fx - Lx) < 0.028) (r, g, b) = fy < 0.17 ? (60, 50, 50) : (255, 215, 60);
        if (fy > 0.13 && fy <= 0.15 && Math.Abs(fx - Lx) < 0.02 + (fy - 0.13)) (r, g, b) = (190, 35, 35);

        // Veleiro: casco branco e vela em faixas de arco-íris.
        double bx = 0.58, by = 0.6;
        if (fy > by && fy < by + 0.04 && Math.Abs(fx - bx) < 0.07 - (fy - by)) (r, g, b) = (250, 250, 250);
        if (fy > 0.3 && fy <= by && fx > bx - 0.005 && fx < bx + (fy - 0.3) * 0.22)
        {
            var band = (int)((fx - bx) / 0.012);
            (r, g, b) = band switch { 0 => (230, 50, 50), 1 => (245, 140, 30), 2 => (250, 215, 40), 3 => (60, 180, 90), 4 => (40, 140, 220), _ => (140, 80, 200) };
        }

        // Palmeira à direita: tronco curvo e folhas.
        var trunkX = 0.83 - Math.Pow((0.95 - fy) / 0.6, 2) * 0.06;
        if (fy > 0.33 && fy < 0.95 && Math.Abs(fx - trunkX) < 0.012) (r, g, b) = (120, 85, 50);
        var leaf = Math.Max(Math.Max(Blob(fx, fy, 0.76, 0.36, 0.09, 0.03), Blob(fx, fy, 0.86, 0.36, 0.09, 0.03)),
            Math.Max(Blob(fx, fy, 0.79, 0.31, 0.06, 0.035), Blob(fx, fy, 0.84, 0.42, 0.07, 0.025)));
        if (leaf > 0.5) { var n = Math.Sin(fx * 200) * 15; (r, g, b) = (40 + n, 135 + n, 55); }

        // Guarda-sol amarelo e azul com toalha vermelha.
        double ux = 0.46, uy = 0.8;
        if (fy > uy && fy < 0.93 && Math.Abs(fx - ux) < 0.004) (r, g, b) = (90, 90, 90);
        var dome = Math.Pow((fx - ux) / 0.09, 2) + Math.Pow((fy - uy) / 0.06, 2);
        if (dome < 1 && fy <= uy) (r, g, b) = (int)((fx - ux + 0.09) / 0.03) % 2 == 0 ? (250, 205, 40) : (40, 120, 210);
        if (fy > 0.9 && fy < 0.95 && fx > 0.5 && fx < 0.64) (r, g, b) = (225, 60, 70);
        return (r, g, b);
    }

    /// <summary>Mancha elíptica suave (1 no centro, 0 fora).</summary>
    private static double Blob(double fx, double fy, double cx, double cy, double rx, double ry)
    {
        var d = Math.Sqrt(Math.Pow((fx - cx) / rx, 2) + Math.Pow((fy - cy) / ry, 2));
        return Math.Clamp((1 - d) * 3, 0, 1);
    }

    /// <summary>Compõe a imagem com transparência sobre um xadrez, para a área removida ficar visível.</summary>
    private static BgraImage OverChecker(BgraImage image)
    {
        var px = (byte[])image.Pixels.Clone();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var i = (y * image.Width + x) * 4;
                double a = px[i + 3] / 255.0, bg = ((x / 8) + (y / 8)) % 2 == 0 ? 205 : 150;
                for (var c = 0; c < 3; c++) px[i + c] = ToByte(px[i + c] * a + bg * (1 - a));
                px[i + 3] = 255;
            }
        }
        return image with { Pixels = px };
    }

    /// <summary>Sem conversão: a interface converte a imagem BGRA (ex.: <c>BgraBitmaps</c>).</summary>
    private static BgraImage ToBitmap(BgraImage image) => image;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
