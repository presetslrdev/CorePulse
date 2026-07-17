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

    [STAThread]
    private static void Main(string[] args)
    {
        WaitForReplacedProcess(args); // до мьютекса: старый экземпляр ещё держит его

        using var mutex = new Mutex(initiallyOwned: true, "CpuMonitorNotifier_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // уже запущен — вторую иконку не создаём

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

    /// <summary>
    /// После самообновления новый экземпляр стартует, пока старый ещё жив. Без ожидания он увидел бы
    /// занятый мьютекс и молча вышел — пользователь остался бы вообще без приложения.
    /// </summary>
    private static void WaitForReplacedProcess(string[] args)
    {
        int i = Array.IndexOf(args, UpdatedFlag);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int pid))
            return;

        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(OldProcessExitTimeoutMs);
        }
        catch (ArgumentException)
        {
            // процесс уже завершился — ждать нечего
        }
    }
}
