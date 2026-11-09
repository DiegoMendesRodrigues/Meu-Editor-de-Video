using MeuEditorDeVideo.Core.Media;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using OpenCvSharp;
using OpenCvSharp.Tracking;

namespace MeuEditorDeVideo.ImageEngine.Composition;

/// <summary>
/// Análise de movimento por visão computacional (OpenCV): rastreamento de uma região quadro a quadro (RF111, RF150)
/// e medição do movimento da câmera para a estabilização (RF075). Os quadros vêm de um callback (decodificação fica
/// fora daqui) e os resultados são normalizados pela imagem, independentes da resolução analisada.
/// </summary>
public static class MotionAnalyzer
{
    /// <summary>
    /// Rastreia a região <paramref name="start"/> nos instantes de origem <paramref name="times"/> (em ordem, a partir do
    /// quadro onde a região foi marcada; para rastrear para trás, passe os instantes em ordem decrescente).
    /// Usa o rastreador CSRT (escala e rotação moderadas); se ele falhar num quadro, procura por correspondência de modelo.
    /// </summary>
    public static IReadOnlyList<TrackSample> Track(Func<MediaTime, BgraImage?> frameAt, IReadOnlyList<MediaTime> times, TrackSample start,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var result = new List<TrackSample>(times.Count);
        if (times.Count == 0 || frameAt(times[0]) is not { } first) return result;

        using var firstColor = ToMat(first);
        var w = first.Width;
        var h = first.Height;
        var box = ToRect(start, w, h);
        result.Add(start with { SourceTime = times[0] });

        Tracker? tracker = null;
        try
        {
            tracker = TrackerCSRT.Create();
            tracker.Init(firstColor, box);
        }
        catch (Exception ex) when (ex is OpenCVException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            tracker?.Dispose();
            tracker = null;
        }

        using var firstGray = Gray(firstColor);
        using var roi = new Mat(firstGray, box);
        using var model = roi.Clone();
        try
        {
            for (var i = 1; i < times.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (frameAt(times[i]) is not { } frame) continue;
                using var mat = ToMat(frame);
                var found = false;
                if (tracker is not null)
                {
                    var r = box;
                    try
                    {
                        found = tracker.Update(mat, ref r) && r.Width > 2 && r.Height > 2;
                    }
                    catch (OpenCVException)
                    {
                        found = false;
                    }
                    if (found) box = r;
                }
                if (!found && MatchTemplate(Gray(mat), model, box, w, h) is { } matched) box = matched;

                result.Add(new TrackSample(times[i],
                    (box.X + box.Width / 2.0) / w, (box.Y + box.Height / 2.0) / h, (double)box.Width / w, (double)box.Height / h));
                progress?.Report((double)i / (times.Count - 1));
            }
        }
        finally
        {
            tracker?.Dispose();
        }
        return result;
    }

    /// <summary>
    /// Movimento da câmera entre quadros consecutivos: pontos de interesse (goodFeaturesToTrack) seguidos por fluxo óptico
    /// (Lucas–Kanade) e transformação de similaridade robusta (RANSAC) em torno do centro da imagem.
    /// O primeiro passo é zero (instante inicial).
    /// </summary>
    public static IReadOnlyList<MotionStep> AnalyzeCameraMotion(Func<MediaTime, BgraImage?> frameAt, IReadOnlyList<MediaTime> times,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var steps = new List<MotionStep>(times.Count);
        Mat? prev = null;
        try
        {
            for (var i = 0; i < times.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var frame = frameAt(times[i]);
                if (frame is null)
                {
                    steps.Add(new MotionStep(times[i], 0, 0, 0));
                    continue;
                }
                using var color = ToMat(frame);
                var gray = Gray(color);
                if (prev is null || prev.Size() != gray.Size())
                {
                    steps.Add(new MotionStep(times[i], 0, 0, 0));
                }
                else
                {
                    var (dx, dy, da) = Estimate(prev, gray);
                    steps.Add(new MotionStep(times[i], dx / gray.Width, dy / gray.Width, da));
                }
                prev?.Dispose();
                prev = gray;
                progress?.Report(times.Count > 1 ? (double)i / (times.Count - 1) : 1);
            }
        }
        finally
        {
            prev?.Dispose();
        }
        return steps;
    }

    private static (double Dx, double Dy, double Da) Estimate(Mat prev, Mat cur)
    {
        var corners = Cv2.GoodFeaturesToTrack(prev, 300, 0.01, Math.Max(8, prev.Width / 40.0), null!, 3, false, 0.04);
        if (corners.Length < 8) return (0, 0, 0);
        var next = new Point2f[corners.Length];
        Cv2.CalcOpticalFlowPyrLK(prev, cur, corners, ref next, out var status, out _, new Size(21, 21), 3);
        var cx = prev.Width / 2f;
        var cy = prev.Height / 2f;
        var a = new List<Point2f>();
        var b = new List<Point2f>();
        for (var i = 0; i < corners.Length; i++)
        {
            if (status[i] == 0) continue;
            a.Add(new Point2f(corners[i].X - cx, corners[i].Y - cy));
            b.Add(new Point2f(next[i].X - cx, next[i].Y - cy));
        }
        if (a.Count < 6) return (0, 0, 0);
        using var from = InputArray.Create(a);
        using var to = InputArray.Create(b);
        using var m = Cv2.EstimateAffinePartial2D(from, to);
        if (m is null || m.Empty()) return (0, 0, 0);
        var dx = m.At<double>(0, 2);
        var dy = m.At<double>(1, 2);
        var da = Math.Atan2(m.At<double>(1, 0), m.At<double>(0, 0));
        return double.IsFinite(dx + dy + da) ? (dx, dy, da) : (0, 0, 0);
    }

    /// <summary>Procura o modelo numa janela em volta da última posição (correlação normalizada).</summary>
    private static Rect? MatchTemplate(Mat gray, Mat model, Rect last, int w, int h)
    {
        using (gray)
        {
            var margin = Math.Max(last.Width, last.Height);
            var search = new Rect(last.X - margin, last.Y - margin, last.Width + 2 * margin, last.Height + 2 * margin)
                .Intersect(new Rect(0, 0, w, h));
            if (search.Width < model.Width || search.Height < model.Height) return null;
            using var region = new Mat(gray, search);
            using var score = new Mat();
            Cv2.MatchTemplate(region, model, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out var max, out _, out var loc);
            if (max < 0.35) return null;
            return new Rect(search.X + loc.X, search.Y + loc.Y, last.Width, last.Height);
        }
    }

    private static Rect ToRect(TrackSample s, int w, int h)
    {
        var rw = Math.Clamp((int)Math.Round(s.Width * w), 4, w);
        var rh = Math.Clamp((int)Math.Round(s.Height * h), 4, h);
        var x = Math.Clamp((int)Math.Round(s.X * w - rw / 2.0), 0, w - rw);
        var y = Math.Clamp((int)Math.Round(s.Y * h - rh / 2.0), 0, h - rh);
        return new Rect(x, y, rw, rh);
    }

    /// <summary>BGRA → Mat BGR (o canal alfa é descartado).</summary>
    private static Mat ToMat(BgraImage image)
    {
        using var bgra = new Mat(image.Height, image.Width, MatType.CV_8UC4);
        System.Runtime.InteropServices.Marshal.Copy(image.Pixels, 0, bgra.Data, image.Width * image.Height * 4);
        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }

    private static Mat Gray(Mat bgr)
    {
        var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }
}
