using System.Diagnostics;
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
        string coresText = alert.Cores.Length == 1
            ? $"Ядро {alert.Cores[0]}"
            : $"Ядра {string.Join(", ", alert.Cores)}";

        string title = $"{coresText} под нагрузкой уже {FormatDuration(alert.Duration)}";
        string body = culprits.Count > 0
            ? "Вероятный виновник: " + string.Join(", ", culprits.Select(c => $"{c.Name} ({c.Cores * 100:F0}% ядра)"))
            : "Виновник не определён (возможно, системный или защищённый процесс)";

        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .AddButton(new ToastButton()
                .SetContent("Диспетчер задач")
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

    private static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1 ? $"{d.TotalMinutes:F0} мин" : $"{d.TotalSeconds:F0} с";

    public void Dispose()
    {
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        ToastNotificationManagerCompat.History.Clear();
    }
}
