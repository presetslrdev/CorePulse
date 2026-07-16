using Microsoft.Win32;

namespace CpuMonitorNotifier.Theming;

/// <summary>Тема интерфейса. <see cref="System"/> — как в Windows.</summary>
internal enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Применяет тему к окнам приложения. Системная тема читается из
/// HKCU\...\Themes\Personalize\AppsUseLightTheme (0 = тёмная).
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Текущая выбранная тема (последняя переданная в <see cref="Apply"/>).</summary>
    public static AppTheme Current { get; private set; } = AppTheme.System;

    /// <summary>Тёмная ли тема сейчас фактически (с учётом System → настройки Windows).</summary>
    public static bool IsDarkNow => IsDark(Current);

    /// <summary>В Windows выбрана тёмная тема для приложений?</summary>
    public static bool SystemUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false; // нет ключа/доступа — считаем светлой
        }
    }

    public static bool IsDark(AppTheme theme) => theme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => SystemUsesDarkApps(),
    };

    /// <summary>
    /// Применяет тему к приложению. Влияет на окна, создаваемые после вызова
    /// (окна настроек/истории создаются по требованию, поэтому смена темы подхватывается).
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        Current = theme;
        Application.SetColorMode(theme switch
        {
            AppTheme.Dark => SystemColorMode.Dark,
            AppTheme.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System,
        });
    }
}
