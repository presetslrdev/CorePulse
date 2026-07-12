using System.Diagnostics;
using CpuMonitorNotifier.Monitoring;
using CpuMonitorNotifier.Notifications;
using CpuMonitorNotifier.Settings;
using CpuMonitorNotifier.Tray;

namespace CpuMonitorNotifier.App;

/// <summary>Хост tray-приложения: иконка, меню, таймеры опроса и анимации, жизненный цикл.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private const int RenderIntervalMs = 125; // частота перерисовки «живой» иконки

    private readonly AppSettings _settings;
    private readonly NotifyIcon _trayIcon;
    private readonly CpuSampler _sampler;
    private readonly ProcessSampler _processSampler = new();
    private readonly LoadDetector _detector;
    private readonly TrayIconRenderer _renderer = new();
    private readonly ToastNotifier _notifier = new();
    private readonly System.Windows.Forms.Timer _sampleTimer;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ToolStripMenuItem _pauseItem;
    private SettingsForm? _settingsForm;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();
        _sampler = new CpuSampler();
        _detector = new LoadDetector(_sampler.CoreCount);
        _detector.Alert += OnAlert;
        ApplySettings();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        menu.Items.Add("Проверить уведомление", null, (_, _) => SendTestNotification());
        _pauseItem = new ToolStripMenuItem("Пауза мониторинга") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => TogglePause();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CPU Monitor Notifier",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        _sampleTimer = new System.Windows.Forms.Timer { Interval = _settings.PollIntervalSeconds * 1000 };
        _sampleTimer.Tick += (_, _) => OnSample();
        _sampleTimer.Start();

        _renderTimer = new System.Windows.Forms.Timer { Interval = RenderIntervalMs };
        _renderTimer.Tick += (_, _) => Render();
        _renderTimer.Start();

        OnSample(); // мгновенный первый замер, не дожидаясь секундного тика
    }

    private void ApplySettings()
    {
        _detector.ThresholdPercent = _settings.ThresholdPercent;
        _detector.DurationSeconds = _settings.DurationSeconds;
        _detector.Cooldown = TimeSpan.FromMinutes(_settings.CooldownMinutes);
        _renderer.Style = _settings.IconStyle;
    }

    /// <summary>Секундный тик: замер нагрузки, детекция, обновление подсказки.</summary>
    private void OnSample()
    {
        _sampler.Sample();
        _processSampler.Sample();
        _detector.Update(_sampler.CoreLoads, _settings.PollIntervalSeconds, DateTime.Now);
        SetTooltip(BuildTooltip());
    }

    /// <summary>Частый тик: перерисовка иконки с текущей фазой анимации.</summary>
    private void Render()
    {
        _renderer.Apply(_trayIcon, _sampler.CoreLoads, _detector.ActiveAlerts, _clock.Elapsed.TotalSeconds);
    }

    private string BuildTooltip()
    {
        int maxCore = 0;
        for (int i = 1; i < _sampler.CoreCount; i++)
            if (_sampler.CoreLoads[i] > _sampler.CoreLoads[maxCore])
                maxCore = i;

        // самое горячее ядро — ведущая информация
        string text = $"Ядро {maxCore}: {_sampler.CoreLoads[maxCore]:F0}% | CPU {_sampler.TotalLoad:F0}%";

        var top = _processSampler.GetTopConsumers(1);
        if (top.Count > 0 && top[0].Cores >= 0.5)
            text += $" | {top[0].Name}";
        return text;
    }

    private void OnAlert(LoadAlert alert)
    {
        if (_settings.NotificationsEnabled)
            _notifier.ShowAlert(alert, _processSampler.GetTopConsumers(3));
    }

    /// <summary>Показывает уведомление по текущему самому нагруженному ядру — для проверки, что тосты доходят.</summary>
    private void SendTestNotification()
    {
        int maxCore = 0;
        for (int i = 1; i < _sampler.CoreCount; i++)
            if (_sampler.CoreLoads[i] > _sampler.CoreLoads[maxCore])
                maxCore = i;

        var alert = new LoadAlert(new[] { maxCore }, TimeSpan.FromSeconds(_settings.DurationSeconds));
        _notifier.ShowAlert(alert, _processSampler.GetTopConsumers(3));
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        if (_settingsForm.ShowDialog() == DialogResult.OK)
        {
            _settingsForm.ApplyTo(_settings);
            _settings.Save();
            ApplySettings();
            _sampleTimer.Interval = _settings.PollIntervalSeconds * 1000;
        }
        _settingsForm.Dispose();
        _settingsForm = null;
    }

    private void TogglePause()
    {
        if (_pauseItem.Checked)
        {
            _sampleTimer.Stop();
            _renderTimer.Stop();
            _trayIcon.Icon = SystemIcons.Application;
            SetTooltip("CPU Monitor Notifier — пауза");
        }
        else
        {
            _sampleTimer.Start();
            _renderTimer.Start();
        }
    }

    private void SetTooltip(string text)
    {
        // Ограничение NotifyIcon.Text — 127 символов
        _trayIcon.Text = text.Length <= 127 ? text : text[..127];
    }

    private void ExitApp()
    {
        _sampleTimer.Stop();
        _renderTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _renderer.Dispose();
        _sampler.Dispose();
        _notifier.Dispose();
        Application.Exit();
    }
}
