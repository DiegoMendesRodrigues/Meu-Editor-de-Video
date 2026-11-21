using System.Collections.Concurrent;
using System.Globalization;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using SkiaSharp;

namespace MeuEditorDeVideo.ImageEngine.Text;

/// <summary>
/// Texto rasterizado em pixels de saída (BGRA com alfa não pré-multiplicado).
/// <see cref="BoxWidth"/>/<see cref="BoxHeight"/> = bloco visível (texto + caixa de fundo), centrado na imagem;
/// a imagem tem margem extra para contorno e sombra.
/// </summary>
public sealed record TextRaster(BgraImage Image, double BoxWidth, double BoxHeight);

/// <summary>
/// Rasteriza textos com SkiaSharp e HarfBuzz: fontes do sistema e do usuário, quebra de linha, alinhamento,
/// sublinhado, contorno (traço por fora), sombra com desfoque e caixa de fundo com barra de destaque (RF132–RF138).
/// Funciona no Windows, no Linux e no macOS e pode ser chamado de qualquer thread (pré-visualização e exportação).
/// Resultados ficam num cache pequeno (o mesmo texto se repete em muitos quadros).
/// </summary>
public static class TextRasterizer
{
    private const int MaxSide = 16384;
    private const int CacheLimit = 96;
    private const int LayoutCacheLimit = 64;
    private static readonly ConcurrentDictionary<Key, TextRaster> Cache = new();
    private static readonly ConcurrentDictionary<LayoutKey, Block> Layouts = new();

    private readonly record struct Key(string Text, int Visible, TextStyle Style, double Scale, double MaxWidth);

    private readonly record struct LayoutKey(string Text, string? Family, bool Bold, bool Italic, double Size, double LineSpacing, double MaxWidth);

    /// <summary>Texto completo diagramado (as linhas parciais da máquina de escrever usam as mesmas quebras).</summary>
    private sealed record Block(TextLayout Layout, List<TextLayout.Line> Lines, float TextWidth, float LineHeight, float FirstBaseline);

    /// <param name="text">Texto completo (define o tamanho e as quebras de linha do bloco).</param>
    /// <param name="visible">Caracteres desenhados (máquina de escrever); o bloco mantém o tamanho do texto completo.</param>
    /// <param name="scale">Pixels de saída por pixel do projeto.</param>
    /// <param name="frameWidth">Largura do quadro de saída (limita a largura do bloco, <see cref="TextStyle.MaxWidth"/>).</param>
    public static TextRaster Render(string text, int visible, TextStyle style, double scale, int frameWidth)
    {
        visible = Math.Clamp(visible, 0, text.Length);
        var key = new Key(text, visible, style, Math.Round(scale, 4), Math.Max(1, frameWidth * Math.Clamp(style.MaxWidth, 0.05, 1)));
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var result = RenderCore(key);
        if (Cache.Count >= CacheLimit) Cache.Clear();
        Cache[key] = result;
        return result;
    }

    /// <summary>Converte "#AARRGGBB", "#RRGGBB", "#ARGB", "#RGB" ou nome de cor (White, Red…); inválida = <paramref name="fallback"/>.</summary>
    public static SKColor ParseColor(string? value, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim();
        if (v.StartsWith('#'))
        {
            var hex = v[1..];
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return fallback;
            return hex.Length switch
            {
                8 => new SKColor(n),
                6 => new SKColor(0xFF000000 | n),
                4 => new SKColor((byte)(((n >> 8) & 0xF) * 17), (byte)(((n >> 4) & 0xF) * 17), (byte)((n & 0xF) * 17), (byte)(((n >> 12) & 0xF) * 17)),
                3 => new SKColor((byte)(((n >> 8) & 0xF) * 17), (byte)(((n >> 4) & 0xF) * 17), (byte)((n & 0xF) * 17)),
                _ => fallback,
            };
        }
        // Nomes de cor conhecidos (mesma lista do WPF e do CSS).
        var known = System.Drawing.Color.FromName(v);
        return known.IsKnownColor && !known.IsSystemColor ? new SKColor(known.R, known.G, known.B, known.A) : fallback;
    }

    private static Block LayOut(Key key, double size)
    {
        var style = key.Style;
        var layoutKey = new LayoutKey(key.Text, style.FontFamily, style.Bold, style.Italic, size, style.LineSpacing, key.MaxWidth);
        if (Layouts.TryGetValue(layoutKey, out var block)) return block;

        var layout = new TextLayout(FontCatalog.Resolve(style.FontFamily, style.Bold, style.Italic), size);
        var lines = layout.Wrap(key.Text, key.MaxWidth);
        float textWidth = 0;
        foreach (var line in lines) textWidth = Math.Max(textWidth, layout.Measure(key.Text, line.Start, line.End));
        var natural = layout.NaturalLineHeight;
        var lineHeight = Math.Abs(style.LineSpacing - 1) > 1e-3 ? Math.Max(1, natural * (float)Math.Clamp(style.LineSpacing, 0.5, 4)) : natural;
        // Com altura de linha própria, a linha de base fica na mesma proporção da altura (como no WPF).
        var firstBaseline = natural > 0 ? lineHeight * layout.Ascent / natural : layout.Ascent;
        block = new Block(layout, lines, textWidth, lineHeight, firstBaseline);
        if (Layouts.Count >= LayoutCacheLimit) Layouts.Clear();
        Layouts[layoutKey] = block;
        return block;
    }

    private static TextRaster RenderCore(Key key)
    {
        var style = key.Style;
        var s = key.Scale;
        var size = Math.Clamp(style.FontSize * s, 1, 4000);
        var block = LayOut(key, size);
        var layout = block.Layout;
        var fill = ParseColor(style.Color, SKColors.White);

        // Bloco medido com o texto completo; a largura final fixa o alinhamento das linhas parciais.
        var textW = Math.Max(1, Math.Ceiling(block.TextWidth));
        var textH = Math.Max(1, Math.Ceiling(block.LineHeight * Math.Max(1, block.Lines.Count)));
        var layoutWidth = textW + 1;

        var bg = ParseColor(style.BackgroundColor, SKColors.Transparent);
        var accent = ParseColor(style.AccentColor, SKColors.Transparent);
        var hasBox = bg.Alpha > 0 || accent.Alpha > 0;
        var pad = hasBox ? Math.Max(0, style.BackgroundPadding * s) : 0;
        var accentW = accent.Alpha > 0 ? Math.Max(2, Math.Round(size * 0.12)) : 0;
        var boxW = textW + 2 * pad + accentW;
        var boxH = textH + 2 * pad;

        var outline = Math.Max(0, style.OutlineWidth * s);
        var shadowColor = ParseColor(style.ShadowColor, new SKColor(0, 0, 0, 180));
        var hasShadow = style.Shadow && shadowColor.Alpha > 0;
        var sdx = hasShadow ? style.ShadowOffsetX * s : 0;
        var sdy = hasShadow ? style.ShadowOffsetY * s : 0;
        var blur = hasShadow ? Math.Max(0, style.ShadowBlur * s) : 0;
        var margin = Math.Ceiling(outline + Math.Max(Math.Abs(sdx), Math.Abs(sdy)) + blur * 1.5 + 2);

        var width = (int)Math.Min(MaxSide, Math.Ceiling(boxW + 2 * margin));
        var height = (int)Math.Min(MaxSide, Math.Ceiling(boxH + 2 * margin));
        var originX = (float)(margin + pad + accentW);
        var originY = (float)(margin + pad);

        // Contornos do texto visível (linhas do texto completo, cortadas no último caractere visível).
        var builder = new SKPathBuilder { FillType = SKPathFillType.Winding };
        for (var i = 0; i < block.Lines.Count; i++)
        {
            var line = block.Lines[i];
            var end = Math.Min(line.End, key.Visible);
            if (end <= line.Start) break;
            var lineW = layout.Measure(key.Text, line.Start, end);
            var x = originX + style.Align switch
            {
                TextAlign.Left => 0f,
                TextAlign.Right => (float)layoutWidth - lineW,
                _ => ((float)layoutWidth - lineW) / 2,
            };
            var baseline = originY + block.FirstBaseline + i * block.LineHeight;
            layout.AddGlyphs(builder, key.Text, line.Start, end, x, baseline);
            if (style.Underline && lineW > 0)
                builder.AddRect(SKRect.Create(x, baseline + layout.UnderlinePosition, lineW, layout.UnderlineThickness));
        }
        using var geometry = builder.Detach();
        builder.Dispose();
        // Negrito sintético (família sem negrito): o preenchimento ganha um traço fino em volta.
        var emboldenWidth = FontCatalog.Resolve(style.FontFamily, style.Bold, style.Italic).FakeBold ? (float)(size / 24) : 0;

        var boxRect = SKRect.Create((float)margin, (float)margin, (float)boxW, (float)boxH);
        var radius = (float)Math.Max(0, style.BackgroundRadius * s);

        // Camada principal: caixa, contorno e preenchimento.
        var pixels = Draw(width, height, canvas =>
        {
            if (hasBox)
            {
                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(boxRect, radius, radius), SKClipOperation.Intersect, antialias: true);
                if (bg.Alpha > 0) canvas.DrawRect(boxRect, Paint(bg));
                if (accent.Alpha > 0) canvas.DrawRect(SKRect.Create(boxRect.Left, boxRect.Top, (float)accentW, boxRect.Height), Paint(accent));
                canvas.Restore();
            }
            if (outline > 0)
                canvas.DrawPath(geometry, Stroke(ParseColor(style.OutlineColor, SKColors.Black), (float)(outline * 2) + emboldenWidth));
            canvas.DrawPath(geometry, FillPaint(fill, emboldenWidth));
        });

        if (hasShadow)
        {
            // Sombra: silhueta (caixa, se houver; senão o texto com contorno), deslocada e desfocada.
            var shadow = Draw(width, height, canvas =>
            {
                canvas.Translate((float)sdx, (float)sdy);
                if (bg.Alpha > 0) canvas.DrawRoundRect(boxRect, radius, radius, Paint(SKColors.White));
                else
                {
                    if (outline > 0) canvas.DrawPath(geometry, Stroke(SKColors.White, (float)(outline * 2) + emboldenWidth));
                    canvas.DrawPath(geometry, FillPaint(SKColors.White, emboldenWidth));
                }
            });
            var alpha = new float[width * height];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = shadow[i * 4 + 3] / 255f;
            if (blur >= 0.5) BoxBlur(alpha, width, height, Math.Max(1, (int)Math.Round(blur / 2)));
            UnderlayShadow(pixels, alpha, shadowColor);
        }

        Unpremultiply(pixels);
        return new TextRaster(new BgraImage(width, height, pixels), boxW, boxH);
    }

    internal static SKPaint Paint(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint FillPaint(SKColor color, float embolden) => embolden > 0
        ? new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = embolden, StrokeJoin = SKStrokeJoin.Round }
        : Paint(color);

    internal static SKPaint Stroke(SKColor color, float width, SKStrokeCap cap = SKStrokeCap.Butt) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeJoin = SKStrokeJoin.Round,
        StrokeCap = cap,
    };

    /// <summary>Desenha numa superfície BGRA pré-multiplicada e devolve os pixels.</summary>
    internal static byte[] Draw(int width, int height, Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* p = pixels)
            {
                using var surface = SKSurface.Create(info, (nint)p, width * 4)
                    ?? throw new InvalidOperationException("Não foi possível criar a superfície de desenho.");
                surface.Canvas.Clear(SKColors.Transparent);
                draw(surface.Canvas);
                surface.Canvas.Flush();
            }
        }
        return pixels;
    }

    /// <summary>Coloca a sombra por baixo da camada principal (ambas pré-multiplicadas).</summary>
    private static void UnderlayShadow(byte[] main, float[] alpha, SKColor color)
    {
        var ca = color.Alpha / 255f;
        for (var i = 0; i < alpha.Length; i++)
        {
            var sa = alpha[i] * ca;
            if (sa <= 0) continue;
            var p = i * 4;
            var inv = 1 - main[p + 3] / 255f;
            if (inv <= 0) continue;
            var w = sa * inv;
            main[p] = (byte)Math.Min(255, main[p] + color.Blue * w);
            main[p + 1] = (byte)Math.Min(255, main[p + 1] + color.Green * w);
            main[p + 2] = (byte)Math.Min(255, main[p + 2] + color.Red * w);
            main[p + 3] = (byte)Math.Min(255, main[p + 3] + 255 * w);
        }
    }

    /// <summary>Desfoque aproximadamente gaussiano: três passadas de caixa na horizontal e na vertical.</summary>
    internal static void BoxBlur(float[] data, int width, int height, int radius)
    {
        var tmp = new float[data.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            Blur1D(data, tmp, width, height, radius, horizontal: true);
            Blur1D(tmp, data, width, height, radius, horizontal: false);
        }
    }

    private static void Blur1D(float[] src, float[] dst, int width, int height, int r, bool horizontal)
    {
        var lines = horizontal ? height : width;
        var length = horizontal ? width : height;
        var norm = 1f / (2 * r + 1);
        Parallel.For(0, lines, line =>
        {
            int Index(int i) => horizontal ? line * width + i : i * width + line;
            float sum = 0;
            for (var i = -r; i <= r; i++) sum += src[Index(Math.Clamp(i, 0, length - 1))];
            for (var i = 0; i < length; i++)
            {
                dst[Index(i)] = sum * norm;
                sum += src[Index(Math.Min(length - 1, i + r + 1))] - src[Index(Math.Max(0, i - r))];
            }
        });
    }

    internal static void Unpremultiply(byte[] px)
    {
        for (var i = 0; i < px.Length; i += 4)
        {
            var a = px[i + 3];
            if (a is 0 or 255) continue;
            px[i] = (byte)Math.Min(255, px[i] * 255 / a);
            px[i + 1] = (byte)Math.Min(255, px[i + 1] * 255 / a);
            px[i + 2] = (byte)Math.Min(255, px[i + 2] * 255 / a);
        }
    }
}
