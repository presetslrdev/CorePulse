using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CpuMonitorNotifier.Tray;

/// <summary>
/// Рисует иконку трея: сетка кругов, по одному на логическое ядро. Круг «наливается» и меняет
/// цвет по нагрузке — зелёный &lt;60%, жёлтый 60–90%, красный &gt;90%; ядро в алерте — белое кольцо.
/// Рендер в двойном разрешении с антиалиасингом ради гладких кругов на HiDPI-панели задач.
/// </summary>
internal sealed class TrayIconRenderer : IDisposable
{
    private const int Size = 64;      // внутреннее разрешение (Windows масштабирует под трей)
    private const int MaxDots = 36;   // при большем числе ядер агрегируем попарно (6×6)

    private static readonly Color BackColor = Color.FromArgb(170, 22, 22, 28);
    private static readonly Color TrackColor = Color.FromArgb(38, 255, 255, 255);
    private static readonly Color Green = Color.FromArgb(52, 199, 89);
    private static readonly Color Yellow = Color.FromArgb(255, 190, 50);
    private static readonly Color Red = Color.FromArgb(255, 69, 58);

    private Icon? _currentIcon;
    private IntPtr _currentHandle;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Рисует иконку по текущим нагрузкам и назначает её NotifyIcon, освобождая предыдущую.</summary>
    public void Apply(NotifyIcon trayIcon, IReadOnlyList<float> coreLoads, IReadOnlyList<bool> alerts)
    {
        (float[] loads, bool[] alarm) = Aggregate(coreLoads, alerts);
        int n = loads.Length;

        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (int)Math.Ceiling((double)n / cols);
        float cellW = (float)Size / cols;
        float cellH = (float)Size / rows;
        float maxR = Math.Min(cellW, cellH) / 2f - Size * 0.03f; // радиус круга минус зазор

        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            DrawBacker(g);

            for (int i = 0; i < n; i++)
            {
                int row = i / cols;
                int col = i % cols;
                int dotsInRow = Math.Min(cols, n - row * cols);
                float xOffset = (Size - dotsInRow * cellW) / 2f; // центрируем неполный ряд
                float cx = xOffset + col * cellW + cellW / 2f;
                float cy = row * cellH + cellH / 2f;

                float load = loads[i];
                var color = alarm[i] || load > 90f ? Red
                          : load >= 60f ? Yellow
                          : Green;

                // фоновый «трек» — круг всегда виден, задаёт структуру сетки
                DrawCircle(g, TrackColor, cx, cy, maxR, fill: true);

                // заливка по нагрузке: радиус растёт от 35% до 100% maxR
                float fillR = maxR * (0.35f + 0.65f * Math.Clamp(load / 100f, 0f, 1f));
                DrawCircle(g, color, cx, cy, fillR, fill: true);

                if (alarm[i]) // кольцо-маркер алерта
                    DrawCircle(g, Color.White, cx, cy, maxR, fill: false, penWidth: Size * 0.035f);
            }
        }

        IntPtr handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle);
        trayIcon.Icon = icon;
        ReleaseCurrent();
        _currentIcon = icon;
        _currentHandle = handle;
    }

    private static void DrawBacker(Graphics g)
    {
        float r = Size * 0.22f; // радиус скругления
        using var path = new GraphicsPath();
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(Size - r, 0, r, r, 270, 90);
        path.AddArc(Size - r, Size - r, r, r, 0, 90);
        path.AddArc(0, Size - r, r, r, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(BackColor);
        g.FillPath(brush, path);
    }

    private static void DrawCircle(Graphics g, Color color, float cx, float cy, float radius, bool fill, float penWidth = 1f)
    {
        float d = radius * 2f;
        var rect = new RectangleF(cx - radius, cy - radius, d, d);
        if (fill)
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, rect);
        }
        else
        {
            using var pen = new Pen(color, penWidth);
            g.DrawEllipse(pen, rect);
        }
    }

    /// <summary>Попарная агрегация (максимум пары), пока кругов больше MaxDots.</summary>
    private static (float[], bool[]) Aggregate(IReadOnlyList<float> loads, IReadOnlyList<bool> alerts)
    {
        var l = loads.ToArray();
        var a = alerts.ToArray();
        while (l.Length > MaxDots)
        {
            int half = (l.Length + 1) / 2;
            var l2 = new float[half];
            var a2 = new bool[half];
            for (int i = 0; i < half; i++)
            {
                int j = Math.Min(i * 2 + 1, l.Length - 1);
                l2[i] = MathF.Max(l[i * 2], l[j]);
                a2[i] = a[i * 2] || a[j];
            }
            l = l2;
            a = a2;
        }
        return (l, a);
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
