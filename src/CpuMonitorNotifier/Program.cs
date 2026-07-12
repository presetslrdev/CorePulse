namespace CpuMonitorNotifier;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "CpuMonitorNotifier_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // уже запущен — вторую иконку не создаём

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new App.TrayAppContext());
        }
        catch (Exception ex)
        {
            // типовой случай — отключённые/повреждённые счётчики производительности (lodctr /R лечит)
            MessageBox.Show(
                $"Не удалось запустить мониторинг:\n\n{ex.Message}",
                "CPU Monitor Notifier",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
