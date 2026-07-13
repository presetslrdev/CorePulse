using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CpuMonitorNotifier.Tray;

/// <summary>
/// Рисует «живую» иконку трея в выбранном стиле. Число/дуга/уровень показывают текущую нагрузку
/// самого горячего ядра, а <b>цвет</b> — «нагретость» (как долго ядро под нагрузкой): короткий спайк
/// остаётся зелёным, а по-настоящему залипшее ядро разгорается зелёный→жёлтый→красный и пульсирует.
/// Рендер в двойном разрешении с антиалиасингом ради гладких форм на HiDPI-панели задач.
/// </summary>
internal sealed class TrayIconRenderer : IDisposable
{
    private const int Size = 64;
    private const int MaxSegments = 48; // при большем числе ядер агрегируем попарно

    private static readonly Color BackColor = Color.FromArgb(175, 22, 22, 28);
    private static readonly Color TrackColor = Color.FromArgb(45, 255, 255, 255);
    private static readonly Color Green = Color.FromArgb(52, 199, 89);
    private static readonly Color Yellow = Color.FromArgb(255, 190, 50);
    private static readonly Color Red = Color.FromArgb(255, 69, 58);

    private Icon? _currentIcon;
    private IntPtr _currentHandle;

    public TrayIconStyle Style { get; set; } = TrayIconStyle.Ring;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Цвет по «нагретости» 0..1: зелёный → жёлтый → красный.</summary>
    private static Color HeatColor(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5
            ? Lerp(Green, Yellow, t / 0.5)
            : Lerp(Yellow, Red, (t - 0.5) / 0.5);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    /// <summary>
    /// Рисует иконку и назначает её NotifyIcon, освобождая предыдущую.
    /// <paramref name="heat"/> — «нагретость» ядер 0..1 (для цвета), <paramref name="time"/> — секунды для анимации.
    /// </summary>
    public void Apply(NotifyIcon trayIcon, IReadOnlyList<float> coreLoads,
        IReadOnlyList<bool> alerts, IReadOnlyList<double> heat, double time)
    {
        // фокус — самое устойчиво-горячее ядро; если ничто не «греется», то просто самое нагруженное сейчас
        int focus = 0;
        double bestHeat = -1;
        for (int i = 0; i < coreLoads.Count; i++)
            if (heat[i] > bestHeat) { bestHeat = heat[i]; focus = i; }
        if (bestHeat <= 0)
            for (int i = 1; i < coreLoads.Count; i++)
                if (coreLoads[i] > coreLoads[focus]) focus = i;

        float focusLoad = coreLoads.Count > 0 ? coreLoads[focus] : 0f;
        double focusHeat = heat.Count > 0 ? heat[focus] : 0;
        bool anyAlarm = false;
        for (int i = 0; i < alerts.Count; i++)
            anyAlarm |= alerts[i];

        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            switch (Style)
            {
                case TrayIconStyle.Ring: DrawRing(g, focusLoad, focusHeat, anyAlarm, time); break;
                case TrayIconStyle.Segments: DrawSegments(g, coreLoads, alerts, heat); break;
                case TrayIconStyle.Speedometer: DrawSpeedometer(g, focusLoad, focusHeat); break;
                case TrayIconStyle.Liquid: DrawLiquid(g, focusLoad, focusHeat, time); break;
                case TrayIconStyle.Dots: DrawDots(g, coreLoads, alerts, heat); break;
            }
        }

        IntPtr handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle);
        trayIcon.Icon = icon;
        ReleaseCurrent();
        _currentIcon = icon;
        _currentHandle = handle;
    }

    // ---------- Кольцо + число ----------
    private static void DrawRing(Graphics g, float load, double heat, bool alarm, double time)
    {
        DrawBacker(g);
        float cx = Size / 2f, cy = Size / 2f, r = Size * 0.38f, t = Size * 0.12f;
        var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        var color = HeatColor(heat);

        if (alarm) // пульсирующее свечение позади кольца
        {
            double s = Math.Sin(time * Math.PI * 2 * 1.1) * 0.5 + 0.5;
            int a = (int)(25 + 65 * s);
            using var glow = new Pen(Color.FromArgb(a, color.R, color.G, color.B), t * 1.9f);
            g.DrawArc(glow, rect, 0, 360);
        }

        using (var track = new Pen(TrackColor, t))
            g.DrawArc(track, rect, 0, 360);

        if (load > 1f) // длина дуги — мгновенная нагрузка
        {
            using var pen = new Pen(color, t) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, rect, -90, 360f * load / 100f);
        }

        DrawCenteredNumber(g, load, cx, cy - Size * 0.02f, r * 1.5f, Size * 0.42f);
    }

    // ---------- Сегментное кольцо ----------
    private static void DrawSegments(Graphics g, IReadOnlyList<float> coreLoads,
        IReadOnlyList<bool> alerts, IReadOnlyList<double> heat)
    {
        DrawBacker(g);
        (float[] loads, bool[] alarm, double[] hot) = Aggregate(coreLoads, alerts, heat, MaxSegments);
        int n = loads.Length;
        int focus = FocusIndex(loads, hot);

        float cx = Size / 2f, cy = Size / 2f, r = Size * 0.38f, t = Size * 0.11f;
        var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        float seg = 360f / n;
        float gap = MathF.Min(6f, seg * 0.25f);

        for (int i = 0; i < n; i++)
        {
            var c = HeatColor(hot[i]);                        // цвет — нагретость
            int a = (int)(70 + 185 * Math.Clamp(loads[i] / 100f, 0f, 1f)); // яркость — мгновенная нагрузка
            float s0 = -90 + i * seg + gap / 2;
            using (var pen = new Pen(Color.FromArgb(a, c.R, c.G, c.B), t) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, rect, s0, seg - gap);

            if (i == focus || alarm[i]) // подсветка ядра в фокусе / алерте
                using (var hp = new Pen(Color.White, Size * 0.03f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(hp, rect, s0, seg - gap);
        }

        DrawCenteredNumber(g, loads[focus], cx, cy - Size * 0.01f, r * 1.1f, Size * 0.34f);
    }

    // ---------- Спидометр ----------
    private static void DrawSpeedometer(Graphics g, float load, double heat)
    {
        DrawBacker(g);
        float cx = Size / 2f, cy = Size * 0.54f, r = Size * 0.36f, t = Size * 0.12f;
        var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        const float start = 135f, total = 270f;

        using (var track = new Pen(TrackColor, t) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(track, rect, start, total);

        if (load > 1f)
        {
            using var pen = new Pen(HeatColor(heat), t) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, rect, start, total * load / 100f);
        }

        DrawCenteredNumber(g, load, cx, cy + Size * 0.01f, r * 1.4f, Size * 0.34f);
    }

    // ---------- «Жидкость» ----------
    private static void DrawLiquid(Graphics g, float load, double heat, double time)
    {
        using var path = RoundedRect(Size * 0.22f);
        using (var bg = new SolidBrush(BackColor))
            g.FillPath(bg, path);

        var state = g.Save();
        g.SetClip(path);

        var col = HeatColor(heat);
        float level = Size * (1f - load / 100f);
        float amp = Size * 0.045f;
        double phase = time * 3.0;

        var pts = new List<PointF>();
        for (int x = 0; x <= Size; x += 3)
            pts.Add(new PointF(x, level + amp * (float)Math.Sin(x / (double)Size * Math.PI * 3 + phase)));
        pts.Add(new PointF(Size, Size));
        pts.Add(new PointF(0, Size));
        using (var fill = new SolidBrush(Color.FromArgb(220, col.R, col.G, col.B)))
            g.FillPolygon(fill, pts.ToArray());

        g.Restore(state);
        DrawCenteredNumber(g, load, Size / 2f, Size / 2f, Size * 0.62f, Size * 0.40f);
    }

    // ---------- Сетка кругов ----------
    private static void DrawDots(Graphics g, IReadOnlyList<float> coreLoads,
        IReadOnlyList<bool> alerts, IReadOnlyList<double> heat)
    {
        DrawBacker(g);
        (float[] loads, bool[] alarm, double[] hot) = Aggregate(coreLoads, alerts, heat, 36);
        int n = loads.Length;
        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (int)Math.Ceiling((double)n / cols);
        float cellW = (float)Size / cols, cellH = (float)Size / rows;
        float maxR = Math.Min(cellW, cellH) / 2f - Size * 0.03f;

        for (int i = 0; i < n; i++)
        {
            int row = i / cols, col = i % cols;
            int dotsInRow = Math.Min(cols, n - row * cols);
            float xOffset = (Size - dotsInRow * cellW) / 2f;
            float cx = xOffset + col * cellW + cellW / 2f;
            float cy = row * cellH + cellH / 2f;

            FillCircle(g, TrackColor, cx, cy, maxR);
            float fillR = maxR * (0.35f + 0.65f * Math.Clamp(loads[i] / 100f, 0f, 1f)); // размер — нагрузка
            FillCircle(g, HeatColor(hot[i]), cx, cy, fillR);                             // цвет — нагретость
            if (alarm[i])
                using (var pen = new Pen(Color.White, Size * 0.035f))
                    g.DrawEllipse(pen, cx - maxR, cy - maxR, maxR * 2, maxR * 2);
        }
    }

    /// <summary>Индекс ядра в фокусе: самое горячее по «нагретости», иначе самое нагруженное.</summary>
    private static int FocusIndex(float[] loads, double[] heat)
    {
        int focus = 0;
        double best = -1;
        for (int i = 0; i < heat.Length; i++)
            if (heat[i] > best) { best = heat[i]; focus = i; }
        if (best <= 0)
            for (int i = 1; i < loads.Length; i++)
                if (loads[i] > loads[focus]) focus = i;
        return focus;
    }

    // ---------- примитивы ----------
    private static void DrawBacker(Graphics g)
    {
        using var path = RoundedRect(Size * 0.22f);
        using var brush = new SolidBrush(BackColor);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedRect(float r)
    {
        var p = new GraphicsPath();
        p.AddArc(0, 0, r, r, 180, 90);
        p.AddArc(Size - r, 0, r, r, 270, 90);
        p.AddArc(Size - r, Size - r, r, r, 0, 90);
        p.AddArc(0, Size - r, r, r, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void FillCircle(Graphics g, Color color, float cx, float cy, float radius)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private static void DrawCenteredNumber(Graphics g, float value, float cx, float cy, float maxWidth, float startSize)
    {
        string text = ((int)MathF.Round(value)).ToString();
        using var font = FitFont(g, text, maxWidth, startSize);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(Color.White);
        g.DrawString(text, font, brush, cx, cy, sf);
    }

    private static Font FitFont(Graphics g, string text, float maxWidth, float startSize)
    {
        for (float s = startSize; s > 6f; s -= 1f)
        {
            var f = new Font("Segoe UI", s, FontStyle.Bold, GraphicsUnit.Pixel);
            if (g.MeasureString(text, f).Width <= maxWidth)
                return f;
            f.Dispose();
        }
        return new Font("Segoe UI", 6f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    /// <summary>Попарная агрегация (максимум пары), пока элементов больше <paramref name="max"/>.</summary>
    private static (float[], bool[], double[]) Aggregate(
        IReadOnlyList<float> loads, IReadOnlyList<bool> alerts, IReadOnlyList<double> heat, int max)
    {
        var l = loads.ToArray();
        var a = alerts.ToArray();
        var h = heat.ToArray();
        while (l.Length > max)
        {
            int half = (l.Length + 1) / 2;
            var l2 = new float[half];
            var a2 = new bool[half];
            var h2 = new double[half];
            for (int i = 0; i < half; i++)
            {
                int j = Math.Min(i * 2 + 1, l.Length - 1);
                l2[i] = MathF.Max(l[i * 2], l[j]);
                a2[i] = a[i * 2] || a[j];
                h2[i] = Math.Max(h[i * 2], h[j]);
            }
            l = l2;
            a = a2;
            h = h2;
        }
        return (l, a, h);
    }

    private void ReleaseCurrent()
    {
        _currentIcon?.Dispose();
        if (_currentHandle != IntPtr.Zero)
        {
            DestroyIcon(_currentHandle); // GetHicon отдаёт владение — без этого течёт GDI-хендл
            _currentHandle = IntPtr.Zero;
        }
    }

    public void Dispose() => ReleaseCurrent();
}
