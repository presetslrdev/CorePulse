using System.Diagnostics;
using System.Globalization;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Monitoring;
using Microsoft.Toolkit.Uwp.Notifications;

namespace CpuMonitorNotifier.Notifications;

/// <summary>Windows Toast-уведомления об алертах (работает для unpackaged Win32 на Win10/11).</summary>
internal sealed class ToastNotifier : IDisposable
{
    private const string ActionOpenTaskManager = "openTaskManager";

    public ToastNotifier()
    {
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    public void ShowAlert(LoadAlert alert, IReadOnlyList<ProcessLoad> culprits)
    {
        string cores = string.Join(", ", alert.Cores);
        string titleKey = alert.Cores.Length == 1 ? "toast.title.one" : "toast.title.many";
        string title = string.Format(Loc.T(titleKey), cores, FormatDuration(alert.Duration));

        string body = culprits.Count > 0
            ? string.Format(Loc.T("toast.culprit"), string.Join(", ",
                culprits.Select(c => string.Format(Loc.T("toast.core.load"), c.Name, (c.Cores * 100).ToString("F0", CultureInfo.CurrentCulture)))))
            : Loc.T("toast.culprit.none");

        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .AddButton(new ToastButton()
                .SetContent(Loc.T("toast.button.taskmgr"))
                .AddArgument("action", ActionOpenTaskManager))
            .Show();
    }

    public void ShowProcessAlert(ProcessAlert alert)
    {
        string title = string.Format(Loc.T("toast.proc.title"), alert.Name, FormatDuration(alert.Duration));
        string body = string.Format(Loc.T("toast.proc.body"),
            (alert.Cores * 100).ToString("F0", CultureInfo.CurrentCulture));

        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .AddButton(new ToastButton()
                .SetContent(Loc.T("toast.button.taskmgr"))
                .AddArgument("action", ActionOpenTaskManager))
            .Show();
    }

    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        if (args.TryGetValue("action", out string action) && action == ActionOpenTaskManager)
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
            }
            catch
            {
                // диспетчер задач может быть заблокирован политикой — молча игнорируем
            }
        }
    }

    private static string FormatDuration(TimeSpan d)
    {
        var c = CultureInfo.CurrentCulture;
        return d.TotalMinutes >= 1
            ? string.Format(Loc.T("duration.min"), d.TotalMinutes.ToString("F0", c))
            : string.Format(Loc.T("duration.sec"), d.TotalSeconds.ToString("F0", c));
    }

    public void Dispose()
    {
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        ToastNotificationManagerCompat.History.Clear();
    }
}
