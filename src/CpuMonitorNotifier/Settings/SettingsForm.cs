using CpuMonitorNotifier.Tray;

namespace CpuMonitorNotifier.Settings;

/// <summary>Окно настроек: стиль иконки, пороги детекции, уведомления, автозапуск.</summary>
internal sealed class SettingsForm : Form
{
    private sealed record StyleItem(TrayIconStyle Style, string Title)
    {
        public override string ToString() => Title;
    }

    private static readonly StyleItem[] StyleItems =
    {
        new(TrayIconStyle.Ring, "Кольцо + %"),
        new(TrayIconStyle.Segments, "Сегментное кольцо"),
        new(TrayIconStyle.Speedometer, "Спидометр"),
        new(TrayIconStyle.Liquid, "Жидкость + %"),
        new(TrayIconStyle.Dots, "Сетка кругов"),
    };

    private readonly ComboBox _iconStyle;
    private readonly NumericUpDown _threshold;
    private readonly NumericUpDown _duration;
    private readonly NumericUpDown _cooldown;
    private readonly NumericUpDown _pollInterval;
    private readonly CheckBox _notifications;
    private readonly CheckBox _autoStart;

    public SettingsForm(AppSettings settings)
    {
        Text = "CPU Monitor Notifier — настройки";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(420, 288);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        layout.Controls.Add(new Label
        {
            Text = "Стиль иконки в трее:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        });
        _iconStyle = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        _iconStyle.Items.AddRange(StyleItems);
        _iconStyle.SelectedItem = Array.Find(StyleItems, s => s.Style == settings.IconStyle) ?? StyleItems[0];
        layout.Controls.Add(_iconStyle);

        _threshold = AddNumericRow(layout, "Порог нагрузки ядра, %:",
            (decimal)settings.ThresholdPercent, min: 50, max: 100);
        _duration = AddNumericRow(layout, "Длительность до алерта, с:",
            settings.DurationSeconds, min: 5, max: 3600);
        _cooldown = AddNumericRow(layout, "Пауза между уведомлениями, мин:",
            settings.CooldownMinutes, min: 1, max: 120);
        _pollInterval = AddNumericRow(layout, "Интервал опроса, с:",
            settings.PollIntervalSeconds, min: 1, max: 10);

        _notifications = new CheckBox
        {
            Text = "Показывать уведомления",
            Checked = settings.NotificationsEnabled,
            AutoSize = true,
        };
        layout.Controls.Add(_notifications);
        layout.SetColumnSpan(_notifications, 2);

        _autoStart = new CheckBox
        {
            Text = "Запускать при входе в Windows",
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
        var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel };
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
        settings.IconStyle = ((StyleItem)_iconStyle.SelectedItem!).Style;
        settings.ThresholdPercent = (float)_threshold.Value;
        settings.DurationSeconds = (int)_duration.Value;
        settings.CooldownMinutes = (int)_cooldown.Value;
        settings.PollIntervalSeconds = (int)_pollInterval.Value;
        settings.NotificationsEnabled = _notifications.Checked;
        AutoStart.IsEnabled = _autoStart.Checked;
    }

    private static NumericUpDown AddNumericRow(
        TableLayoutPanel layout, string label, decimal value, decimal min, decimal max)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        });
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
