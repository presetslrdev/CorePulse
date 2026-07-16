using System.Reflection;

namespace CpuMonitorNotifier.Update;

/// <summary>
/// Версии для обновления. Тег релиза («1.5.0») и версия сборки («1.5.0.0») должны сравниваться
/// как равные, поэтому обе приводятся к Major.Minor.Build.
/// </summary>
internal static class UpdateVersions
{
    /// <summary>Версия текущей сборки.</summary>
    public static Version Current { get; } =
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Приводит версию к Major.Minor.Build; неопределённые компоненты (-1) становятся нулями.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Разбирает тег релиза вида «v1.5.0» или «1.5.0». null — если это не версия.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Обновляемся только на строго большую версию — это же защищает от отката при ошибочном теге.</summary>
    public static bool IsNewer(Version current, Version candidate) =>
        Normalize(candidate) > Normalize(current);
}
