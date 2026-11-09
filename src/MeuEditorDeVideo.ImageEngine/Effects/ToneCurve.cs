namespace MeuEditorDeVideo.ImageEngine.Effects;

/// <summary>Curva de tons por interpolação cúbica monotônica (Fritsch–Carlson): sem "ondulações" entre pontos (RF098).</summary>
public static class ToneCurve
{
    /// <summary>Tabela de 256 entradas (0–1) a partir dos pontos (x, y) em 0–1.</summary>
    public static float[] Table(IReadOnlyList<(double X, double Y)> points)
    {
        var pts = points.OrderBy(p => p.X).GroupBy(p => p.X).Select(g => g.First()).ToList();
        var table = new float[256];
        if (pts.Count == 0) pts = [(0, 0), (1, 1)];
        if (pts.Count == 1)
        {
            Array.Fill(table, (float)pts[0].Y);
            return table;
        }

        var n = pts.Count;
        var d = new double[n - 1];
        for (var i = 0; i < n - 1; i++) d[i] = (pts[i + 1].Y - pts[i].Y) / (pts[i + 1].X - pts[i].X);
        var m = new double[n];
        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (var i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
        for (var i = 0; i < n - 1; i++)
        {
            if (d[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
            var a = m[i] / d[i];
            var b = m[i + 1] / d[i];
            var s = a * a + b * b;
            if (s > 9)
            {
                var t = 3 / Math.Sqrt(s);
                m[i] = t * a * d[i];
                m[i + 1] = t * b * d[i];
            }
        }

        for (var k = 0; k < 256; k++)
        {
            var x = k / 255.0;
            double y;
            if (x <= pts[0].X) y = pts[0].Y;
            else if (x >= pts[^1].X) y = pts[^1].Y;
            else
            {
                var i = 0;
                while (x > pts[i + 1].X) i++;
                var h = pts[i + 1].X - pts[i].X;
                var t = (x - pts[i].X) / h;
                var t2 = t * t;
                var t3 = t2 * t;
                y = (2 * t3 - 3 * t2 + 1) * pts[i].Y + (t3 - 2 * t2 + t) * h * m[i]
                    + (-2 * t3 + 3 * t2) * pts[i + 1].Y + (t3 - t2) * h * m[i + 1];
            }
            table[k] = (float)Math.Clamp(y, 0, 1);
        }
        return table;
    }
}
