using System.Drawing.Drawing2D;

namespace CpuMonitorNotifier.App;

/// <summary>
/// Спарклайн-таймлайн нагрузки самого горячего ядра (0–100%). Линия окрашена вертикальным
/// градиентом зелёный→красный, снизу — заливка, пунктиром показан порог алерта. Видно «полочку»
/// устойчивой нагрузки против коротких пиков.
/// </summary>
internal sealed class SparklineControl : Control
{
    private float[] _values = Array.Empty<float>();
    private double _threshold = 90;

    public SparklineControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetData(float[] values, double thresholdPercent)
    {
        _values = values;
        _threshold = thresholdPercent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float w = Width, h = Height;
        float pad = 4f;
        float plotH = h - pad * 2;

        using (var bg = new SolidBrush(Color.FromArgb(255, 24, 24, 30)))
            g.FillRectangle(bg, 0, 0, w, h);

        // сетка 25/50/75%
        using (var grid = new Pen(Color.FromArgb(30, 255, 255, 255)))
            for (int p = 25; p <= 75; p += 25)
            {
                float y = pad + plotH * (1f - p / 100f);
                g.DrawLine(grid, pad, y, w - pad, y);
            }

        // порог — пунктир
        using (var thr = new Pen(Color.FromArgb(120, 255, 120, 90)) { DashStyle = DashStyle.Dash })
        {
            float y = pad + plotH * (1f - (float)_threshold / 100f);
            g.DrawLine(thr, pad, y, w - pad, y);
        }

        if (_values.Length < 2)
            return;

        float plotW = w - pad * 2;
        var pts = new PointF[_values.Length];
        for (int i = 0; i < _values.Length; i++)
        {
            float x = pad + plotW * i / (_values.Length - 1);
            float y = pad + plotH * (1f - Math.Clamp(_values[i], 0f, 100f) / 100f);
            pts[i] = new PointF(x, y);
        }

        // заливка под линией
        var fill = new PointF[pts.Length + 2];
        Array.Copy(pts, fill, pts.Length);
        fill[^2] = new PointF(pts[^1].X, h - pad);
        fill[^1] = new PointF(pts[0].X, h - pad);
        using (var fb = new LinearGradientBrush(
            new RectangleF(0, pad, w, plotH),
            Color.FromArgb(70, 255, 69, 58), Color.FromArgb(10, 52, 199, 89), LinearGradientMode.Vertical))
            g.FillPolygon(fb, fill);

        // линия с вертикальным градиентом (красный сверху, зелёный снизу)
        using var linePen = new Pen(new LinearGradientBrush(
            new RectangleF(0, pad, w, plotH),
            Color.FromArgb(255, 69, 58), Color.FromArgb(52, 199, 89), LinearGradientMode.Vertical), 1.8f)
        { LineJoin = LineJoin.Round };
        g.DrawLines(linePen, pts);
    }
}
