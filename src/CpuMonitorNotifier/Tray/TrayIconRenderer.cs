using System.Runtime.InteropServices;

namespace CpuMonitorNotifier.Tray;

/// <summary>
/// Рисует иконку трея: столбчатая диаграмма нагрузки всех логических ядер.
/// Зелёный &lt;60%, жёлтый 60–90%, красный &gt;90%; ядро в алерте — красный столбец с белым маркером.
/// </summary>
internal sealed class TrayIconRenderer : IDisposable
{
    private const int Size = 32;
    private const int MaxBars = 32; // при большем числе ядер агрегируем попарно

    private static readonly Color BackColor = Color.FromArgb(200, 20, 20, 24);
    private static readonly Color GreenBar = Color.FromArgb(80, 200, 90);
    private static readonly Color YellowBar = Color.FromArgb(235, 190, 50);
    private static readonly Color RedBar = Color.FromArgb(230, 60, 50);

    private Icon? _currentIcon;
    private IntPtr _currentHandle;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Рисует иконку по текущим нагрузкам и назначает её NotifyIcon, освобождая предыдущую.</summary>
    public void Apply(NotifyIcon trayIcon, IReadOnlyList<float> coreLoads, IReadOnlyList<bool> alerts)
    {
        (float[] loads, bool[] alarm) = Aggregate(coreLoads, alerts);

        using var bmp = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using (var back = new SolidBrush(BackColor))
                g.FillRectangle(back, 0, 0, Size, Size);

            float barWidth = (float)Size / loads.Length;
            for (int i = 0; i < loads.Length; i++)
            {
                float load = loads[i];
                int barHeight = Math.Max(1, (int)MathF.Round(load / 100f * Size));
                var color = alarm[i] || load > 90f ? RedBar
                          : load >= 60f ? YellowBar
                          : GreenBar;

                float x = i * barWidth;
                float w = MathF.Max(1f, barWidth - (barWidth >= 3f ? 1f : 0f)); // зазор между столбцами
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, x, Size - barHeight, w, barHeight);

                if (alarm[i]) // маркер алерта поверх столбца
                    using (var marker = new SolidBrush(Color.White))
                        g.FillRectangle(marker, x, 0, w, 3);
            }
        }

        IntPtr handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle);
        trayIcon.Icon = icon;
        ReleaseCurrent();
        _currentIcon = icon;
        _currentHandle = handle;
    }

    /// <summary>Попарная агрегация (максимум пары), пока столбцов больше MaxBars.</summary>
    private static (float[], bool[]) Aggregate(IReadOnlyList<float> loads, IReadOnlyList<bool> alerts)
    {
        var l = loads.ToArray();
        var a = alerts.ToArray();
        while (l.Length > MaxBars)
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
