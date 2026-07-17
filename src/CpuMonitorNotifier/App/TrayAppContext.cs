using System.Diagnostics;
using System.Globalization;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Monitoring;
using CpuMonitorNotifier.Notifications;
using CpuMonitorNotifier.Settings;
using CpuMonitorNotifier.Theming;
using CpuMonitorNotifier.Tray;
using CpuMonitorNotifier.Update;

namespace CpuMonitorNotifier.App;

/// <summary>Хост tray-приложения: иконка, меню, таймеры опроса и анимации, жизненный цикл.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private const int RenderIntervalMs = 125; // частота перерисовки «живой» иконки
    private const int FirstUpdateCheckMs = 60_000;      // не тормозим запуск: первая проверка через минуту
    private const int UpdateCheckPollMs = 3_600_000;    // раз в час смотрим, не прошли ли сутки
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly AppSettings _settings;
    private readonly NotifyIcon _trayIcon;
    private readonly CpuSampler _sampler;
    private readonly ProcessSampler _processSampler = new();
    private readonly LoadDetector _detector;
    private readonly ProcessLoadDetector _procDetector = new();
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
    private readonly UpdateService _updates = new();
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly ToolStripMenuItem _checkUpdatesItem;
    // тосты активируются в фоновом потоке; через скрытый control возвращаемся в UI-поток.
    // SynchronizationContext.Current здесь непригоден: контекст ставит Application.Run, а он ещё не вызван.
    private readonly Control _marshal = new();
    private ReleaseInfo? _pendingUpdate;
    private bool _updating;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();
        _sampler = new CpuSampler();
        _detector = new LoadDetector(_sampler.CoreCount);
        _detector.Alert += OnAlert;
        _procDetector.Alert += OnProcessAlert;
        ApplySettings();

        try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { _appIcon = SystemIcons.Application; }

        var menu = new ContextMenuStrip();
        _settingsItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ShowSettings());
        _historyItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ShowHistory());
        _checkUpdatesItem = new ToolStripMenuItem(string.Empty, null, (_, _) => _ = CheckUpdatesManuallyAsync())
        {
            Visible = DistributionInfo.UpdatesSupported, // сборке из исходников обновляться неоткуда
        };
        _testItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SendTestNotification());
        _pauseItem = new ToolStripMenuItem { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => TogglePause();
        _exitItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ExitApp());
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_historyItem);
        menu.Items.Add(_checkUpdatesItem);
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

        _ = _marshal.Handle; // форсируем создание окна, иначе BeginInvoke бросит
        _notifier.UpdateRequested += () => _marshal.BeginInvoke(() => _ = StartUpdateAsync());

        _updateTimer = new System.Windows.Forms.Timer { Interval = FirstUpdateCheckMs };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = UpdateCheckPollMs;
            _ = CheckUpdatesAutomaticallyAsync();
        };
        if (DistributionInfo.UpdatesSupported)
            _updateTimer.Start();
    }

    private void ApplySettings()
    {
        Loc.Apply(_settings.Language);
        ThemeManager.Apply(_settings.Theme); // подхватится окнами, создаваемыми после смены
        _detector.ThresholdPercent = _settings.ThresholdPercent;
        _detector.DurationSeconds = _settings.DurationSeconds;
        _detector.Cooldown = TimeSpan.FromMinutes(_settings.CooldownMinutes);
        _procDetector.ThresholdCores = _settings.ProcessThresholdPercent / 100.0;
        _procDetector.DurationSeconds = _settings.ProcessDurationMinutes * 60;
        _procDetector.Cooldown = TimeSpan.FromMinutes(_settings.CooldownMinutes);
        _procDetector.Excluded = new HashSet<string>(_settings.ExcludedProcesses, StringComparer.OrdinalIgnoreCase);
        _renderer.Style = _settings.IconStyle;
    }

    private void LocalizeMenu()
    {
        _settingsItem.Text = Loc.T("menu.settings");
        _historyItem.Text = Loc.T("menu.history");
        _checkUpdatesItem.Text = Loc.T("menu.checkUpdates");
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
        if (_settings.ProcessAlertsEnabled)
            _procDetector.Update(_processSampler.LastLoads, _settings.PollIntervalSeconds, DateTime.Now);

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

    private void OnProcessAlert(ProcessAlert alert)
    {
        // в журнал истории (без привязки к конкретному ядру)
        _history.AddAlert(new AlertRecord(
            DateTime.Now, Array.Empty<int>(), alert.Name, alert.Cores, alert.Duration.TotalSeconds));

        if (_settings.NotificationsEnabled)
            _notifier.ShowProcessAlert(alert);
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

    /// <summary>Автоматическая проверка: молчит и об ошибке, и об актуальной версии — пользователь её не запрашивал.</summary>
    private async Task CheckUpdatesAutomaticallyAsync()
    {
        if (!_settings.UpdateCheckEnabled || _updating) return;
        if (DateTime.UtcNow - _settings.LastUpdateCheckUtc < UpdateCheckInterval) return;

        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        TrySaveSettings();

        var result = await _updates.CheckAsync();
        if (result.Status == UpdateCheckStatus.UpdateAvailable)
            OfferUpdate(result.Release!);
    }

    /// <summary>Ручная проверка: сообщает любой исход и не считается с суточным интервалом — её попросили.</summary>
    private async Task CheckUpdatesManuallyAsync()
    {
        if (_updating) return;

        var result = await _updates.CheckAsync();
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        TrySaveSettings(); // запись времени — бухгалтерия; её сбой не должен проглотить ответ ниже

        if (result.Status == UpdateCheckStatus.UpdateAvailable)
            OfferUpdate(result.Release!);
        else
            ReportCheckOutcome(result.Status);
    }

    /// <summary>Показывает исход проверки — только для запрошенных пользователем (ручная проверка, клик по тосту).</summary>
    private static void ReportCheckOutcome(UpdateCheckStatus status)
    {
        if (status == UpdateCheckStatus.UpToDate)
            MessageBox.Show(string.Format(Loc.T("update.upToDate"), UpdateVersions.Current),
                Loc.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        else // Failed (или NotSupported, недостижимо при видимом пункте меню)
            MessageBox.Show(string.Format(Loc.T("update.failed"), Loc.T("update.failed.network")),
                Loc.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Сохраняет настройки; её сбой (диск/AV/права) не должен рушить операцию, которая только что записала время проверки.</summary>
    private void TrySaveSettings()
    {
        try { _settings.Save(); }
        catch { /* не записали время последней проверки — не критично, просто проверим снова раньше */ }
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        _pendingUpdate = release;
        _notifier.ShowUpdateAvailable(release);
    }

    /// <summary>Загружает и подменяет .exe. Если подменять нельзя — отправляем на страницу релиза.</summary>
    private async Task StartUpdateAsync()
    {
        if (_updating) return;
        _updating = true; // до любого await: клики сериализованы UI-потоком, поэтому двойной запуск исключён
        try
        {
            // Тост живёт в Центре уведомлений и переживает перезапуск — тогда _pendingUpdate уже пуст.
            // Молчать в ответ на явный клик нельзя: перепрашиваем сервер и сообщаем исход.
            var release = _pendingUpdate;
            if (release is null)
            {
                var result = await _updates.CheckAsync();
                if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Release is null)
                {
                    ReportCheckOutcome(result.Status);
                    return;
                }
                release = result.Release;
                _pendingUpdate = release;
            }

            if (!UpdateInstaller.CanSwap())
            {
                // каталог недоступен на запись (например, Program Files) — не ломаемся, пусть скачает вручную
                OpenReleasePage(release.PageUrl);
                return;
            }

            _notifier.ShowDownloading(release.Version);
            string file = await _updates.DownloadAsync(release);
            UpdateInstaller.ApplyAndRestart(file); // отсюда процесс уже не вернётся
        }
        catch (Exception ex)
        {
            _notifier.ShowUpdateFailed(ex.Message); // в т.ч. путь к рабочему файлу после неудачного отката
            if (_pendingUpdate is not null)
                OpenReleasePage(_pendingUpdate.PageUrl);
        }
        finally
        {
            _updating = false; // на успешном пути ApplyAndRestart уже завершает приложение — сюда не дойдём
        }
    }

    private static void OpenReleasePage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* браузера может не быть — тогда ничего не делаем */ }
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
        _updateTimer.Stop();
        _marshal.Dispose();
        Application.Exit();
    }
}
