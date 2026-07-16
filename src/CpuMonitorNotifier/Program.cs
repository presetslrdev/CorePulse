using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Settings;
using CpuMonitorNotifier.Theming;

namespace CpuMonitorNotifier;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "CpuMonitorNotifier_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // уже запущен — вторую иконку не создаём

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
}
