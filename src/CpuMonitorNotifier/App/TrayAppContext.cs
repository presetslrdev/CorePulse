namespace CpuMonitorNotifier.App;

/// <summary>Хост tray-приложения: иконка, меню, жизненный цикл.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    public TrayAppContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "CPU Monitor Notifier",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}
