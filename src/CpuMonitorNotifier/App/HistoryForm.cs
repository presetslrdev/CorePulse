using System.Globalization;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Monitoring;

namespace CpuMonitorNotifier.App;

/// <summary>
/// Окно истории: вкладка «Главные нагрузчики за сессию» (процессы по накопленному core-времени —
/// ловит тихого пожирателя) и вкладка «Алерты» (журнал сработавших уведомлений).
/// </summary>
internal sealed class HistoryForm : Form
{
    private readonly UsageHistory _history;
    private readonly ListView _offenders;
    private readonly ListView _alerts;
    private readonly System.Windows.Forms.Timer _refresh;

    public HistoryForm(UsageHistory history)
    {
        _history = history;
        Text = string.Format(Loc.T("history.title"), Loc.AppName);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(560, 380);
        MinimumSize = new Size(460, 300);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        _offenders = MakeListView(
            (Loc.T("history.col.process"), 200),
            (Loc.T("history.col.coremin"), 110),
            (Loc.T("history.col.peak"), 90),
            (Loc.T("history.col.lastseen"), 110));
        var offendersTab = new TabPage(Loc.T("history.tab.offenders"));
        offendersTab.Controls.Add(_offenders);

        _alerts = MakeListView(
            (Loc.T("history.col.time"), 130),
            (Loc.T("history.col.cores"), 70),
            (Loc.T("history.col.process"), 150),
            (Loc.T("history.col.load"), 70),
            (Loc.T("history.col.duration"), 90));
        var alertsTab = new TabPage(Loc.T("history.tab.alerts"));
        alertsTab.Controls.Add(_alerts);

        tabs.TabPages.Add(offendersTab);
        tabs.TabPages.Add(alertsTab);
        Controls.Add(tabs);

        Populate();
        _refresh = new System.Windows.Forms.Timer { Interval = 2000 };
        _refresh.Tick += (_, _) => Populate();
        _refresh.Start();
        FormClosed += (_, _) => _refresh.Dispose();
    }

    private static ListView MakeListView(params (string Header, int Width)[] columns)
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        foreach (var (header, width) in columns)
            lv.Columns.Add(header, width);
        return lv;
    }

    private void Populate()
    {
        var c = CultureInfo.CurrentCulture;

        _offenders.BeginUpdate();
        _offenders.Items.Clear();
        foreach (var o in _history.TopOffenders(25))
        {
            var item = new ListViewItem(o.Name);
            item.SubItems.Add((o.CoreSeconds / 60).ToString("F1", c)); // core-минуты
            item.SubItems.Add((o.PeakCores * 100).ToString("F0", c) + "%");
            item.SubItems.Add(o.LastSeen.ToString("T", c));
            _offenders.Items.Add(item);
        }
        _offenders.EndUpdate();

        _alerts.BeginUpdate();
        _alerts.Items.Clear();
        foreach (var a in _history.Alerts)
        {
            var item = new ListViewItem(a.Time.ToString("g", c));
            item.SubItems.Add(string.Join(", ", a.Cores));
            item.SubItems.Add(a.Culprit);
            item.SubItems.Add((a.CulpritCores * 100).ToString("F0", c) + "%");
            item.SubItems.Add(FormatDuration(TimeSpan.FromSeconds(a.DurationSeconds), c));
            _alerts.Items.Add(item);
        }
        _alerts.EndUpdate();
    }

    private static string FormatDuration(TimeSpan d, CultureInfo c) =>
        d.TotalMinutes >= 1
            ? string.Format(Loc.T("duration.min"), d.TotalMinutes.ToString("F0", c))
            : string.Format(Loc.T("duration.sec"), d.TotalSeconds.ToString("F0", c));
}
