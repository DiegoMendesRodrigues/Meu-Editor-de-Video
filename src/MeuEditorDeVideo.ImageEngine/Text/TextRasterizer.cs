using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;

namespace MeuEditorDeVideo.ImageEngine.Text;

/// <summary>
/// Texto rasterizado em pixels de saída (BGRA com alfa não pré-multiplicado).
/// <see cref="BoxWidth"/>/<see cref="BoxHeight"/> = bloco visível (texto + caixa de fundo), centrado na imagem;
/// a imagem tem margem extra para contorno e sombra.
/// </summary>
public sealed record TextRaster(BgraImage Image, double BoxWidth, double BoxHeight);

/// <summary>
/// Rasteriza textos com a pilha de texto do WPF (DirectWrite): fontes do sistema e do usuário, quebra de linha,
/// alinhamento, sublinhado, contorno (traço por fora), sombra com desfoque e caixa de fundo com barra de destaque
/// (RF132–RF138). Roda numa thread STA dedicada, então pode ser chamado da pré-visualização e da exportação.
/// Resultados ficam num cache pequeno (o mesmo texto se repete em muitos quadros).
/// </summary>
public static class TextRasterizer
{
    private const int MaxSide = 16384;
    private const int CacheLimit = 96;
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly BlockingCollection<Action> Work = [];
    private static readonly ConcurrentDictionary<Key, TextRaster> Cache = new();
    private static readonly Thread Worker = StartWorker();

    private readonly record struct Key(string Text, int Visible, TextStyle Style, double Scale, double MaxWidth);

    private static Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var action in Work.GetConsumingEnumerable()) action();
        })
        {
            IsBackground = true,
            Name = "Textos",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    /// <param name="text">Texto completo (define o tamanho e as quebras de linha do bloco).</param>
    /// <param name="visible">Caracteres desenhados (máquina de escrever); o bloco mantém o tamanho do texto completo.</param>
    /// <param name="scale">Pixels de saída por pixel do projeto.</param>
    /// <param name="frameWidth">Largura do quadro de saída (limita a largura do bloco, <see cref="TextStyle.MaxWidth"/>).</param>
    public static TextRaster Render(string text, int visible, TextStyle style, double scale, int frameWidth)
    {
        visible = Math.Clamp(visible, 0, text.Length);
        var key = new Key(text, visible, style, Math.Round(scale, 4), Math.Max(1, frameWidth * Math.Clamp(style.MaxWidth, 0.05, 1)));
        if (Cache.TryGetValue(key, out var cached)) return cached;

        TextRaster result;
        if (Thread.CurrentThread == Worker)
        {
            result = RenderCore(key);
        }
        else
        {
            var done = new TaskCompletionSource<TextRaster>(TaskCreationOptions.RunContinuationsAsynchronously);
            Work.Add(() =>
            {
                try
                {
                    done.SetResult(RenderCore(key));
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            });
            result = done.Task.GetAwaiter().GetResult();
        }

        if (Cache.Count >= CacheLimit) Cache.Clear();
        Cache[key] = result;
        return result;
    }

    /// <summary>Executa na thread STA de desenho (WPF) e devolve o resultado; reentrante na própria thread.</summary>
    internal static T OnWorker<T>(Func<T> func)
    {
        if (Thread.CurrentThread == Worker) return func();
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try
            {
                done.SetResult(func());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task.GetAwaiter().GetResult();
    }

    /// <summary>Converte "#AARRGGBB"/"#RRGGBB"/nome de cor; inválida = <paramref name="fallback"/>.</summary>
    public static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            return ColorConverter.ConvertFromString(value.Trim()) is Color c ? c : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    internal static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static TextRaster RenderCore(Key key)
    {
        var style = key.Style;
        var s = key.Scale;
        var size = Math.Clamp(style.FontSize * s, 1, 4000);
        var family = FontCatalog.Resolve(style.FontFamily);
        var typeface = new Typeface(family, style.Italic ? FontStyles.Italic : FontStyles.Normal,
            style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var align = style.Align switch
        {
            TextAlign.Left => TextAlignment.Left,
            TextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
        var fill = Brush(ParseColor(style.Color, Colors.White));

        FormattedText Make(string content, double maxWidth)
        {
            var ft = new FormattedText(content, Culture, FlowDirection.LeftToRight, typeface, size, fill, 1.0)
            {
                TextAlignment = align,
                MaxTextWidth = maxWidth,
                Trimming = TextTrimming.None,
            };
            if (Math.Abs(style.LineSpacing - 1) > 1e-3)
                ft.LineHeight = Math.Max(1, size * family.LineSpacing * Math.Clamp(style.LineSpacing, 0.5, 4));
            if (style.Underline) ft.SetTextDecorations(TextDecorations.Underline);
            return ft;
        }

        // Bloco medido com o texto completo; a largura final fixa o alinhamento das linhas parciais.
        var full = Make(key.Text, key.MaxWidth);
        var textW = Math.Max(1, Math.Ceiling(full.Width));
        var textH = Math.Max(1, Math.Ceiling(full.Height));
        var layoutWidth = textW + 1;
        var drawn = key.Visible >= key.Text.Length ? Make(key.Text, layoutWidth) : Make(key.Text[..key.Visible], layoutWidth);

        var bg = ParseColor(style.BackgroundColor, Colors.Transparent);
        var accent = ParseColor(style.AccentColor, Colors.Transparent);
        var hasBox = bg.A > 0 || accent.A > 0;
        var pad = hasBox ? Math.Max(0, style.BackgroundPadding * s) : 0;
        var accentW = accent.A > 0 ? Math.Max(2, Math.Round(size * 0.12)) : 0;
        var boxW = textW + 2 * pad + accentW;
        var boxH = textH + 2 * pad;

        var outline = Math.Max(0, style.OutlineWidth * s);
        var shadowColor = ParseColor(style.ShadowColor, Color.FromArgb(180, 0, 0, 0));
        var hasShadow = style.Shadow && shadowColor.A > 0;
        var sdx = hasShadow ? style.ShadowOffsetX * s : 0;
        var sdy = hasShadow ? style.ShadowOffsetY * s : 0;
        var blur = hasShadow ? Math.Max(0, style.ShadowBlur * s) : 0;
        var margin = Math.Ceiling(outline + Math.Max(Math.Abs(sdx), Math.Abs(sdy)) + blur * 1.5 + 2);

        var width = (int)Math.Min(MaxSide, Math.Ceiling(boxW + 2 * margin));
        var height = (int)Math.Min(MaxSide, Math.Ceiling(boxH + 2 * margin));
        var origin = new Point(margin + pad + accentW, margin + pad);
        var geometry = drawn.BuildGeometry(origin);
        var boxRect = new Rect(margin, margin, boxW, boxH);
        var radius = Math.Max(0, style.BackgroundRadius * s);

        // Camada principal: caixa, contorno e preenchimento.
        var main = new DrawingVisual();
        using (var dc = main.RenderOpen())
        {
            if (hasBox)
            {
                var box = new RectangleGeometry(boxRect, radius, radius);
                dc.PushClip(box);
                if (bg.A > 0) dc.DrawRectangle(Brush(bg), null, boxRect);
                if (accent.A > 0) dc.DrawRectangle(Brush(accent), null, new Rect(boxRect.X, boxRect.Y, accentW, boxH));
                dc.Pop();
            }
            if (outline > 0)
            {
                var pen = new Pen(Brush(ParseColor(style.OutlineColor, Colors.Black)), outline * 2) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
            dc.DrawGeometry(fill, null, geometry);
        }
        var pixels = Rasterize(main, width, height);

        if (hasShadow)
        {
            // Sombra: silhueta (caixa, se houver; senão o texto com contorno), deslocada e desfocada.
            var silhouette = new DrawingVisual();
            using (var dc = silhouette.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(sdx, sdy));
                if (bg.A > 0) dc.DrawRoundedRectangle(Brushes.White, null, boxRect, radius, radius);
                else
                {
                    if (outline > 0) dc.DrawGeometry(null, new Pen(Brushes.White, outline * 2) { LineJoin = PenLineJoin.Round }, geometry);
                    dc.DrawGeometry(Brushes.White, null, geometry);
                }
                dc.Pop();
            }
            var shadow = Rasterize(silhouette, width, height);
            var alpha = new float[width * height];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = shadow[i * 4 + 3] / 255f;
            if (blur >= 0.5) BoxBlur(alpha, width, height, Math.Max(1, (int)Math.Round(blur / 2)));
            UnderlayShadow(pixels, alpha, shadowColor);
        }

        Unpremultiply(pixels);
        return new TextRaster(new BgraImage(width, height, pixels), boxW, boxH);
    }

    internal static byte[] Rasterize(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>Coloca a sombra por baixo da camada principal (ambas pré-multiplicadas).</summary>
    private static void UnderlayShadow(byte[] main, float[] alpha, Color color)
    {
        var ca = color.A / 255f;
        for (var i = 0; i < alpha.Length; i++)
        {
            var sa = alpha[i] * ca;
            if (sa <= 0) continue;
            var p = i * 4;
            var inv = 1 - main[p + 3] / 255f;
            if (inv <= 0) continue;
            var w = sa * inv;
            main[p] = (byte)Math.Min(255, main[p] + color.B * w);
            main[p + 1] = (byte)Math.Min(255, main[p + 1] + color.G * w);
            main[p + 2] = (byte)Math.Min(255, main[p + 2] + color.R * w);
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
