using System.Diagnostics;
using System.Globalization;
using CpuMonitorNotifier.Localization;
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
    private readonly UsageHistory _history = new();
    private readonly System.Windows.Forms.Timer _sampleTimer;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _testItem;
    private readonly ToolStripMenuItem _historyItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Icon _appIcon;
    private SettingsForm? _settingsForm;
    private HistoryForm? _historyForm;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();
        _sampler = new CpuSampler();
        _detector = new LoadDetector(_sampler.CoreCount);
        _detector.Alert += OnAlert;
        ApplySettings();

        try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { _appIcon = SystemIcons.Application; }

        var menu = new ContextMenuStrip();
        _settingsItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ShowSettings());
        _historyItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ShowHistory());
        _testItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SendTestNotification());
        _pauseItem = new ToolStripMenuItem { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => TogglePause();
        _exitItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ExitApp());
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_historyItem);
        menu.Items.Add(_testItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = Loc.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();
        LocalizeMenu();

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
        Loc.Apply(_settings.Language);
        _detector.ThresholdPercent = _settings.ThresholdPercent;
        _detector.DurationSeconds = _settings.DurationSeconds;
        _detector.Cooldown = TimeSpan.FromMinutes(_settings.CooldownMinutes);
        _renderer.Style = _settings.IconStyle;
    }

    private void LocalizeMenu()
    {
        _settingsItem.Text = Loc.T("menu.settings");
        _historyItem.Text = Loc.T("menu.history");
        _testItem.Text = Loc.T("menu.test");
        _pauseItem.Text = Loc.T("menu.pause");
        _exitItem.Text = Loc.T("menu.exit");
    }

    /// <summary>Секундный тик: замер нагрузки, детекция, обновление подсказки.</summary>
    private void OnSample()
    {
        _sampler.Sample();
        _processSampler.Sample();
        _history.Accumulate(_processSampler.LastLoads, _settings.PollIntervalSeconds, DateTime.Now);
        _detector.Update(_sampler.CoreLoads, _settings.PollIntervalSeconds, DateTime.Now);

        float hottest = 0f;
        for (int i = 0; i < _sampler.CoreCount; i++)
            if (_sampler.CoreLoads[i] > hottest) hottest = _sampler.CoreLoads[i];
        _history.AddSample(hottest);

        SetTooltip(BuildTooltip());
    }

    /// <summary>Частый тик: перерисовка иконки с текущей фазой анимации.</summary>
    private void Render()
    {
        _renderer.Apply(_trayIcon, _sampler.CoreLoads, _detector.ActiveAlerts,
            _detector.Heat, _clock.Elapsed.TotalSeconds);
    }

    private string BuildTooltip()
    {
        int maxCore = 0;
        for (int i = 1; i < _sampler.CoreCount; i++)
            if (_sampler.CoreLoads[i] > _sampler.CoreLoads[maxCore])
                maxCore = i;

        var c = CultureInfo.CurrentCulture;
        string core = maxCore.ToString(c);
        string coreLoad = _sampler.CoreLoads[maxCore].ToString("F0", c);
        string cpu = _sampler.TotalLoad.ToString("F0", c);

        // самое горячее ядро — ведущая информация
        var top = _processSampler.GetTopConsumers(1);
        return top.Count > 0 && top[0].Cores >= 0.5
            ? string.Format(Loc.T("tooltip.proc"), core, coreLoad, cpu, top[0].Name)
            : string.Format(Loc.T("tooltip.noproc"), core, coreLoad, cpu);
    }

    private void OnAlert(LoadAlert alert)
    {
        var culprits = _processSampler.GetTopConsumers(3);

        var top = culprits.Count > 0 ? culprits[0] : null;
        _history.AddAlert(new AlertRecord(
            DateTime.Now, alert.Cores,
            top?.Name ?? "—", top?.Cores ?? 0,
            alert.Duration.TotalSeconds));

        if (_settings.NotificationsEnabled)
            _notifier.ShowAlert(alert, culprits);
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
            LocalizeMenu(); // язык мог смениться
            _sampleTimer.Interval = _settings.PollIntervalSeconds * 1000;
            SetTooltip(BuildTooltip());
        }
        _settingsForm.Dispose();
        _settingsForm = null;
    }

    private void ShowHistory()
    {
        if (_historyForm is { IsDisposed: false })
        {
            _historyForm.Activate();
            return;
        }

        _historyForm = new HistoryForm(_history, _settings.ThresholdPercent);
        _historyForm.FormClosed += (_, _) => _historyForm = null;
        _historyForm.Show();
    }

    private void TogglePause()
    {
        if (_pauseItem.Checked)
        {
            _sampleTimer.Stop();
            _renderTimer.Stop();
            _trayIcon.Icon = _appIcon;
            SetTooltip(string.Format(Loc.T("app.paused"), Loc.AppName));
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
