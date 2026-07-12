using CpuMonitorNotifier.Monitoring;
using CpuMonitorNotifier.Tray;

namespace CpuMonitorNotifier.App;

/// <summary>Хост tray-приложения: иконка, меню, таймер опроса, жизненный цикл.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private const int IntervalSeconds = 1;

    private readonly NotifyIcon _trayIcon;
    private readonly CpuSampler _sampler;
    private readonly ProcessSampler _processSampler = new();
    private readonly LoadDetector _detector;
    private readonly TrayIconRenderer _renderer = new();
    private readonly System.Windows.Forms.Timer _timer;

    public TrayAppContext()
    {
        _sampler = new CpuSampler();
        _detector = new LoadDetector(_sampler.CoreCount);
        _detector.Alert += OnAlert;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CPU Monitor Notifier",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer = new System.Windows.Forms.Timer { Interval = IntervalSeconds * 1000 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private void OnTick()
    {
        _sampler.Sample();
        _processSampler.Sample();
        _detector.Update(_sampler.CoreLoads, IntervalSeconds, DateTime.Now);
        _renderer.Apply(_trayIcon, _sampler.CoreLoads, _detector.ActiveAlerts);
        SetTooltip(BuildTooltip());
    }

    private string BuildTooltip()
    {
        int maxCore = 0;
        for (int i = 1; i < _sampler.CoreCount; i++)
            if (_sampler.CoreLoads[i] > _sampler.CoreLoads[maxCore])
                maxCore = i;

        string text = $"CPU {_sampler.TotalLoad:F0}% | ядро {maxCore}: {_sampler.CoreLoads[maxCore]:F0}%";

        var top = _processSampler.GetTopConsumers(1);
        if (top.Count > 0 && top[0].Cores >= 0.5)
            text += $" | {top[0].Name}";
        return text;
    }

    private void OnAlert(LoadAlert alert)
    {
        // Пока нотификации не подключены (следующий этап) — balloon tip как заглушка
        string cores = string.Join(", ", alert.Cores);
        var culprits = _processSampler.GetTopConsumers(3);
        string detail = culprits.Count > 0
            ? "Вероятно: " + string.Join(", ", culprits.Select(c => $"{c.Name} ({c.Cores * 100:F0}%)"))
            : "Виновник не определён";

        _trayIcon.ShowBalloonTip(10000,
            $"Ядра под нагрузкой: {cores}",
            $"Дольше {alert.Duration.TotalSeconds:F0} с выше порога. {detail}",
            ToolTipIcon.Warning);
    }

    private void SetTooltip(string text)
    {
        // Ограничение NotifyIcon.Text — 127 символов
        _trayIcon.Text = text.Length <= 127 ? text : text[..127];
    }

    private void ExitApp()
    {
        _timer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _renderer.Dispose();
        _sampler.Dispose();
        Application.Exit();
    }
}
