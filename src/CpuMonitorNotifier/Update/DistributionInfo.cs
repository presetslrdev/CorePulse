using System.Reflection;

namespace CpuMonitorNotifier.Update;

/// <summary>Вид сборки. Задаётся при публикации через MSBuild-свойство DistributionKind.</summary>
internal enum DistributionKind
{
    /// <summary>Локальная сборка из исходников — не обновляется.</summary>
    Source,
    SelfContained,
    Framework,
}

/// <summary>
/// Вид сборки впечатывается в саму сборку через AssemblyMetadata, поэтому апдейтеру не нужно
/// угадывать, какой из двух ассетов релиза сейчас запущен.
/// </summary>
internal static class DistributionInfo
{
    public const string SelfContainedAsset = "CorePulse.exe";
    public const string FrameworkAsset = "CorePulse-net10.exe";

    public static DistributionKind Current { get; } = Parse(ReadStamp(Assembly.GetExecutingAssembly()));

    /// <summary>Сборку из исходников подменять нельзя: под разработчиком её собирает msbuild.</summary>
    public static bool UpdatesSupported => Current != DistributionKind.Source;

    /// <summary>Имя ассета в релизе для текущей сборки; null — обновление не поддерживается.</summary>
    public static string? CurrentAssetName => AssetNameFor(Current);

    public static string? AssetNameFor(DistributionKind kind) => kind switch
    {
        DistributionKind.SelfContained => SelfContainedAsset,
        DistributionKind.Framework => FrameworkAsset,
        _ => null,
    };

    /// <summary>Неизвестная или отсутствующая метка трактуется как source — безопасный вариант.</summary>
    public static DistributionKind Parse(string? stamp) => stamp switch
    {
        "self-contained" => DistributionKind.SelfContained,
        "framework" => DistributionKind.Framework,
        _ => DistributionKind.Source,
    };

    private static string? ReadStamp(Assembly asm) => asm
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "DistributionKind")?.Value;
}
