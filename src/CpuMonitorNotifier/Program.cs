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
        Application.Run(new App.TrayAppContext());
    }
}
