using System.Diagnostics;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Settings;
using CpuMonitorNotifier.Theming;
using CpuMonitorNotifier.Update;

namespace CpuMonitorNotifier;

internal static class Program
{
    private const string UpdatedFlag = "--updated";
    private const int OldProcessExitTimeoutMs = 10_000;
    private const int MutexHandoffTimeoutMs = 10_000;

    [STAThread]
    private static void Main(string[] args)
    {
        int? replacedPid = ParseUpdatedPid(args);
        if (replacedPid is int pid)
            WaitForProcessExit(pid); // до мьютекса: старый экземпляр ещё держит его

        using var mutex = new Mutex(initiallyOwned: true, "CpuMonitorNotifier_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Обычный второй запуск — молча выходим, вторую иконку не создаём. Но после
            // самообновления мьютекс мог ещё держать не успевший умереть старый экземпляр:
            // сдаться здесь значит оставить пользователя вообще без приложения — ровно то,
            // что этот handoff и предотвращает. Поэтому ждём освобождения, а не выходим.
            if (replacedPid is null || !WaitForMutex(mutex, MutexHandoffTimeoutMs))
                return;
        }

        UpdateInstaller.CleanupOldFile(); // остаток прошлого обновления: раньше файл был занят

        var startup = AppSettings.Load();
        Loc.Apply(startup.Language); // локализуем в т.ч. возможное сообщение об ошибке

        ApplicationConfiguration.Initialize();
        ThemeManager.Apply(startup.Theme); // до создания окон
        try
        {
            Application.Run(new App.TrayAppContext());
        }
        catch (Exception ex)
        {
            // типовой случай — отключённые/повреждённые счётчики производительности (lodctr /R лечит)
            MessageBox.Show(
                string.Format(Loc.T("error.startFailed"), ex.Message),
                Loc.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>PID заменённого экземпляра из «--updated &lt;pid&gt;»; null — обычный запуск.</summary>
    private static int? ParseUpdatedPid(string[] args)
    {
        int i = Array.IndexOf(args, UpdatedFlag);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int pid) ? pid : null;
    }

    /// <summary>
    /// После самообновления новый экземпляр стартует, пока старый ещё жив. Без ожидания он увидел бы
    /// занятый мьютекс и молча вышел — пользователь остался бы вообще без приложения.
    /// </summary>
    private static void WaitForProcessExit(int pid)
    {
        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(OldProcessExitTimeoutMs);
        }
        catch (Exception)
        {
            // Процесс уже завершился (ArgumentException), либо к нему нет доступа
            // (Win32Exception при переиспользованном PID защищённого процесса). Ждать нечего,
            // но упасть здесь нельзя: это до создания иконки — приложение просто не запустится.
        }
    }

    /// <summary>Ждёт, пока предыдущий экземпляр отпустит мьютекс. false — не дождались.</summary>
    private static bool WaitForMutex(Mutex mutex, int timeoutMs)
    {
        try
        {
            return mutex.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            return true; // старый процесс умер, не отпустив мьютекс — он всё равно наш
        }
    }
}
