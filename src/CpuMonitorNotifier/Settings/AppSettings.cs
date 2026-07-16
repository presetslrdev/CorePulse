using System.Text.Json;
using System.Text.Json.Serialization;
using CpuMonitorNotifier.Localization;
using CpuMonitorNotifier.Theming;
using CpuMonitorNotifier.Tray;

namespace CpuMonitorNotifier.Settings;

/// <summary>Настройки приложения. Хранятся в %AppData%\CpuMonitorNotifier\settings.json.</summary>
internal sealed class AppSettings
{
    public float ThresholdPercent { get; set; } = 90f;
    public int DurationSeconds { get; set; } = 60;
    public int CooldownMinutes { get; set; } = 5;
    public bool NotificationsEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 1;
    public TrayIconStyle IconStyle { get; set; } = TrayIconStyle.Liquid;
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    public AppTheme Theme { get; set; } = AppTheme.System;

    // Алерты по «тихому» процессу: держит ≥ ProcessThresholdPercent% одного ядра дольше ProcessDurationMinutes.
    public bool ProcessAlertsEnabled { get; set; } = true;
    public float ProcessThresholdPercent { get; set; } = 25f;
    public int ProcessDurationMinutes { get; set; } = 10;

    // Имена процессов (без .exe), которые не должны вызывать уведомления о нагрузке.
    public List<string> ExcludedProcesses { get; set; } = new();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CpuMonitorNotifier", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }, // IconStyle сохраняем строкой
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // повреждённый файл — стартуем с дефолтами
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
