using CpuMonitorNotifier.App;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Theming;
using CpuMonitorNotifier.Tray;

namespace CpuMonitorNotifier.Settings;

/// <summary>Окно настроек: язык, стиль иконки, пороги детекции, уведомления, исключения, автозапуск.</summary>
internal sealed class SettingsForm : Form
{
    private sealed record StyleItem(TrayIconStyle Style, string Key)
    {
        public override string ToString() => Loc.T(Key);
    }

    private sealed record LangItem(AppLanguage Language, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record ThemeItem(AppTheme Theme, string Key)
    {
        public override string ToString() => Loc.T(Key);
    }

    private static readonly ThemeItem[] ThemeItems =
    {
        new(AppTheme.System, "theme.system"),
        new(AppTheme.Light, "theme.light"),
        new(AppTheme.Dark, "theme.dark"),
    };

    private static readonly StyleItem[] StyleItems =
    {
        new(TrayIconStyle.Ring, "style.ring"),
        new(TrayIconStyle.Segments, "style.segments"),
        new(TrayIconStyle.Speedometer, "style.speedometer"),
        new(TrayIconStyle.Liquid, "style.liquid"),
        new(TrayIconStyle.Dots, "style.dots"),
    };

    private readonly ComboBox _language;
    private readonly ComboBox _theme;
    private readonly ComboBox _iconStyle;
    private readonly NumericUpDown _threshold;
    private readonly NumericUpDown _duration;
    private readonly NumericUpDown _cooldown;
    private readonly NumericUpDown _pollInterval;
    private readonly NumericUpDown _procThreshold;
    private readonly NumericUpDown _procDuration;
    private readonly CheckBox _notifications;
    private readonly CheckBox _procAlerts;
    private readonly CheckBox _autoStart;
    private readonly List<string> _excluded;

    public SettingsForm(AppSettings settings)
    {
        Text = string.Format(Loc.T("settings.title"), Loc.AppName);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(440, 482);
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        _excluded = new List<string>(settings.ExcludedProcesses);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 14,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _language = AddComboRow(layout, Loc.T("settings.language"));
        var langItems = new List<LangItem> { new(AppLanguage.Auto, Loc.T("settings.languageAuto")) };
        langItems.AddRange(Loc.LanguageChoices.Select(c => new LangItem(c.Lang, c.Native)));
        _language.Items.AddRange(langItems.ToArray());
        _language.SelectedItem = langItems.Find(i => i.Language == settings.Language) ?? langItems[0];

        _theme = AddComboRow(layout, Loc.T("settings.theme"));
        _theme.Items.AddRange(ThemeItems);
        _theme.SelectedItem = Array.Find(ThemeItems, t => t.Theme == settings.Theme) ?? ThemeItems[0];

        _iconStyle = AddComboRow(layout, Loc.T("settings.iconStyle"));
        _iconStyle.Items.AddRange(StyleItems);
        _iconStyle.SelectedItem = Array.Find(StyleItems, s => s.Style == settings.IconStyle) ?? StyleItems[0];

        _threshold = AddNumericRow(layout, Loc.T("settings.threshold"),
            (decimal)settings.ThresholdPercent, min: 10, max: 100);
        _duration = AddNumericRow(layout, Loc.T("settings.duration"),
            settings.DurationSeconds, min: 5, max: 3600);
        _cooldown = AddNumericRow(layout, Loc.T("settings.cooldown"),
            settings.CooldownMinutes, min: 1, max: 120);
        _pollInterval = AddNumericRow(layout, Loc.T("settings.pollInterval"),
            settings.PollIntervalSeconds, min: 1, max: 10);
        _procThreshold = AddNumericRow(layout, Loc.T("settings.procThreshold"),
            (decimal)settings.ProcessThresholdPercent, min: 5, max: 100);
        _procDuration = AddNumericRow(layout, Loc.T("settings.procDuration"),
            settings.ProcessDurationMinutes, min: 1, max: 120);

        _notifications = new CheckBox
        {
            Text = Loc.T("settings.notifications"),
            Checked = settings.NotificationsEnabled,
            AutoSize = true,
        };
        layout.Controls.Add(_notifications);
        layout.SetColumnSpan(_notifications, 2);

        _procAlerts = new CheckBox
        {
            Text = Loc.T("settings.procAlerts"),
            Checked = settings.ProcessAlertsEnabled,
            AutoSize = true,
        };
        layout.Controls.Add(_procAlerts);
        layout.SetColumnSpan(_procAlerts, 2);

        var exclusionsBtn = new Button
        {
            Text = Loc.T("settings.exclusions"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        exclusionsBtn.Click += (_, _) =>
        {
            using var f = new ExclusionsForm(_excluded);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                _excluded.Clear();
                _excluded.AddRange(f.Result);
            }
        };
        layout.Controls.Add(exclusionsBtn);
        layout.SetColumnSpan(exclusionsBtn, 2);

        _autoStart = new CheckBox
        {
            Text = Loc.T("settings.autostart"),
            Checked = AutoStart.IsEnabled,
            AutoSize = true,
        };
        layout.Controls.Add(_autoStart);
        layout.SetColumnSpan(_autoStart, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var ok = new Button { Text = Loc.T("settings.ok"), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.T("settings.cancel"), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Переносит значения из контролов в настройки и применяет автозапуск. Вызывать при DialogResult.OK.</summary>
    public void ApplyTo(AppSettings settings)
    {
        settings.Language = ((LangItem)_language.SelectedItem!).Language;
        settings.Theme = ((ThemeItem)_theme.SelectedItem!).Theme;
        settings.IconStyle = ((StyleItem)_iconStyle.SelectedItem!).Style;
        settings.ThresholdPercent = (float)_threshold.Value;
        settings.DurationSeconds = (int)_duration.Value;
        settings.CooldownMinutes = (int)_cooldown.Value;
        settings.PollIntervalSeconds = (int)_pollInterval.Value;
        settings.ProcessThresholdPercent = (float)_procThreshold.Value;
        settings.ProcessDurationMinutes = (int)_procDuration.Value;
        settings.NotificationsEnabled = _notifications.Checked;
        settings.ProcessAlertsEnabled = _procAlerts.Checked;
        settings.ExcludedProcesses = new List<string>(_excluded);
        AutoStart.IsEnabled = _autoStart.Checked;
    }

    private static ComboBox AddComboRow(TableLayoutPanel layout, string label)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        layout.Controls.Add(combo);
        return combo;
    }

    private static NumericUpDown AddNumericRow(
        TableLayoutPanel layout, string label, decimal value, decimal min, decimal max)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        layout.Controls.Add(numeric);
        return numeric;
    }
}
