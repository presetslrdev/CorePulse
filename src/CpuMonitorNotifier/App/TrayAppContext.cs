using CpuMonitorNotifier.Monitoring;
using CpuMonitorNotifier.Tray;

namespace CpuMonitorNotifier.App;

/// <summary>Хост tray-приложения: иконка, меню, таймер опроса, жизненный цикл.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly CpuSampler _sampler;
    private readonly TrayIconRenderer _renderer = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly bool[] _alerts;

    public TrayAppContext()
    {
        _sampler = new CpuSampler();
        _alerts = new bool[_sampler.CoreCount];

        var menu = new ContextMenuStrip();
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CPU Monitor Notifier",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    private void OnTick()
    {
        _sampler.Sample();
        _renderer.Apply(_trayIcon, _sampler.CoreLoads, _alerts);

        int maxCore = 0;
        for (int i = 1; i < _sampler.CoreCount; i++)
            if (_sampler.CoreLoads[i] > _sampler.CoreLoads[maxCore])
                maxCore = i;

        SetTooltip($"CPU {_sampler.TotalLoad:F0}% | ядро {maxCore}: {_sampler.CoreLoads[maxCore]:F0}%");
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
